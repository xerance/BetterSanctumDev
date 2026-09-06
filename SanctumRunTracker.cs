using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BetterSanctum;

// Per-run statistics. The map alone says everything a room offers - all three slots, and
// the measured quantity for each - so nothing here reads what was actually clicked. What
// was taken is derived from the rooms entered, which FloorData.RoomChoices records, and
// from a stated policy rather than a guess at the player's click.
//
// In-progress state is written to disk after every merge, so restarting the HUD part way
// through a run does not lose it.
public class SanctumRunTracker
{
    private const string RunHeader =
        "when;runId;started;ended;floor;prefix;layers;roomsSeen;roomsEntered;floorCompleted;" +
        "rewardsObscured;roomTypesObscured;afflictionsObscured;" +
        "tier01Visible;tier01Collectable;tier01Skipped;dealRooms;dealTier01;assumedHaul;hubVisits;extraHubVisits";

    private const string RoomHeader =
        "when;runId;floor;layer;room;slot;currency;quantity;tier;fightRoom;rewardRoom;affliction;" +
        "entered;onTier01Route;assumedTake";

    private readonly string _runPath;
    private readonly string _roomPath;
    private readonly string _statePath;
    private DateTime _lastSave = DateTime.MinValue;

    public SanctumRunTracker(string runPath, string roomPath, string statePath)
    {
        _runPath = runPath;
        _roomPath = roomPath;
        _statePath = statePath;
    }

    public RunState Current { get; private set; }

    public bool IsRunning => Current is { Ended: null };

    public string LastError { get; private set; }

    public void StartRun()
    {
        Current = new RunState
        {
            RunId = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            Started = DateTime.Now,
        };

        Save(force: true);
    }

    public void AbandonRun()
    {
        Current = null;
        TryDelete(_statePath);
    }

    // Counted on entering the hub rather than on leaving a floor: leaving can happen by
    // dying, by finishing, or by portalling out, and only one of those is distinguishable
    // from the area you land in.
    public void NoteHubVisit()
    {
        if (!IsRunning)
        {
            return;
        }

        Current.HubVisits++;
        Save(force: true);
    }

    // Merged rather than assigned. Rooms reveal progressively, so a later sighting of the
    // same floor carries rooms and rewards the earlier one did not, and the earlier one
    // may still hold a room since hidden behind smoke.
    public void Merge(FloorObservation incoming)
    {
        if (!IsRunning || incoming == null || incoming.Floor <= 0)
        {
            return;
        }

        var floor = Current.Floor(incoming.Floor, incoming.Prefix);
        floor.LayerCount = Math.Max(floor.LayerCount, incoming.LayerCount);
        floor.RewardsObscured |= incoming.RewardsObscured;
        floor.RoomTypesObscured |= incoming.RoomTypesObscured;
        floor.AfflictionsObscured |= incoming.AfflictionsObscured;

        // The longest choice list wins: it only ever grows within a floor, and a short
        // read is a partial one rather than a correction.
        if (incoming.Choices.Count > floor.Choices.Count)
        {
            floor.Choices = new List<int>(incoming.Choices);
        }

        foreach (var pair in incoming.Rooms)
        {
            if (!floor.Rooms.TryGetValue(pair.Key, out var existing))
            {
                floor.Rooms[pair.Key] = pair.Value;
                continue;
            }

            // Fill blanks only. A room that read as unknown under smoke must not overwrite
            // the same room read plainly a moment earlier.
            var room = pair.Value;
            existing.FightRoomId ??= room.FightRoomId;
            existing.RewardRoomId ??= room.RewardRoomId;
            existing.Affliction ??= room.Affliction;
            if (room.Connections.Count > 0)
            {
                existing.Connections = room.Connections;
            }

            if (room.Slots.Count > existing.Slots.Count)
            {
                existing.Slots = room.Slots;
            }
        }

        Save(force: false);
    }

    // A second route solve, independent of the value-based one the overlay draws: this one
    // maximises the number of tier 0 and 1 rewards reachable, because one room per layer
    // means a floor can show more of them than any single walk can collect. The gap
    // between that and what the floor showed is the skip count.
    private static (int Collectable, HashSet<string> Route) BestTier01Route(FloorObservation floor)
    {
        var layers = floor.LayerCount;
        if (layers <= 0)
        {
            return (0, new HashSet<string>());
        }

        var roomsByLayer = new List<List<RoomObservation>>();
        for (var layer = 0; layer < layers; layer++)
        {
            roomsByLayer.Add(floor.Rooms.Values.Where(x => x.Layer == layer).OrderBy(x => x.Room).ToList());
        }

        // best[key] = most tier 0/1 rewards collectable from that room to the last layer
        var best = new Dictionary<string, (int Value, string Next)>();
        for (var layer = layers - 1; layer >= 0; layer--)
        {
            foreach (var room in roomsByLayer[layer])
            {
                var key = FloorObservation.Key(layer, room.Room);
                if (layer == layers - 1)
                {
                    best[key] = (room.Tier01Count, null);
                    continue;
                }

                // An unrevealed room records no connections, and truncating the route
                // there would understate what the floor could have paid. Treating unknown
                // as "anything in the next layer" keeps the count an honest ceiling.
                var onward = room.Connections.Count > 0
                    ? room.Connections
                    : roomsByLayer[layer + 1].Select(x => x.Room).ToList();

                var bestValue = int.MinValue;
                string bestNext = null;
                foreach (var next in onward)
                {
                    if (!best.TryGetValue(FloorObservation.Key(layer + 1, next), out var candidate))
                    {
                        continue;
                    }

                    if (candidate.Value > bestValue)
                    {
                        bestValue = candidate.Value;
                        bestNext = FloorObservation.Key(layer + 1, next);
                    }
                }

                best[key] = bestValue == int.MinValue
                    ? (room.Tier01Count, null)
                    : (room.Tier01Count + bestValue, bestNext);
            }
        }

        var startKey = roomsByLayer.Count == 0
            ? null
            : roomsByLayer[0]
                .Select(x => FloorObservation.Key(0, x.Room))
                .Where(best.ContainsKey)
                .OrderByDescending(x => best[x].Value)
                .FirstOrDefault();

        var route = new HashSet<string>();
        var total = startKey != null ? best[startKey].Value : 0;
        for (var key = startKey; key != null && best.ContainsKey(key); key = best[key].Next)
        {
            route.Add(key);
        }

        return (total, route);
    }

    // The stated policy, applied rather than observed: the slot worth most is assumed
    // taken. Price when a price lookup is available, since that is the question being
    // asked; otherwise the tier assigned in settings, best tier first and larger quantity
    // breaking ties.
    private static SlotObservation AssumedTake(RoomObservation room, Func<string, double> unitPrice)
    {
        SlotObservation best = null;
        var bestScore = double.MinValue;
        foreach (var slot in room.Slots)
        {
            if (string.IsNullOrEmpty(slot.Currency))
            {
                continue;
            }

            double score;
            if (unitPrice != null)
            {
                score = unitPrice(slot.Currency) * slot.Quantity;
            }
            else
            {
                // Tiers run 0 best to 8 worst, so they have to be inverted to score
                score = (8 - slot.Tier) * 1000.0 + slot.Quantity;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = slot;
            }
        }

        return best;
    }

    private static string DescribeHaul(IEnumerable<SlotObservation> taken)
    {
        return string.Join(", ", taken
            .Where(x => x != null && !string.IsNullOrEmpty(x.Currency))
            .GroupBy(x => x.Currency)
            .Select(g => $"{g.Sum(x => x.Quantity)} {g.Key}")
            .OrderBy(x => x, StringComparer.Ordinal));
    }

    // Writes the run out and clears it. Returns how many floor rows were written, or -1 if
    // nothing could be written, so the caller can say so rather than silently succeeding.
    public int EndRun(Func<string, double> unitPrice)
    {
        if (Current == null)
        {
            return -1;
        }

        Current.Ended = DateTime.Now;
        var run = Current;
        try
        {
            var runRows = new List<string>();
            var roomRows = new List<string>();

            // A complete run passes through the hub once per floor, so that is the
            // baseline an informative count is measured against.
            var expectedHubVisits = run.Floors.Count;

            foreach (var floor in run.Floors.Values.OrderBy(x => x.Floor))
            {
                var (collectable, route) = BestTier01Route(floor);
                var visible = floor.Rooms.Values.Sum(x => x.Tier01Count);
                var entered = new HashSet<string>();
                for (var layer = 0; layer < floor.Choices.Count; layer++)
                {
                    entered.Add(FloorObservation.Key(layer, floor.Choices[layer]));
                }

                var taken = new List<SlotObservation>();
                foreach (var room in floor.Rooms.Values.OrderBy(x => x.Layer).ThenBy(x => x.Room))
                {
                    var key = FloorObservation.Key(room.Layer, room.Room);
                    var isEntered = entered.Contains(key);
                    var assumed = isEntered ? AssumedTake(room, unitPrice) : null;
                    if (assumed != null)
                    {
                        taken.Add(assumed);
                    }

                    foreach (var slot in room.Slots.OrderBy(x => x.Slot))
                    {
                        roomRows.Add(Row(
                            run.RunId, floor.Floor, room.Layer, room.Room, slot.Slot,
                            slot.Currency, slot.Quantity, slot.Tier,
                            room.FightRoomId, room.RewardRoomId, room.Affliction,
                            isEntered, route.Contains(key), assumed != null && assumed.Slot == slot.Slot));
                    }
                }

                var dealRooms = floor.Rooms.Values.Where(x => x.IsDeal).ToList();
                runRows.Add(Row(
                    run.RunId, run.Started.ToString("s"), run.Ended?.ToString("s"),
                    floor.Floor, floor.Prefix, floor.LayerCount,
                    floor.Rooms.Count, floor.Choices.Count,
                    floor.LayerCount > 0 && floor.Choices.Count >= floor.LayerCount,
                    floor.RewardsObscured, floor.RoomTypesObscured, floor.AfflictionsObscured,
                    visible, collectable, Math.Max(visible - collectable, 0),
                    dealRooms.Count, dealRooms.Sum(x => x.Tier01Count),
                    DescribeHaul(taken),
                    run.HubVisits, Math.Max(run.HubVisits - expectedHubVisits, 0)));
            }

            Append(_runPath, RunHeader, runRows);
            Append(_roomPath, RoomHeader, roomRows);
            Current = null;
            TryDelete(_statePath);
            LastError = null;
            return runRows.Count;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return -1;
        }
    }

    // Semicolon separated to match the existing reward log, so anything separating a
    // value has to go. Booleans are written as 0/1, which a spreadsheet sums.
    private static string Row(params object[] values)
    {
        return string.Join(";", values.Select(value => value switch
        {
            null => "",
            bool flag => flag ? "1" : "0",
            int number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString().Replace(";", ",").Replace("\n", " ").Replace("\r", " "),
        }));
    }

    private static void Append(string path, string header, List<string> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var stamp = DateTime.Now.ToString("s");
        if (!File.Exists(path))
        {
            File.AppendAllText(path, header + Environment.NewLine);
        }

        File.AppendAllLines(path, rows.Select(row => $"{stamp};{row}"));
    }

    // Throttled, because a merge happens every frame the map is open and this is disk.
    // A forced save is used where losing the last few seconds would actually cost
    // something - starting, ending, and entering the hub.
    public void Save(bool force)
    {
        if (Current == null)
        {
            return;
        }

        if (!force && DateTime.Now - _lastSave < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastSave = DateTime.Now;
        try
        {
            File.WriteAllText(_statePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
    }

    // Restores a run left behind by a HUD restart. A run that was already ended is not
    // resumed, since its rows are written and resuming would double count.
    public void Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            var restored = JsonConvert.DeserializeObject<RunState>(File.ReadAllText(_statePath));
            if (restored is { Ended: null } && !string.IsNullOrEmpty(restored.RunId))
            {
                Current = restored;
            }
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A leftover state file is resumed only while it has no end time, so failing
            // to delete one costs nothing worse than a stale file on disk.
        }
    }
}
