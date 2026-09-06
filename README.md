# BetterSanctumTracker

A Sanctum overlay for [ExileApi](https://github.com/exApiTools/ExileApi-Compiled), with a
per-run statistics tracker.

Rates every room on the floor map from tier values you set, frames the best route from
where you stand to the boss, and marks guard spawners and hazards in the room you are
fighting in. On top of that it records what each run actually produced and writes it to
CSV.

## Relationship to BetterSanctum

This is a full copy of [xerance/BetterSanctum](https://github.com/xerance/BetterSanctum)
with its history, kept as a separate repository so the HUD can install it independently
and the original stays untouched and working. Once the tracker is proven, it merges back
there as an ordinary merge rather than a hand port.

**Do not enable both plugins at once.** They draw the same overlay, so you would get
every frame and every line twice. Logs are separate - this one writes to
`Logs/BetterSanctumTracker/` - so the two do not fight over files.

## Credit

A fork of [exApiTools/BetterSanctum](https://github.com/exApiTools/BetterSanctum), which
is the origin of the floor map overlay, the tier and profile system, and the duplicate-run
reward marking.

The in-room spawner and hazard overlay is ported from
[deafwave/PathfindSanctum](https://github.com/deafwave/PathfindSanctum), itself a fork of
the above, which is also where the idea of scoring whole routes rather than colouring
individual connections comes from.

Prices, where enabled, come from the [Ninja Price](https://github.com/exApiTools/Ninja-Price)
plugin through the plugin bridge.

Original donation addresses, carried over from both:

BTC: bc1qke67907s6d5k3cm7lx7m020chyjp9e8ysfwtuz

ETH: 0x3A37B3f57453555C2ceabb1a2A4f55E0eB969105

## How routing works

Give every currency, room type and affliction a value from 0 to 8:

| 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| always route through | good | | | neutral | | | | never route through |

You enter exactly one room per layer, so every route holds the same number of rooms and
their totals compare directly. A route is counted per tier and weighted **per axis**,
because the same tier means different things depending on what wears it:

| tier | reward | affliction | room type |
|---|---|---|---|
| 1 | +100 | +100 | +20 |
| 2 | +3 | +30 | +10 |
| 3 | +1 | +10 | +4 |
| 5 | -1 | -20 | -4 |
| 6 | -3 | -70 | -15 |
| 7 | -10 | -250 | -40 |

Rewards are deliberately bimodal: a tier-1 reward decides routes while a tier-2 is a
bonus, so it would take two dozen lesser rewards to justify one bad affliction. Afflictions
are calibrated on the trade that matters - one tier-1 reward is worth one bad affliction
but not two. Room type sits between them, enough to prefer a calm route and never enough
to turn down a tier 1.

Tiers 0 and 8 score nothing and are compared ahead of the sum: most must-takes wins first,
then fewest never-enters. So a route reaching a 0 beats every route that does not, through
anything marked 8.

Currency is rated per reward slot, only a room's best slot counts, and quantities are
known per currency and slot - single-item rewards double in the third slot on floor 4.

## Context

Routing adjusts for the run:

- **Run type** per profile: Normal, The Hour of Divinity, The Gilded Chalice. Hour of
  Divinity flattens BoonFountain to neutral, Gilded Chalice flattens Fountain.
- **Floor**: floors 3-4 favour Deal rooms and better currency, floors 1-2 favour Treasure
  and Merchant, except under Hour of Divinity where boons cannot be bought.
- **Prices**, optionally: with Ninja Price installed, currencies you rated alike are
  ordered by what they are worth, capped so price can never overrule a tier you assigned.

## Other features

- In-room overlay marking Sanctum spawners and hazard telegraphs
- Prices on the reward window, with quantity taken from the offer text
- Hovering a room hides everything else on the map
- Overlay gives way to tooltips and open panels
- Profiles, each holding its own tiers, run type and currency cutoff

## Building

Put the source in `Plugins/Source/BetterSanctumTracker` and launch the HUD, which compiles it.
Debug output goes to `Logs/BetterSanctumTracker/` in the HUD root.
