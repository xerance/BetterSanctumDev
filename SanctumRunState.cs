using System;
using System.Collections.Generic;

namespace BetterSanctumDev;

// Plain data, deliberately free of ExileCore types: the plugin reads the game and hands
// these over, the tracker merges and persists them. That keeps the merge rules - which
// are the fiddly part, because rooms reveal a few at a time - away from memory reading,
// and lets the whole run state round-trip through JSON without custom converters.

// One reward slot of one room. Quantity is the measured figure for this currency in this
// slot, not something the game exposes, so it travels with the observation rather than
// being looked up again at write time when the floor is no longer known.
public class SlotObservation
{
    public int Slot { get; set; }
    public string Currency { get; set; }
    public int Quantity { get; set; }
    public int Tier { get; set; }
}

// One line of the reward window: "Receive 14x Orbs of Fusing at the end of the Floor".
// The map carries Reward1/2/3 for an ordinary reward room, but a Deal reads all three as
// null - its contents only exist once you are standing in it - so this is the only way to
// see what a deal was worth. Captured for every room rather than only deals, since it also
// puts the window's own quantities beside the measured ones.
public class OfferObservation
{
    public int Slot { get; set; }
    public string Text { get; set; }
    public string Currency { get; set; }
    public int Quantity { get; set; }
    public int Tier { get; set; }
}

public class RoomObservation
{
    public int Layer { get; set; }
    public int Room { get; set; }
    public string FightRoomId { get; set; }
    public string RewardRoomId { get; set; }
    public string Affliction { get; set; }

    // Indices into the next layer. Kept per room rather than as one floor-wide table
    // because rooms arrive a few at a time and the table is only ever complete at the end.
    public List<int> Connections { get; set; } = new List<int>();

    public List<SlotObservation> Slots { get; set; } = new List<SlotObservation>();

    // What the reward window said while you stood in this room, which for a Deal is the
    // only place its contents appear at all.
    public List<OfferObservation> Offers { get; set; } = new List<OfferObservation>();

    public bool IsDeal => FightRoomId == "Deal" || RewardRoomId == "Deal";

    public int Tier01Count
    {
        get
        {
            var count = 0;
            foreach (var slot in Slots)
            {
                if (slot.Tier <= 1)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

public class FloorObservation
{
    public int Floor { get; set; }
    public string Prefix { get; set; }
    public int LayerCount { get; set; }

    // Keyed "layer/room" so a floor merges across every map opening rather than being
    // captured once. A dictionary, not a list, because the same room is seen many times
    // and later sightings carry more than earlier ones.
    public Dictionary<string, RoomObservation> Rooms { get; set; } = new Dictionary<string, RoomObservation>();

    // The room index taken in each completed layer, so the route actually walked is known.
    public List<int> Choices { get; set; } = new List<int>();

    // Set when the matching affliction was active while this floor was on screen. A run
    // under one of these undercounts rather than reporting nothing, so it has to be
    // filterable rather than silently averaged in.
    public bool RewardsObscured { get; set; }
    public bool RoomTypesObscured { get; set; }
    public bool AfflictionsObscured { get; set; }

    public static string Key(int layer, int room) => $"{layer}/{room}";
}

public class RunState
{
    public string RunId { get; set; }
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }

    // Every entry into the hub since the run started. A complete run passes through it
    // once per floor, so the informative number is what this exceeds that baseline by.
    public int HubVisits { get; set; }

    // Time spent paused - a trade in the middle of a run, say - which the run's duration
    // leaves out. PausedAt is a pause still going; Paused is every pause already over.
    // Both are saved, so a pause survives a HUD restart like the rest of the run.
    public DateTime? PausedAt { get; set; }
    public TimeSpan Paused { get; set; }

    // Running time up to a moment, with every pause taken out, including one still open
    public TimeSpan Elapsed(DateTime? until = null)
    {
        var end = until ?? DateTime.Now;
        var paused = Paused + (PausedAt is { } since && end > since ? end - since : TimeSpan.Zero);
        var elapsed = end - Started - paused;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public Dictionary<int, FloorObservation> Floors { get; set; } = new Dictionary<int, FloorObservation>();

    public FloorObservation Floor(int floor, string prefix)
    {
        if (!Floors.TryGetValue(floor, out var observation))
        {
            observation = new FloorObservation { Floor = floor, Prefix = prefix };
            Floors[floor] = observation;
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            observation.Prefix = prefix;
        }

        return observation;
    }
}
