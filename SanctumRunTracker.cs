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
    // One row per run, and only what a run is actually judged on: how long it took, what
    // each floor paid, what the deals in it gave up, how many high rewards were on offer
    // whether or not a route could reach them, and whether either of the two afflictions
    // that make a run's numbers worth setting aside turned up.
    //
    // Everything finer is in the room file, a row per room and slot.
    private const string RunHeader =
        "when,runId,duration,floor1,floor2,floor3,floor4," +
        "dealsEntered,dealCurrency,highRewardsSeen," +
        "goldenSmoke,goldenSmokeFloor,deceptiveMirror,deceptiveMirrorFloor";

    // Afflictions worth a column of their own rather than a line in a list: one hides the
    // rewards the routing is built on, the other sends you somewhere you did not choose,
    // and either makes a run's numbers worth setting aside.
    private const string GoldenSmoke = "Golden Smoke";
    private const string DeceptiveMirror = "Deceptive Mirror";

    // A row per room now, not per reward slot: a room with no rewards - a deal, a fountain,
    // the boss - carried none and so appeared nowhere, which made the room-type columns
    // impossible to count against and left roomsSeen unexplainable.
    // source says where the row came from: "map" is what the floor map showed, "window" is
    // what the reward window said while you stood in the room. A Deal only ever produces
    // the second, since the map reads its rewards as null.
    private const string RoomHeader =
        "when,runId,floor,layer,room,source,slot,currency,quantity,tier,fightRoom,rewardRoom,roomAffliction," +
        "entered,onTier01Route,assumedTake,slotValue,offerText";

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

    // Recorded against the room you are standing in rather than a room on the map, since
    // the map is shut while the reward window is open. Merged by slot: the window is read
    // every frame it is up, and the same three lines arrive over and over.
    public string NoteOffers(int floor, string prefix, int layer, int room, List<OfferObservation> offers)
    {
        if (!IsRunning)
        {
            return "no run";
        }

        if (floor <= 0 || offers == null || offers.Count == 0)
        {
            return "nothing to record";
        }

        var floorState = Current.Floor(floor, prefix);

        // The room is validated against the floor already mapped rather than against the
        // resolve reading beside it, which is stale at exactly the moment the reward
        // window is open. A layer past the end of the floor, or a negative one, is a bad
        // read of RoomChoices and not a room anyone is standing in.
        if (layer < 0 || (floorState.LayerCount > 0 && layer >= floorState.LayerCount))
        {
            return $"layer {layer} outside floor of {floorState.LayerCount}";
        }

        var key = FloorObservation.Key(layer, room);
        if (!floorState.Rooms.TryGetValue(key, out var observation))
        {
            // A room can be stood in before the map ever showed it - rooms reveal a few
            // layers ahead - so an unknown room is created rather than dropped, as long as
            // its layer is one this floor actually has.
            observation = new RoomObservation { Layer = layer, Room = room };
            floorState.Rooms[key] = observation;
        }

        foreach (var offer in offers)
        {
            var existing = observation.Offers.FirstOrDefault(x => x.Slot == offer.Slot);
            if (existing == null)
            {
                observation.Offers.Add(offer);
                continue;
            }

            // A later read only wins where it actually says more: the window can be caught
            // part drawn, with the text not yet filled in.
            if (!string.IsNullOrWhiteSpace(offer.Text) && offer.Text.Length > (existing.Text?.Length ?? 0))
            {
                existing.Text = offer.Text;
                existing.Currency = offer.Currency;
                existing.Quantity = offer.Quantity;
                existing.Tier = offer.Tier;
            }
        }

        Save(force: false);
        return $"recorded {observation.Offers.Count}";
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
    // asked; otherwise the value band recorded with the slot, best band first and larger
    // quantity breaking ties.
    // Every slot's score is written out beside it, so which slot the policy picked and why
    // is auditable from the file rather than being a number to take on trust.
    private static double SlotValue(SlotObservation slot, Func<string, double> unitPrice)
    {
        if (slot == null || string.IsNullOrEmpty(slot.Currency))
        {
            return 0;
        }

        if (unitPrice != null)
        {
            var chaos = unitPrice(slot.Currency) * slot.Quantity;
            if (chaos > 0)
            {
                return chaos;
            }
        }

        // No price for this currency is not the same as no price at all: a lookup that
        // knows chaos but not chromatics would otherwise score the chromatics zero and
        // hand the room to whichever slot came first. Bands run 0 best to 5 worst, so
        // they invert, and are kept negative so a real chaos value always outranks them.
        return -((slot.Tier + 1) * 1000.0) + slot.Quantity;
    }

    // The map's slots where it has them, the reward window's where it does not. A Deal
    // reads its rewards as empty on the map, so without this the one room whose contents
    // only the window can see would contribute nothing to what the run produced.
    private static List<SlotObservation> TakeableSlots(RoomObservation room)
    {
        if (room.Slots.Count > 0)
        {
            return room.Slots;
        }

        return room.Offers
            .Select(offer => new SlotObservation
            {
                Slot = offer.Slot,
                Currency = offer.Currency,
                Quantity = offer.Quantity,
                Tier = offer.Tier,
            })
            .ToList();
    }

    private static SlotObservation AssumedTake(RoomObservation room, Func<string, double> unitPrice)
    {
        SlotObservation best = null;
        var bestScore = double.MinValue;
        foreach (var slot in TakeableSlots(room))
        {
            if (string.IsNullOrEmpty(slot.Currency))
            {
                continue;
            }

            var score = SlotValue(slot, unitPrice);
            if (score > bestScore)
            {
                bestScore = score;
                best = slot;
            }
        }

        return best;
    }

    // Golden, Red and Purple Smoke are not player buffs - a full run of buff samples holds
    // no Sanctum entry at all - so the only readable trace of them is the room that grants
    // one. An affliction is gained by entering the room carrying it and then lasts the run,
    // so the rooms actually entered say which were active.
    //
    // A lower bound, deliberately: afflictions also arrive from deals, curse fountains and
    // Accursed Prism, none of which are visible here. A set flag is therefore trustworthy
    // and a clear one is only the absence of evidence. Every affliction picked up this way
    // is written out whole in afflictionsTaken, so the three smoke flags can be checked
    // against the list rather than believed.

    // Chaos, plus anything worth five chaos a unit or more, richest first. A floor pays a
    // long tail of alteration and chance that says nothing about how it went, and reading
    // past it to find the divine was most of the work of reading the file at all.
    //
    // Without a price lookup nothing can be filtered, so nothing is: dropping every
    // currency because none of them has a price would be worse than the clutter.
    private const double HaulFloorChaos = 5.0;

    private static string DescribeHaul(IEnumerable<SlotObservation> taken, Func<string, double> unitPrice)
    {
        var grouped = taken
            .Where(x => x != null && !string.IsNullOrEmpty(x.Currency))
            .GroupBy(x => x.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Unit = unitPrice?.Invoke(g.Key) ?? 0,
            })
            .Where(x => unitPrice == null || x.Currency == "Chaos Orbs" || x.Unit >= HaulFloorChaos)
            .OrderByDescending(x => x.Unit * x.Quantity)
            .ThenByDescending(x => x.Quantity)
            .Select(x => $"{x.Quantity} {x.Currency}");

        return string.Join(", ", grouped);
    }

    // "14m37s", with an hour only where there is one. A run is minutes long, so a decimal
    // count of them reads as a number to convert rather than a duration to glance at.
    private static string DescribeDuration(TimeSpan? span)
    {
        if (span is not { } elapsed || elapsed < TimeSpan.Zero)
        {
            return "";
        }

        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h{elapsed.Minutes:00}m{elapsed.Seconds:00}s"
            : $"{elapsed.Minutes}m{elapsed.Seconds:00}s";
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


            // Afflictions last the run, so they carry from floor to floor rather than
            // being read afresh on each.
            var afflictionsTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Gathered across every floor, since the run is what the row is about
            var takesByFloor = new Dictionary<int, List<SlotObservation>>();
            var dealTakes = new List<SlotObservation>();
            var afflictionFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var highRewardsSeen = 0;
            var dealsEntered = 0;

            foreach (var floor in run.Floors.Values.OrderBy(x => x.Floor))
            {
                var (_, route) = BestTier01Route(floor);
                var entered = new HashSet<string>();
                for (var layer = 0; layer < floor.Choices.Count; layer++)
                {
                    entered.Add(FloorObservation.Key(layer, floor.Choices[layer]));
                }

                foreach (var room in floor.Rooms.Values.OrderBy(x => x.Layer).ThenBy(x => x.Room))
                {
                    var key = FloorObservation.Key(room.Layer, room.Room);

                    // Having read the reward window in a room is proof of having stood in
                    // it, and better proof than the choice list: the map only opens before
                    // a room is entered, so the last room of a floor never appears there.
                    var isEntered = entered.Contains(key) || room.Offers.Count > 0;
                    var onRoute = route.Contains(key);
                    var assumed = isEntered ? AssumedTake(room, unitPrice) : null;
                    if (assumed != null)
                    {
                        if (!takesByFloor.TryGetValue(floor.Floor, out var floorTakes))
                        {
                            floorTakes = new List<SlotObservation>();
                            takesByFloor[floor.Floor] = floorTakes;
                        }

                        floorTakes.Add(assumed);
                    }

                    // Counted whether or not a route could reach it: a floor can show more
                    // than one walk can collect, and what was on offer is the question.
                    // Per room rather than per slot, since one room pays one reward however
                    // many of its slots are worth having.
                    if (TakeableSlots(room).Any(x => x.Tier <= 1))
                    {
                        highRewardsSeen++;
                    }
                    if (isEntered && room.Affliction != null)

                    {
                        afflictionsTaken.Add(room.Affliction);
                    }

                    // A room with no rewards still gets a row, so deals, fountains and
                    // bosses can be counted rather than vanishing from the file.
                    if (room.Slots.Count == 0 && room.Offers.Count == 0)
                    {
                        roomRows.Add(Row(
                            run.RunId, floor.Floor, room.Layer, room.Room, "map", null,
                            null, null, null,
                            room.FightRoomId, room.RewardRoomId, room.Affliction,
                            isEntered, onRoute, false, null, null));
                        continue;
                    }

                    foreach (var slot in room.Slots.OrderBy(x => x.Slot))
                    {
                        roomRows.Add(Row(
                            run.RunId, floor.Floor, room.Layer, room.Room, "map", slot.Slot,
                            slot.Currency, slot.Quantity, slot.Tier,
                            room.FightRoomId, room.RewardRoomId, room.Affliction,
                            isEntered, onRoute, assumed != null && assumed.Slot == slot.Slot,
                            Math.Round(SlotValue(slot, unitPrice), 2), null));
                    }

                    // Window rows sit alongside the map rows rather than replacing them,
                    // so where both exist the two readings can be compared.
                    foreach (var offer in room.Offers.OrderBy(x => x.Slot))
                    {
                        roomRows.Add(Row(
                            run.RunId, floor.Floor, room.Layer, room.Room, "window", offer.Slot,
                            offer.Currency, offer.Quantity, offer.Tier,
                            room.FightRoomId, room.RewardRoomId, room.Affliction,
                            isEntered, onRoute, false,
                            Math.Round(SlotValue(new SlotObservation
                            {
                                Currency = offer.Currency,
                                Quantity = offer.Quantity,
                                Tier = offer.Tier,
                            }, unitPrice), 2),
                            offer.Text));
                    }
                }

                // A deal reads as empty on the map, so what it gave up is known only from
                // the reward window, and only for one you actually walked into.
                foreach (var deal in floor.Rooms.Values.Where(x =>
                             x.IsDeal && entered.Contains(FloorObservation.Key(x.Layer, x.Room))))
                {
                    dealsEntered++;
                    var dealTake = AssumedTake(deal, unitPrice);
                    if (dealTake != null)
                    {
                        dealTakes.Add(dealTake);
                    }
                }

                // The floor an affliction was first seen on, which says when a run stopped
                // being worth measuring rather than merely that it did.
                foreach (var name in new[] { GoldenSmoke, DeceptiveMirror })
                {
                    if (!afflictionFloors.ContainsKey(name) && afflictionsTaken.Contains(name))
                    {
                        afflictionFloors[name] = floor.Floor;
                    }
                }
            }

            var goldenFloor = afflictionFloors.GetValueOrDefault(GoldenSmoke, 0);
            var mirrorFloor = afflictionFloors.GetValueOrDefault(DeceptiveMirror, 0);
            runRows.Add(Row(
                run.RunId,
                DescribeDuration(run.Ended - run.Started),
                DescribeHaul(takesByFloor.GetValueOrDefault(1) ?? new List<SlotObservation>(), unitPrice),
                DescribeHaul(takesByFloor.GetValueOrDefault(2) ?? new List<SlotObservation>(), unitPrice),
                DescribeHaul(takesByFloor.GetValueOrDefault(3) ?? new List<SlotObservation>(), unitPrice),
                DescribeHaul(takesByFloor.GetValueOrDefault(4) ?? new List<SlotObservation>(), unitPrice),
                dealsEntered, DescribeHaul(dealTakes, unitPrice), highRewardsSeen,
                goldenFloor > 0, goldenFloor > 0 ? goldenFloor : (object)null,
                mirrorFloor > 0, mirrorFloor > 0 ? mirrorFloor : (object)null));

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

    // Comma separated and quoted the ordinary way, which is what a spreadsheet opens
    // without being told anything. The old semicolons matched the reward log and cost a
    // separator dialog on every import; a list like "2 Divine Orbs, 14 Chaos Orbs" is now
    // quoted rather than having its commas stripped out of it.
    //
    // Booleans are 0/1, which a spreadsheet can sum.
    private static string Row(params object[] values)
    {
        return string.Join(",", values.Select(Field));
    }

    private static string Field(object value)
    {
        var text = value switch
        {
            null => "",
            bool flag => flag ? "1" : "0",
            int number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Contains(',') || text.Contains('"')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
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

        File.AppendAllLines(path, rows.Select(row => $"{stamp},{row}"));
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
