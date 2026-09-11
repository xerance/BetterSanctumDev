using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BetterSanctumDev;

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
    // it paid in total, what the deals in it gave up, how many high rewards were on offer
    // whether or not a route could reach them, and whether either of the two afflictions
    // that make a run's numbers worth setting aside turned up.
    //
    // Everything finer is in the room file, a row per room and slot.
    private const string RunHeader =
        "when,runId,duration,currency," +
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

    // Every offer of every deal walked into, on any floor, with nothing filtered out. The
    // run file reports deals under the same rules as the rest of the haul, which drops
    // most of what a deal actually pays - deals deal in bulk, and a unit price floor aimed
    // at a run's long tail removes nearly all of it. This is the raw record to answer
    // "what is a deal worth" from, once there is enough of it to answer from.
    //
    // Every floor, not floor 3 up, because whether an early deal is worth taking is one of
    // the questions, and a file that had already decided could not answer it.
    private const string DealHeader =
        "when,runId,floor,layer,room,slot,currency,quantity,chaos,taken,offerText,list";

    // The run row again, one row per currency instead of three cells of comma-joined text.
    // A spreadsheet cannot pivot or chart "3 Volatile Vaal Orbs, 1 Divine Orbs" - it is one
    // string - so the same figures are written a second time in a shape it can group by.
    //
    // source is "run" for the haul, "deal" for the part of it that came out of a deal, and
    // "seen" for high rewards the floor showed whether or not they were reachable. Summing
    // run and deal together double counts, since deal is part of run.
    private const string CurrencyHeader =
        "when,runId,source,currency,quantity,unitChaos,totalChaos,list";

    // A column per currency: the shape a spreadsheet charts without being reshaped first,
    // where the long file above is the shape it pivots.
    //
    // Two rows a run - what the run paid, then what the deals in it paid, in the same
    // columns. A deal is a room, so the deal row is part of the run row rather than an
    // addition to it: filter on source rather than summing both. Only the run row carries
    // the date, since repeating it reads at a glance as a second run.
    //
    // Counts only. What a haul was worth is a valuation rather than something that dropped,
    // and a fractional chaos figure beside a column of quantities invites being read as a
    // number of orbs. Value lives in sanctum-run-currency.csv, per currency.
    //
    // Which currencies get a column is a setting and can change; which ones a given file
    // has cannot, so a file keeps the columns it was started with and only a new file picks
    // up a new set. A currency the run did not pay is a zero, which is what makes a column
    // summable straight down. run counts the rows already in the file, so it is a stable x
    // axis across runs.
    //
    // No count of deal rooms. Sitting in front of the currency columns and named like one,
    // it read as a currency, and the deal row already says a run had deals in it. The count
    // itself is not lost - dealsEntered is a column of sanctum-runs.csv.
    //
    // runId trails everything, out of the way of what is worth reading, because it is the
    // key the other three files join on and dropping it would strand them.
    private const string WideFixedHeader = "date,run,source,duration";
    private const string WideTrailingHeader = "runId";

    // Resolved on every use rather than held, because the tracking profile decides which
    // folder these go in and it can be switched between runs. A profile is a set of
    // columns, and two sets of columns cannot share a file, so each gets its own.
    private readonly Func<string, string> _trackingFile;

    // Outside the profile folders, and for different reasons.
    //
    // An unfinished run belongs to the run rather than to whichever profile was selected
    // when it started.
    //
    // The deal and currency files are one dataset rather than one per list. Their columns
    // are fixed, so unlike the wide file nothing in them depends on which currencies a list
    // tracks, and what a deal is worth is a question answered by volume. Splitting them per
    // list would divide the files that need every row they can get.
    //
    // Both carry the list that wrote each row instead. They have to: the duplicate-run rule
    // changes which slot counts as taken, so a row from one list does not mean quite what
    // the same row from another does, and a shared file that could not say which was which
    // would be a file you cannot safely average.
    private readonly string _dealPath;
    private readonly string _currencyPath;
    private readonly string _statePath;
    private readonly Func<string> _listName;

    private DateTime _lastSave = DateTime.MinValue;

    public SanctumRunTracker(
        Func<string, string> trackingFile,
        string dealPath,
        string currencyPath,
        string statePath,
        Func<string> listName)
    {
        _trackingFile = trackingFile;
        _dealPath = dealPath;
        _currencyPath = currencyPath;
        _statePath = statePath;
        _listName = listName;
    }

    private string RunPath => _trackingFile("sanctum-runs.csv");
    private string RoomPath => _trackingFile("sanctum-run-rooms.csv");
    private string DealPath => _dealPath;
    private string CurrencyPath => _currencyPath;
    private string ListName => _listName?.Invoke() ?? "";
    private string WidePath => _trackingFile("sanctum-run-wide.csv");

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

    public bool IsPaused => IsRunning && Current.PausedAt != null;

    public void Pause()
    {
        if (!IsRunning || Current.PausedAt != null)
        {
            return;
        }

        Current.PausedAt = DateTime.Now;
        Save(force: true);
    }

    // Safe to call every frame: it does nothing unless a pause is open
    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        Current.Paused += DateTime.Now - Current.PausedAt.Value;
        Current.PausedAt = null;
        Save(force: true);
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

    // What a slot is worth for a reader rather than for a comparison. The figure above is
    // a ranking, and where there is no price it is a sentinel in the thousands - which is
    // fine for picking the best slot and poison in a column somebody sums. Blank instead,
    // which a spreadsheet skips rather than averaging in as a value that was measured.
    private static object SlotChaos(SlotObservation slot, Func<string, double> unitPrice)
    {
        var chaos = SlotValue(slot, unitPrice);
        return chaos > 0 ? Math.Round(chaos, 2) : null;
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

    // canTake is the run type's own rule about which slots are worth taking at all. On a
    // duplicate run the deferred slots are crossed out in the offer window, so assuming
    // the richest slot regardless would credit the run with currency it would never have
    // taken - the exact reward the overlay was telling you to walk past.
    private static SlotObservation AssumedTake(
        RoomObservation room,
        Func<string, double> unitPrice,
        Func<int, int, string, bool> canTake,
        int floor)
    {
        SlotObservation best = null;
        var bestScore = double.MinValue;
        foreach (var slot in TakeableSlots(room))
        {
            if (string.IsNullOrEmpty(slot.Currency))
            {
                continue;
            }

            if (canTake != null && !canTake(floor, slot.Slot, slot.Currency))
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

    // Band 1 is a divine or more, band 0 five divine or more. What counts as a reward
    // worth noting having seen, whether or not a route could reach it.
    private const int HighRewardBand = 1;

    private static string DescribeHaul(IEnumerable<SlotObservation> taken, Func<string, double> unitPrice)
    {
        return Describe(taken, unitPrice,
            (currency, unit) => unitPrice == null || currency == "Chaos Orbs" || unit >= HaulFloorChaos);
    }

    // Grouped by currency and summed, richest first. What counts as worth listing is the
    // caller's, since the run haul and the high rewards seen are cut at different lines.
    private static string Describe(
        IEnumerable<SlotObservation> slots,
        Func<string, double> unitPrice,
        Func<string, double, bool> keep)
    {
        var grouped = slots
            .Where(x => x != null && !string.IsNullOrEmpty(x.Currency))
            .GroupBy(x => x.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Unit = unitPrice?.Invoke(g.Key) ?? 0,
            })
            .Where(x => keep(x.Currency, x.Unit))
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
    public int EndRun(
        Func<string, double> unitPrice,
        Func<int, int, string, bool> canTake = null,
        IReadOnlyList<string> wideColumns = null)
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
            var dealRows = new List<string>();


            // Afflictions last the run, so they carry from floor to floor rather than
            // being read afresh on each.
            var afflictionsTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Gathered across every floor, since the run is what the row is about
            var runTakes = new List<SlotObservation>();
            var dealTakes = new List<SlotObservation>();
            var afflictionFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var highRewardsSeen = new List<SlotObservation>();
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
                    var assumed = isEntered ? AssumedTake(room, unitPrice, canTake, floor.Floor) : null;
                    if (assumed != null)
                    {
                        runTakes.Add(assumed);
                    }

                    // Collected whether or not a route could reach it: a floor can show
                    // more than one walk can collect, and what was on offer is the
                    // question. Per room rather than per slot, since one room pays one
                    // reward however many of its slots are worth having - and the richest
                    // of them regardless of the run type, since this is what the floor
                    // held rather than what you would have taken.
                    var richest = TakeableSlots(room)
                        .Where(x => x.Tier <= HighRewardBand)
                        .OrderByDescending(x => SlotValue(x, unitPrice))
                        .FirstOrDefault();
                    if (richest != null)
                    {
                        highRewardsSeen.Add(richest);
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
                            SlotChaos(slot, unitPrice), null));
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
                            SlotChaos(new SlotObservation
                            {
                                Currency = offer.Currency,
                                Quantity = offer.Quantity,
                                Tier = offer.Tier,
                            }, unitPrice),
                            offer.Text));
                    }
                }

                // A deal reads as empty on the map, so what it gave up is known only from
                // the reward window, and only for one you actually walked into.
                //
                // Floor 3 and up only. An early deal pays too little to be worth counting
                // beside a late one, and averaging the two together would say less about
                // either than leaving the early ones out says on its own.
                foreach (var deal in floor.Rooms.Values.Where(x =>
                             floor.Floor >= 3 &&
                             x.IsDeal &&
                             entered.Contains(FloorObservation.Key(x.Layer, x.Room))))
                {
                    dealsEntered++;
                    var dealTake = AssumedTake(deal, unitPrice, canTake, floor.Floor);
                    if (dealTake != null)
                    {
                        dealTakes.Add(dealTake);
                    }
                }

                // Written from every floor, unfiltered, whatever the run row decided to
                // report. An entered deal with no rows is one whose window never opened.
                foreach (var deal in floor.Rooms.Values.Where(x =>
                             x.IsDeal && entered.Contains(FloorObservation.Key(x.Layer, x.Room))))
                {
                    var best = AssumedTake(deal, unitPrice, canTake, floor.Floor);
                    foreach (var offer in deal.Offers.OrderBy(x => x.Slot))
                    {
                        var slot = new SlotObservation
                        {
                            Slot = offer.Slot,
                            Currency = offer.Currency,
                            Quantity = offer.Quantity,
                            Tier = offer.Tier,
                        };

                        dealRows.Add(Row(
                            run.RunId, floor.Floor, deal.Layer, deal.Room, offer.Slot,
                            offer.Currency, offer.Quantity,
                            SlotChaos(slot, unitPrice),
                            best != null && best.Slot == offer.Slot,
                            offer.Text,
                            ListName));
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
                DescribeDuration(run.Elapsed(run.Ended)),
                DescribeHaul(runTakes, unitPrice),
                // Already cut at a divine by band, so nothing further is filtered out
                dealsEntered, DescribeHaul(dealTakes, unitPrice),
                Describe(highRewardsSeen, unitPrice, (_, _) => true),
                goldenFloor > 0, goldenFloor > 0 ? goldenFloor : (object)null,
                mirrorFloor > 0, mirrorFloor > 0 ? mirrorFloor : (object)null));

            var currencyRows = new List<string>();
            currencyRows.AddRange(CurrencyRows(run.RunId, "run", runTakes, unitPrice,
                (currency, unit) => unitPrice == null || currency == "Chaos Orbs" || unit >= HaulFloorChaos));
            currencyRows.AddRange(CurrencyRows(run.RunId, "deal", dealTakes, unitPrice,
                (currency, unit) => unitPrice == null || currency == "Chaos Orbs" || unit >= HaulFloorChaos));
            currencyRows.AddRange(CurrencyRows(run.RunId, "seen", highRewardsSeen, unitPrice, (_, _) => true));

            Append(RunPath, RunHeader, runRows);
            Append(RoomPath, RoomHeader, roomRows);
            Append(DealPath, DealHeader, dealRows);
            Append(CurrencyPath, CurrencyHeader, currencyRows);
            AppendWide(run, wideColumns, runTakes, dealTakes);
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

    // One run across. The columns tracked can change between runs - they follow prices
    // unless they have been ticked by hand - so the file's own header decides what this
    // row contains, and the caller's list is only used when there is no file yet.
    //
    // Writing today's set into a file written under a different one would put quantities
    // under the wrong headings, silently, for every row after it. A currency that has
    // since stopped being tracked keeps its column and reads zero; one that has started
    // being tracked does not appear until the file is started again.
    //
    // The currencies without a column are not written here at all - they are the tail this
    // file exists to leave out, and sanctum-run-currency.csv still itemises every one.
    private void AppendWide(
        RunState run,
        IReadOnlyList<string> wantedColumns,
        List<SlotObservation> takes,
        List<SlotObservation> dealTakes)
    {
        var columns = ExistingWideColumns() ?? wantedColumns ?? new List<string>();
        if (!File.Exists(WidePath))
        {
            File.AppendAllText(
                WidePath,
                WideFixedHeader + "," +
                string.Join(",", columns.Select(x => Field(CurrencyNames.ToShort(x)))) + "," +
                WideTrailingHeader + Environment.NewLine);
        }

        // The run's own number, shared by both of its rows so they group together. Counted
        // from one: nobody calls their first Sanctum of the day the zeroth.
        var index = Math.Max(CountRows(WidePath), 0) / 2 + 1;
        var date = DateTime.Now.ToString("yyyy-MM-dd");

        // The deal row carries no date. It is the same run on the same day, and repeating
        // it reads as a second run at a glance. The run number ties the two together.
        var rows = new List<string>
        {
            WideRow(columns, date, index, "run", DescribeDuration(run.Elapsed(run.Ended)), takes, run.RunId),
            WideRow(columns, null, index, "deal", null, dealTakes, run.RunId),
        };

        File.AppendAllLines(WidePath, rows);
    }

    // One row, for one source. Quantities land in the same columns whichever source it is,
    // which is the point: the deal row says how much of each currency came out of deals
    // without a second set of columns to read it in.
    private static string WideRow(
        IReadOnlyList<string> columns,
        string date,
        int index,
        string source,
        string duration,
        List<SlotObservation> takes,
        string runId)
    {
        var quantities = takes
            .Where(x => x != null && !string.IsNullOrEmpty(x.Currency))
            .GroupBy(x => x.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity), StringComparer.OrdinalIgnoreCase);

        var values = new List<object>
        {
            index,
            source,
            duration,
        };

        // A column written short still holds a currency's full name underneath, and a file
        // started before the short names existed holds the full one - ToFull takes either.
        values.AddRange(columns.Select(column =>
            (object)quantities.GetValueOrDefault(CurrencyNames.ToFull(column), 0)));

        values.Add(runId);
        return $"{date},{Row(values.ToArray())}";
    }

    // The currency columns a wide file was started with, or null if there is no file yet.
    // Split on commas without quote handling, which is safe because a currency name has
    // none - and Field would only quote one that did.
    private IReadOnlyList<string> ExistingWideColumns()
    {
        try
        {
            if (!File.Exists(WidePath))
            {
                return null;
            }

            var header = File.ReadLines(WidePath).FirstOrDefault();
            if (string.IsNullOrEmpty(header))
            {
                return null;
            }

            // The currency columns are what sits between the fixed prefix and the runId
            // that trails them. An older file has no trailing column, so dropping it is
            // conditional rather than assumed.
            var columns = header
                .Split(',')
                .Skip(WideFixedHeader.Split(',').Length)
                .Where(x => x.Length > 0)
                .ToList();

            if (columns.Count > 0 && columns[^1] == WideTrailingHeader)
            {
                columns.RemoveAt(columns.Count - 1);
            }

            return columns;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // How many runs the file already holds, so a run can carry its own index rather than
    // relying on a spreadsheet formula that breaks the moment a row is sorted. Zero for a
    // file that does not exist yet, which makes the first run run 0.
    private static int CountRows(string path)
    {
        try
        {
            return File.Exists(path) ? Math.Max(File.ReadAllLines(path).Length - 1, 0) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // The same grouping Describe does, emitted as rows instead of a sentence. Quantities
    // are summed per currency so a run contributes one row per currency per source, which
    // is the shape a pivot table wants.
    private IEnumerable<string> CurrencyRows(
        string runId,
        string source,
        IEnumerable<SlotObservation> slots,
        Func<string, double> unitPrice,
        Func<string, double, bool> keep)
    {
        return slots
            .Where(x => x != null && !string.IsNullOrEmpty(x.Currency))
            .GroupBy(x => x.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Unit = unitPrice?.Invoke(g.Key) ?? 0,
            })
            .Where(x => keep(x.Currency, x.Unit))
            .OrderByDescending(x => x.Unit * x.Quantity)
            .Select(x => Row(
                runId, source, x.Currency, x.Quantity,
                Math.Round(x.Unit, 2), Math.Round(x.Unit * x.Quantity, 2), ListName))
            .ToList();
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
