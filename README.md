# BetterSanctumTracker

A Sanctum overlay for [ExileApi](https://github.com/exApiTools/ExileApi-Compiled), with a
per-run statistics tracker.

Prices every room on the floor map in chaos, frames the best route from where you stand to
the boss, and marks guard spawners and hazards in the room you are fighting in. On top of
that it records what each run actually produced and writes it to CSV.

## Relationship to BetterSanctum

This is a full copy of [xerance/BetterSanctum](https://github.com/xerance/BetterSanctum)
with its history, kept as a separate repository so the HUD can install it independently
and the original stays untouched and working. Once the tracker is proven, it merges back
there as an ordinary merge rather than a hand port.

The plugin class is named `BetterSanctumTrackerPlugin` rather than `BetterSanctumPlugin`,
and that is what keeps the two apart in the HUD. It identifies a plugin by its class name
and not by its folder or its assembly, so while they shared a class name they shared one
menu entry and one settings file, `config/global/BetterSanctum_settings.json` - and this
plugin's own settings were invisible behind the other one's. Settings now live in
`BetterSanctumTracker_settings.json` and logs in `Logs/BetterSanctumTracker/`.

Copy your old settings file over that name to keep your profiles. The scoring rework
replaces the values inside them either way: an old tier does not mean anything on the new
scales, so a profile from before it is reset to the defaults rather than converted.

**Do not enable both plugins at once.** They draw the same overlay, so you would get every
frame and every line twice.

## Credit

A fork of [exApiTools/BetterSanctum](https://github.com/exApiTools/BetterSanctum), which
is the origin of the floor map overlay, the profile system, and the duplicate-run reward
marking.

The in-room spawner and hazard overlay is ported from
[deafwave/PathfindSanctum](https://github.com/deafwave/PathfindSanctum), itself a fork of
the above, which is also where the idea of scoring whole routes rather than colouring
individual connections comes from.

Prices come from another plugin over the plugin bridge; which one is up to you, see below.

Original donation addresses, carried over from both:

BTC: bc1qke67907s6d5k3cm7lx7m020chyjp9e8ysfwtuz

ETH: 0x3A37B3f57453555C2ceabb1a2A4f55E0eB969105

## Prices

Prices are read over the ExileApi plugin bridge, by asking for the method
`NinjaPrice.GetBaseItemTypeValue` and handing it a base item type. What matters is that
*something* has registered that name, not which plugin did it. Both
[Ninja Price](https://github.com/exCore2/NinjaPricer) and
[Get-Chaos-Value](https://github.com/exApiTools/Get-Chaos-Value) register it, so either
one satisfies the requirement and there is no reason to run both. The lookup is resolved
lazily and retried every five seconds, since the price plugin may well initialise after
this one does.

With no price source at all nothing fails, it just gets vaguer:

- The divine rate, which every anchor below is expressed against, falls back to
  **Divine chaos fallback** (400 chaos). Rooms and afflictions therefore go on scoring
  against each other exactly as they did.
- Every reward reads as unpriced and is scored at **Unknown reward** (20% of a divine),
  the same figure a room the map has not revealed gets. Rewards then stop separating one
  route from another, and the route falls to room types and afflictions.
- **Currency price overrides** are per-unit chaos figures you type in yourself, so
  anything you actually care about can still be priced by hand.

A currency the price plugin has never heard of is treated the same way: the unknown reward
figure stands in, rather than the reward reading as worthless and every route walking
around it.

## How routing works

Everything is scored in chaos. There are no tier weights and no points: a route is worth
the reward you would take on it, less what the rooms and afflictions on the way cost.

You enter exactly one room per layer, so every route holds the same number of rooms and
their totals compare directly.

### Rewards

A reward is priced through the bridge and multiplied by the quantity that currency pays in
that slot. Quantities are measured rather than read - nothing in room data exposes them -
and they depend on the currency and the slot but not on the floor, except that single-item
rewards double in the third slot on floor 4. Only a room's best slot counts, since the
three offers are one reward at different timings and you take exactly one of them.

Where you disagree with the market, **Currency price overrides** set the chaos one unit is
worth. An override wins outright, including an override of `0`, which is how a currency is
told to pull no route at all; `-1` or absent means use the market price. That list is the
only per-currency setting there is. There is no currency tier list any more, because a
reward's band is read straight off what it is worth, which is all a tier was ever standing
in for:

| band | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| worth | 5d+ | 1d+ | 0.5d+ | 0.3d+ | 0.1d+ | the rest |

The same bands colour the reward text on the map, so the colour and the number always
agree.

### Rooms and afflictions

Neither has a price of its own, so both are anchored to a percentage of the live Divine
Orb price and move with it. Two settings hold the anchors: **Room value** (40% of a
divine) and **Affliction cost** (80%). Both scales step in fifths of their anchor, so a
step is the same size on each.

| room tier | 0 | 5 | 10 |
|---|---|---|---|
| worth | the anchor | nothing | minus the anchor |

Rooms are symmetric because a room type can be worth seeking as easily as worth avoiding,
and the tier applies to the fight room and the reward room alike, so a room is counted
twice from the one list. An unrated room type sits at 5, worth nothing either way.

| affliction tier | 0 | 5 | 6 |
|---|---|---|---|
| costs | nothing | the anchor | never entered |

Afflictions are one-sided - none of them is worth having - and 6 is not a price but a hard
block. An unrated affliction defaults to 3, mid-scale, because an affliction nobody has
rated is a cost of unknown size rather than a free one.

### What is absolute

Two things are counted rather than priced, and are compared before any chaos is:

1. **Must-takes.** Past **Must take at** (500% of a divine, the same line the top colour
   band is drawn at) a reward is worth walking through an affliction you would otherwise
   refuse. Zero switches the override off, and then nothing gets past a hard block.
2. **Hard blocks**, the afflictions rated 6.

Routes are compared by most must-takes first, then fewest hard blocks, then most chaos.
That order is what makes a must-take beat a hard block: it is settled before the blocks
are looked at.

### What the map cannot read

- A **Deal** reads its rewards as null on the map; they exist only once you are inside it.
  From floor 3 it is assumed to be worth **Deal value** (50% of a divine), and before that
  the unknown reward figure, since an early deal is not worth what a late one is.
- An unrevealed room, and a Deferral, are worth the **Unknown reward** figure (20% of a
  divine). Zero would route you around everything you have not seen yet.

### Run types

Per profile, and applied as a step on the tier before it is priced rather than as a lump
of chaos, so an adjustment keeps its meaning whatever the anchors are set to.

- **Default** applies nothing, which is the honest baseline to judge the rest against.
- **Normal** is how a run without a duplicating relic actually plays: Merchant, Treasure
  and TreasureMinor each gain a step on floors 1-2, while there is still a run left to
  spend coins in, and the afflictions that attack Aureus lose a step on floors 3-4, where
  coins matter less.
- **The Hour of Divinity** blocks boons, so BoonFountain drops to worth nothing and the
  early room bias goes with it, since coins buy boons. The Aureus affliction discount
  still applies.
- **The Gilded Chalice** blocks resolve recovery, so Fountain drops to worth nothing. It
  otherwise carries the Normal adjustments.

Both relics duplicate the final reward, so either one also crosses out the offers that are
no longer worth taking in the reward window. CurseFountain is never adjusted, and a
hard-blocked affliction is never adjusted by any run type.

### Display filter

**Hide rewards worth less than** keeps cheap rewards out of the room text on the map. It
is display only - a hidden reward is still scored, because a route is worth the sum of
what is on it. `-1` follows the divine price, at 10% of one, so it moves as the economy
does; `0` shows everything; a figure you type is absolute chaos and stays where you put
it.

## Run tracker

Off by default; turn on **Track runs**. Start and End sit in a small window that appears
while you are in the Forbidden Sanctum hub, which is where a run both begins and ends and
where nothing else on screen is competing for attention. An unfinished run is written to
`run-state.json` as it goes, so restarting the HUD part way through one does not lose it.

While a run is on, the floor map is read every time it opens and merged into what is
already known - rooms reveal a few layers ahead, so a later sighting carries rooms an
earlier one did not, and blanks are filled rather than overwritten. The reward window is
read separately, while the map is shut and you are standing in the room, which is the only
place a Deal's contents ever appear at all.

End Run writes to `Logs/BetterSanctumTracker/`:

- `sanctum-runs.csv`, a row per floor: rooms seen, layers entered, whether the floor was
  completed, which afflictions were picked up, deal rooms seen against deal rooms entered,
  how many rewards worth a divine or more the floor showed against how many one walk could
  actually have collected - one room per layer means a floor can show more than any single
  route reaches - and the assumed haul.
- `sanctum-run-rooms.csv`, a row per room and reward slot. Each row is marked `map` or
  `window` - what the floor map showed against what the reward window said - and where
  both exist the two readings sit side by side rather than one replacing the other. A room
  with no rewards still gets a row, so deals, fountains and bosses can be counted rather
  than vanishing from the file.

Nothing reads what you actually clicked. What a run produced is worked out from the rooms
you entered, assuming the most valuable slot was taken in each, so treat the haul as an
estimate - and an optimistic one, since the best slot is usually the end-of-Sanctum
deferral, which pays nothing if the run ends early. Every slot's score is written out
beside it, so which slot the policy picked and why is auditable from the file rather than
being a number to take on trust.

## Other features

- In-room overlay marking Sanctum spawners and hazard telegraphs
- Prices on the reward window, with quantity taken from the offer text
- Hovering a room hides everything else on the map
- Overlay gives way to tooltips and open panels
- Profiles, each holding its own room and affliction tiers, currency price overrides, run
  type and hide threshold; colours and display settings are shared across all of them

## Building

Put the source in `Plugins/Source/BetterSanctumTracker` and launch the HUD, which compiles it.
Debug output goes to `Logs/BetterSanctumTracker/` in the HUD root.
