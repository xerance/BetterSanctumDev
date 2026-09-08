# BetterSanctumTracker

A Sanctum overlay for [ExileApi](https://github.com/exApiTools/ExileApi-Compiled).

Prices every room on the floor map in chaos, frames the best route from where you stand to
the boss, and marks guard spawners and hazards in the room you are fighting in. Records
what each run produced and writes it to CSV.

## Credit

A fork of [exApiTools/BetterSanctum](https://github.com/exApiTools/BetterSanctum), which
is the origin of the floor map overlay, the tier and profile system, and the duplicate-run
reward marking.

The in-room spawner and hazard overlay is ported from
[deafwave/PathfindSanctum](https://github.com/deafwave/PathfindSanctum), itself a fork of
the above, which is also where the idea of scoring whole routes rather than colouring
individual connections comes from.

Prices come through the plugin bridge from anything registering
`NinjaPrice.GetBaseItemTypeValue` - [Ninja Price](https://github.com/exCore2/NinjaPricer)
and [Get-Chaos-Value](https://github.com/exApiTools/Get-Chaos-Value) both do, so either
one will serve and there is no reason to run both.

Original donation addresses, carried over from both:

BTC: bc1qke67907s6d5k3cm7lx7m020chyjp9e8ysfwtuz

ETH: 0x3A37B3f57453555C2ceabb1a2A4f55E0eB969105

## Relationship to BetterSanctum

A full copy of [xerance/BetterSanctum](https://github.com/xerance/BetterSanctum) with its
history, kept separate so the HUD can install both and the original stays working. The
plugin class is renamed, which is what keeps them apart: the HUD identifies a plugin by its
class name and not by its folder, so sharing one meant sharing a menu entry and a settings
file. Settings live in `BetterSanctumTracker_settings.json`, logs in
`Logs/BetterSanctumTracker/`.

**Do not enable both at once.** They draw the same overlay, so you get every frame and
every line twice.

## How routing works

Everything is scored in chaos. A route is worth the reward you would take on it, less what
the rooms and afflictions on the way cost. You enter exactly one room per layer, so every
route holds the same number of rooms and their totals compare directly.

Rewards are priced through the bridge and multiplied by the quantity that currency pays in
that slot - quantities are measured rather than read, and single-item rewards double in the
third slot on floor 4. Only a room's best slot counts, since the three offers are one
reward at different timings and you take one. There is no currency tier list: a reward's
band is read off what it is worth, which is all a tier was ever standing in for, and the
same bands colour it on the map.

| band | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| worth | 5d+ | 1d+ | 0.5d+ | 0.3d+ | 0.1d+ | the rest |

Rooms and afflictions have no price of their own, so both are anchored to a percentage of
the live divine price and move with it - **Room value** at 40% and **Affliction cost** at
80%. Each scale steps in fifths of its anchor:

| tier | 0 | 5 | 10 |
|---|---|---|---|
| room | worth the anchor | nothing | costs the anchor |
| affliction | costs nothing | costs the anchor | *(never entered, at 6)* |

Rooms are symmetric, since a room type can be worth seeking as readily as avoiding, and the
tier applies to the fight room and the reward room alike, so a room is counted twice from
the one list. Afflictions are one-sided - none of them is worth having - and stop at 6,
which is a block rather than a price.

Two things are counted rather than priced and are compared ahead of the chaos: rewards past
**Must take at** (5 divine, the line the top band is drawn at), then afflictions at 6. Most
must-takes wins first, then fewest blocks. So a route reaching a must-take beats every
route that does not, through anything blocked.

What the map cannot read is assumed: a **Deal** hides its rewards until you are inside, and
is worth 50% of a divine from floor 3; a room the map has not revealed is worth 20%. Zero
would route you around everything you have not seen yet.

Where you disagree with the market, a per-currency chaos override replaces the price
outright. `0` means the reward pulls no route at all, `-1` or absent means use the price.

## Context

Routing adjusts for the run:

- **Run type** per profile. Default applies nothing. Normal favours Merchant, Treasure and
  TreasureMinor on floors 1-2, while there is still a run left to spend coins in, and
  discounts the Aureus afflictions on floors 3-4 where coins matter less. The Hour of
  Divinity drops BoonFountain to worth nothing and gives up the coin bias with it, there
  being no boons to buy; The Gilded Chalice drops Fountain the same way. Both relics
  duplicate the final reward, so either also marks the offers not worth taking.
- **Floor**: quantities double in the last slot on floor 4, and a Deal is only worth its
  full assumption from floor 3.
- **Prices**: with no price plugin the divine falls back to a figure you set, so rooms and
  afflictions go on scoring against each other, but every reward reads as unknown and stops
  separating routes. Overrides still price anything you care about by hand.

Adjustments move a tier by a step before it is priced, never by adding chaos, so they keep
their meaning whatever the anchors are set to. A blocked affliction is never adjusted.

## Run tracking

Off by default. A window in the Forbidden Sanctum hub starts and ends a run, and End Run
writes three files to `Logs/BetterSanctumTracker/`:

- `sanctum-runs.csv` - a row per run: how long it took, what it paid across all four
  floors, how many deals you entered from floor 3 and what they gave up, how many rewards
  worth a divine or more the run put in front of you whether or not a route could reach
  them, and whether Golden Smoke or Deceptive Mirror turned up and on which floor.
- `sanctum-run-rooms.csv` - a row per room and reward slot, marked `map` or `window`. A
  Deal only ever produces `window` rows, since the map reads its rewards as empty and they
  exist only in the reward window while you stand there.
- `sanctum-deals.csv` - a row per offer of every deal you walked into, on any floor, with
  nothing filtered out. The run file reports deals under the same rules as the rest of the
  haul, which drops most of what a deal actually pays, so this is the raw record to work
  out what a deal is worth from once there is enough of it.

Hauls list chaos and anything worth five chaos a unit or more, richest first, since the
long tail of alteration and chance says nothing about how a run went. Comma separated and
quoted the ordinary way, so a spreadsheet opens either without being asked about
separators.

Rooms reveal a few layers at a time, so a floor is merged across every map opening rather
than captured once. What a run produced is worked out from the rooms you entered, assuming
you took the most valuable slot in each - an estimate, and an optimistic one, so floors
completed is recorded beside it. In-progress state is saved to disk, so restarting the HUD
part way through a run does not lose it.

## Other features

- In-room overlay marking Sanctum spawners and hazard telegraphs
- Prices on the reward window, with quantity taken from the offer text
- Rewards on the map read as the count and what that many come to
- Hovering a room hides everything else on the map
- Overlay gives way to tooltips and open panels
- Profiles, each holding its own tiers, price overrides, run type and hide threshold

## Building

Put the source in `Plugins/Source/BetterSanctumTracker` and launch the HUD, which compiles
it. Debug output goes to `Logs/BetterSanctumTracker/` in the HUD root.
