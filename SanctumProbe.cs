using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements.Sanctum;

namespace BetterSanctum;

// A throwaway investigation aid for the two things the run tracker needs and the room
// dump cannot reach: what the hub area is actually called, and whether the game exposes
// the rewards accrued so far rather than only the three offers in front of you.
//
// Writes an append-only log so a whole session - hub, four floors, back to the hub -
// lands in one file in order. Everything is read defensively: this runs outside a
// Sanctum too, where most of it is expected to be absent.
public class SanctumProbe
{
    private readonly string _path;
    private string _lastStateKey;
    private DateTime _lastStateWrite = DateTime.MinValue;

    public SanctumProbe(string path)
    {
        _path = path;
    }

    private void Write(string line)
    {
        try
        {
            File.AppendAllText(_path, $"{DateTime.Now:s} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // The probe is diagnostics; a locked file must not take the overlay down
        }
    }

    // Unknown 3. Golden, Red and Purple Smoke hide parts of the floor map, so a run under
    // one of them undercounts rather than reporting nothing, and has to be filterable.
    // They are player afflictions rather than room effects, and nothing found so far says
    // where the active ones live - buffs are the likeliest place, so this writes the
    // player's buff names out to be read against the affliction list.
    //
    // Sampled once per area rather than on change: buffs churn constantly in combat, an
    // affliction lasts the floor, so one sample per room says what is needed and keeps
    // the file readable.
    // Whether the reward window was recorded against a room, and if not, what stopped it.
    // The capture failed silently for a week of runs because nothing said either way.
    public void LogOfferCapture(int floor, int layer, int room, int offerCount, string outcome)
    {
        Write($"offercapture floor={floor} layer={layer} room={room} offers={offerCount} -> {outcome}");
    }

    public void LogBuffs(IEnumerable<string> buffNames)
    {
        try
        {
            var names = buffNames?.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            Write($"buffs [{(names == null ? "<null>" : string.Join(", ", names))}]");
        }
        catch (Exception e)
        {
            Write($"buffs <{e.GetType().Name}>");
        }
    }

    // Unknown 1. The hub is a static zone entered from the map device rather than from a
    // floor, so it never appears while the floor map is open. Both Id and RawName are
    // logged: Id is the internal identifier and the better thing to key on if they differ.
    public void LogAreaChange(AreaInstance area, SanctumFloorWindow floorWindow)
    {
        if (area?.Area == null)
        {
            Write("area <null>");
            return;
        }

        Write($"area id={area.Area.Id} raw={area.Area.RawName} name={area.Area.Name} " +
              $"level={area.Area.AreaLevel} town={area.IsTown} hideout={area.IsHideout} " +
              $"instance={area.InstanceId} | {DescribeFloorWindow(floorWindow)}");
    }

    // Whether the floor window and its data survive outside a floor decides whether the
    // hub can be recognised by state rather than by area name, and whether accrued
    // rewards can be read at the end of a run.
    private static string DescribeFloorWindow(SanctumFloorWindow floorWindow)
    {
        if (floorWindow == null)
        {
            return "floorWindow=<null>";
        }

        try
        {
            var data = floorWindow.FloorData;
            return $"floorWindow addr={floorWindow.Address:X} valid={floorWindow.IsValid} " +
                   $"visible={floorWindow.IsVisible} floorData={(data == null ? "null" : $"{data.Address:X}")}";
        }
        catch (Exception e)
        {
            return $"floorWindow=<{e.GetType().Name}>";
        }
    }

    // Unknown 2. SanctumFloorData.Rewards is a List<SanctumDeferredReward>, and that type
    // carries a Count and a DeferralCategory alongside the currency - which is the shape
    // accrued-but-not-yet-paid rewards would take. This logs it whenever it changes, so
    // taking an offer should show up as a new or grown entry, and the end of a floor
    // should show the deferral bucket emptying.
    public void LogState(SanctumFloorWindow floorWindow, string areaRawName)
    {
        // Rate limited rather than per frame, and written only on change: the interesting
        // signal is the transition, not the steady state.
        if (DateTime.Now - _lastStateWrite < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        _lastStateWrite = DateTime.Now;

        string key;
        try
        {
            key = DescribeState(floorWindow, areaRawName);
        }
        catch (Exception e)
        {
            key = $"state=<{e.GetType().Name}: {e.Message}>";
        }

        if (key == _lastStateKey)
        {
            return;
        }

        _lastStateKey = key;
        Write(key);
    }

    private static string DescribeState(SanctumFloorWindow floorWindow, string areaRawName)
    {
        if (floorWindow == null)
        {
            return $"state area={areaRawName} floorWindow=<null>";
        }

        var data = floorWindow.FloorData;
        if (data == null)
        {
            return $"state area={areaRawName} {DescribeFloorWindow(floorWindow)} floorData=<null>";
        }

        var parts = new List<string>
        {
            $"state area={areaRawName}",
            $"windowVisible={floorWindow.IsVisible}",
            $"floorData={data.Address:X}",
            $"gold={data.Gold}",
            $"resolve={data.CurrentResolve}/{data.MaxResolve}",
            $"inspiration={data.Inspiration}",
            $"choices=[{DescribeChoices(data)}]",
            $"rewards=[{DescribeRewards(data)}]",
        };

        return string.Join(" ", parts);
    }

    private static string DescribeChoices(SanctumFloorData data)
    {
        try
        {
            return data.RoomChoices is IEnumerable choices
                ? string.Join(",", choices.Cast<object>().Select(x => Convert.ToInt32(x)))
                : "<absent>";
        }
        catch (Exception e)
        {
            return $"<{e.GetType().Name}>";
        }
    }

    // The answer to unknown 2 in one string: currency, how many, and which deferral
    // bucket it is sitting in.
    private static string DescribeRewards(SanctumFloorData data)
    {
        try
        {
            var rewards = data.Rewards;
            if (rewards == null)
            {
                return "<null>";
            }

            return string.Join(" | ", rewards.Select(reward =>
            {
                if (reward == null)
                {
                    return "<null>";
                }

                var currency = reward.RewardCategory?.CurrencyName ?? reward.RewardCategory?.BaseType?.BaseName ?? "?";
                return $"{reward.Count}x {currency} id={reward.Id} deferral={reward.DeferralCategory?.Id ?? "?"}";
            }));
        }
        catch (Exception e)
        {
            return $"<{e.GetType().Name}: {e.Message}>";
        }
    }
}
