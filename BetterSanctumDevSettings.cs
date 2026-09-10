using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using Color = SharpDX.Color;

namespace BetterSanctumDev;

public class BetterSanctumDevSettings : ISettings
{
    // Every reward category the game has, which is what a price override and a tracking
    // tick can be set on, so it has to stay complete. The ones worth reading first lead it
    // and the tail follows, which only affects the order they are listed in the menu - the
    // wide run file picks its own columns and sorts them by name.
    public static readonly IReadOnlyList<string> CurrencyTypes = new List<string>
    {
        "Mirrors of Kalandra",
        "Volatile Vaal Orbs",
        "Fracturing Orbs",
        "Divine Orbs",
        "Veiled Chaos Orbs",
        "Sacred Orbs",
        "Orbs of Annulment",
        "Ancient Orbs",
        "Divine Vessels",
        "Chaos Orbs",
        "Chromatic Orbs",
        "Gemcutter's Prisms",

        "Orbs of Alteration",
        "Orbs of Chance",
        "Glassblower's Baubles",
        "Jeweller's Orbs",
        "Orbs of Alchemy",
        "Orbs of Fusing",
        "Orbs of Scouring",
        "Cartographer's Chisels",
        "Orbs of Binding",
        "Orbs of Regret",
        "Blessed Orbs",
        "Vaal Orbs",
        "Orbs of Horizon",
        "Instilling Orbs",
        "Regal Orbs",
        "Enkindling Orbs",
        "Orbs of Unmaking",
        "Awakened Sextants",
        "Stacked Decks",
        "Exalted Orbs",
        "Blacksmith's Whetstones",
        "Armourer's Scraps",
        "Orbs of Transmutation",
        "Orbs of Augmentation",
    };

    // JsonIgnore matters here: Newtonsoft appends to an existing collection rather than
    // replacing it, so a serialised copy grew by five entries every time settings loaded.
    [JsonIgnore]
    public readonly IReadOnlyList<string> CurrencyDuplicate = new List<string>
    {
        "Divine Orb",
        "Divine Orbs",
        "Mirror of kalandra",
        "Mirror",
        "Mirrors",
    };

    private static readonly IReadOnlyList<string> RoomTypes = new List<string>
    {
        "Explore",
        "Arena",
        "Lair",
        "Maze",
        "Gauntlet",
        "Miniboss",
        "Vault",
        "Puzzle",
        "Boss",
        "Merchant",
        "Fountain",
        "Deal",
        "Deferral",
        "CurseFountain",
        "BoonFountain",
        "RainbowFountain",
        "Treasure",
        "TreasureMinor",
        "Final",
    };

    private static readonly IReadOnlyList<(string, string)> AfflictionTypes = new List<(string, string)>
    {
        ("Corrosive Concoction", "No Resolve Mitigation, chance to Avoid Resolve loss or Resolve Aegis"),
        ("Shattered Shield", "Cannot have Resolve Aegis"),
        ("Sharpened Arrowhead", "Enemy Hits ignore your Resolve Mitigation"),
        ("Iron Manacles", "Cannot Avoid Resolve Loss from Enemy Hits"),
        ("Accursed Prism", "When you gain an Affliction, gain an additional random Minor Affliction"),
        ("Poisoned Water", "Gain a random Minor Affliction when you use a Fountain"),
        ("Glass Shard", "The next Boon you gain is converted into a random Minor Affliction"),
        ("Cutpurse", "You cannot gain Aureus coins"),
        ("Corrupted Lockpick", "Chests in rooms explode when opened"),
        ("Voodoo Doll", "100% more Resolve lost while Resolve is below 50%"),
        ("Phantom Illusion", "Every room grants a random Minor Affliction, Afflictions granted this way are removed on room completion"),
        ("Gargoyle Totem", "Guards are accompanied by a Gargoyle"),
        ("Purple Smoke", "Afflictions are unknown on the Sanctum Map"),
        ("Veiled Sight", "Rooms are unknown on the Sanctum Map"),
        ("Red Smoke", "Room types are unknown on the Sanctum Map"),
        ("Golden Smoke", "Rewards are unknown on the Sanctum Map"),
        ("Blunt Sword", "You and your Minions deal 25% less Damage"),
        ("Charred Coin", "50% less Aureus coins found"),
        ("Deadly Snare", "Traps impact infinite Resolve"),
        ("Spiked Exit", "Lose 5% of current Resolve on room completion"),
        ("Floor Tax", "Lose all Aureus on floor completion"),
        ("Door Tax", "Lose 30 Aureus coins on room completion"),
        ("Spilt Purse", "Lose 20 Aureus coins when you lose Resolve from a Hit"),
        ("Liquid Cowardice", "Lose 10 Resolve when you use a Flask"),
        ("Tight Choker", "You can have a maximum of 5 Boons"),
        ("Unhallowed Ring", "50% increased Merchant prices"),
        ("Unhallowed Amulet", "The Merchant offers 50% fewer choices"),
        ("Rusted Coin", "The Merchant only offers one choice"),
        ("Honed Claws", "Monsters deal 25% more Damage"),
        ("Spiked Shell", "Monsters have 30% increased Maximum Life"),
        ("Chiselled Stone", "Monsters Petrify on Hit"),
        ("Hungry Fangs", "Monsters impact 25% increased Resolve"),
        ("Chains of Binding", "Monsters inflict Binding Chains on Hit"),
        ("Rusted Mallet", "Monsters always Knockback, Monsters have increased Knockback Distance"),
        ("Fiendish Wings", "Monsters' Action Speed cannot be slowed below base, Monsters have 30% increased Attack, Cast and Movement Speed"),
        ("Mark of Terror", "Monsters inflict Resolve Weakness on Hit"),
        ("Concealed Anomaly", "Guards release a Volatile Anomaly on Death"),
        ("Empty Trove", "Chests no longer drop Aureus coins"),
        ("Death Toll", "Monsters no longer drop Aureus coins"),
        ("Tattered Blindfold", "90% reduced Light Radius, Minimap is hidden"),
        ("Haemorrhage", "You cannot recover Resolve (removed after killing the next Floor Boss)"),
        ("Demonic Skull", "Cannot recover Resolve"),
        ("Unassuming Brick", "You cannot gain any more Boons"),
        ("Unholy Urn", "50% reduced Effect of your Relics"),
        ("Weakened Flesh", "-100 to Maximum Resolve"),
        ("Worn Sandals", "40% reduced Movement Speed"),
        ("Orb of Negation", "Relics have no Effect"),
        ("Ghastly Scythe", "Losing Resolve ends your Sanctum"),
        ("Unquenched Thirst", "50% reduced Resolve recovered"),
        ("Dark Pit", "Traps impact 100% increased Resolve"),
        ("Rapid Quicksand", "Traps are faster"),
        ("Anomaly Attractor", "Rooms spawn Volatile Anomalies"),
        ("Black Smoke", "You can see one fewer room ahead on the Sanctum Map"),
        ("Deceptive Mirror", "You are not always taken to the room you select"),
    };

    public BetterSanctumDevSettings()
    {
        var currencyFilter = "";
        var roomFilter = "";
        var afflictionFilter = "";
        var renameBuffer = "";
        string renameBufferOwner = null;
        TieringNode = new CustomNode
        {
            DrawDelegate = () =>
            {
                var (profileName, profile) = GetCurrentProfile();

                // The rename box edits a buffer that survives across frames and is only
                // written back on Enter. Committing every keystroke renamed the profile
                // out from under the widget, after which each further keystroke consumed
                // whichever profile had become current.
                if (renameBufferOwner != profileName)
                {
                    renameBufferOwner = profileName;
                    renameBuffer = profileName;
                }

                foreach (var key in Profiles.Keys.OrderBy(x => x).ToList())
                {
                    if (key == profileName)
                    {
                        ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.DarkGreen.ToImgui());
                        if (ImGui.InputText("Current profile (Enter to rename)", ref renameBuffer, 200, ImGuiInputTextFlags.EnterReturnsTrue))
                        {
                            RenameProfile(profileName, renameBuffer);
                            renameBufferOwner = null;
                        }

                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.Button($"Activate profile {key}##profile"))
                        {
                            CurrentProfile = key;
                        }
                    }
                }

                if (ImGui.Button("Add profile##addProfile"))
                {
                    var newProfileName = Enumerable.Range(0, 100).Select(x => $"New profile {x}").First(x => !Profiles.ContainsKey(x));
                    Profiles[newProfileName] = ProfileContent.CreateNew();
                    CurrentProfile = newProfileName;
                }

                Hint("A profile holds the room and affliction tiers, the currency price overrides, the run type and the hide threshold. Colours and display settings are shared across all profiles.");

                // Deleting the last profile would leave nothing to fall back to
                if (Profiles.Count > 1)
                {
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete profile {profileName}##deleteProfile"))
                    {
                        Profiles.Remove(profileName);
                        CurrentProfile = Profiles.Keys.First();
                    }
                }


                // Part of the profile rather than a display preference: both follow the
                // run strategy, and the tier cutoff is meaningless without the tiers it
                // is counted against.
                var runType = profile.RunType;
                if (ImGui.Combo("Run type", ref runType, RunTypeNames, RunTypeNames.Length))
                {
                    profile.RunType = runType;
                }

                Hint("Default applies nothing, which is the baseline to judge the others against." +
                     "\n\nNormal is an ordinary run: Merchant, Treasure and TreasureMinor each gain a step on floors 1-2, while there is still a run left to spend coins in, and the afflictions that attack Aureus lose a step on floors 3-4 where coins matter less." +
                     "\n\nBoth relics duplicate the final reward, so either also marks the offers not worth taking. They carry the Normal adjustments as well." +
                     "\nHour of Divinity blocks boons: BoonFountain drops to worth nothing and the early room bias goes with it, since coins buy boons. The Aureus affliction discount still applies." +
                     "\nGilded Chalice blocks resolve recovery: Fountain drops to worth nothing. CurseFountain is never adjusted." +
                     "\n\nA hard-blocked affliction is never adjusted by any of them.");

                // -1 for "unset" the same way the currency overrides read it, so the two
                // fields behave alike rather than one clamping where the other clears.
                var hideRewardsBelowChaos = profile.HideRewardsBelowChaos;
                if (ImGui.InputInt("Hide rewards worth less than (chaos) override", ref hideRewardsBelowChaos))
                {
                    profile.HideRewardsBelowChaos = hideRewardsBelowChaos < 0 ? -1 : hideRewardsBelowChaos;
                }

                Hint("Rewards worth less than this are left out of the room text on the map. It does not affect routing - a hidden reward is still scored." +
                     $"\n\n-1 uses the default, which is {DefaultHidePercentOfDivine}% of a Divine Orb{DescribeDefaultHideChaos()}. Being a share of a divine it moves as the economy does." +
                     "\n0 hides nothing." +
                     "\n\nAnything else is that many chaos, flat, and stays where you put it - so it is worth revisiting when prices move.");

                DisabledText("Every axis is scored in chaos. Rewards are priced; rooms and afflictions are priced from an anchor set in Routing.");
                Hint("Currency has no tiers. A reward is worth its price times the quantity of the slot, and its band is read off that figure, so the only setting is the price override below." +
                     "\nRoom 0-10: 0 is worth the room anchor, 5 is worth nothing, 10 costs the anchor. Unrated room types sit at 5." +
                     "\nAffliction 0-6: 0 costs nothing, 5 costs the affliction anchor, 6 is never walked into unless a reward past Must Take Percent Of Divine lies beyond it. Unrated afflictions sit at 3, since an unrated one is a cost of unknown size rather than a free one.");

                if (ImGui.TreeNode("Currency price overrides"))
                {
                    DisabledText("A reward's band is read off what it is worth, so there is nothing to rate. Override the chaos each one is worth where you disagree with the market, or set 0 to ignore it.");
                    ImGui.InputTextWithHint("##CurrencyFilter", "Filter", ref currencyFilter, 100);
                    var (currencyTypes, fromGameFiles) = GetKnownCurrencyTypes();
                    DisabledText($"{currencyTypes.Count} currencies ({(fromGameFiles ? "from game files" : "fallback list")})");
                    DisabledText("Blank or -1 leaves the price alone. The value is per unit; the quantity of the slot is applied on top.");

                    foreach (var type in currencyTypes)
                    {
                        if (!MatchesFilter(type, currencyFilter))
                        {
                            continue;
                        }

                        // -1 rather than 0 for "no override", since 0 is the useful value
                        // that says to ignore a currency entirely.
                        var current = profile.CurrencyUnitPriceOverrides.GetValueOrDefault(type, -1);
                        if (ImGui.InputInt(type, ref current))
                        {
                            if (current < 0)
                            {
                                profile.CurrencyUnitPriceOverrides.Remove(type);
                            }
                            else
                            {
                                profile.CurrencyUnitPriceOverrides[type] = current;
                            }
                        }
                    }

                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Room tiering"))
                {
                    DisabledText("Applies to both the fight room and the reward room, so a room is counted twice from this one list.");
                    ImGui.InputTextWithHint("##RoomFilter", "Filter", ref roomFilter, 100);
                    foreach (var type in RoomTypes.Where(t => t.Contains(roomFilter, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        var currentValue = GetRoomTier(type);
                        if (ImGui.SliderInt(type, ref currentValue, 0, SanctumValues.RoomTierMax))
                        {
                            profile.RoomTiers[type] = currentValue;
                        }
                    }

                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Affliction tiering"))
                {
                    DisabledText("Filter matches names and descriptions, and several words all have to match.");
                    ImGui.InputTextWithHint("##AfflictionFilter", "Filter", ref afflictionFilter, 100);
                    // Name and description are searched as one string, so terms can span both
                    foreach (var (type, description) in AfflictionTypes.Where(t => MatchesFilter($"{t.Item1} {t.Item2}", afflictionFilter)))
                    {
                        var currentValue = GetAfflictionTier(type);
                        if (ImGui.SliderInt(type, ref currentValue, 0, SanctumValues.AfflictionTierMax))
                        {
                            profile.AfflictionTiers[type] = currentValue;
                        }

                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered())
                        {
                            Tooltip(description);
                        }
                    }

                    ImGui.TreePop();
                }
            }
        };
    }

    // The set of reward currencies is defined by the game's SanctumDeferredRewardCategory table,
    // which is also what room rewards report as their CurrencyName. Reading it here keeps the
    // tiering keys in sync with the lookup keys automatically. The static list below is only a
    // fallback for when the settings are drawn before the game files are loaded.
    // Space-separated terms, all of which must match, so "chaos second" narrows to one slot.
    private static bool MatchesFilter(string text, string filter)
    {
        return filter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => text.Contains(term, StringComparison.InvariantCultureIgnoreCase));
    }

    private (IReadOnlyList<string> Types, bool FromGameFiles) GetKnownCurrencyTypes()
    {
        if (RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList is { Count: > 0 } entries)
        {
            var liveTypes = entries
                .Select(x => x.CurrencyName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            if (liveTypes.Count > 0)
            {
                return (liveTypes, true);
            }
        }

        return (CurrencyTypes, false);
    }

    // Silently ignores names that would collide with or erase another profile:
    // the old code assigned before removing, so renaming onto an existing name
    // overwrote that profile's contents.
    private void RenameProfile(string oldName, string newName)
    {
        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == oldName || Profiles.ContainsKey(newName))
        {
            return;
        }

        if (!Profiles.Remove(oldName, out var content))
        {
            return;
        }

        Profiles[newName] = content;
        CurrentProfile = newName;
    }

    // Each axis runs on its own scale now, because they no longer mean the same thing.
    // Currency says what you are after and is priced from the price plugin; rooms and
    // afflictions are priced from an anchor. Only two positions are absolute: a currency
    // at 0 is taken whatever stands in the way, an affliction at 6 is never walked into.
    // Where nothing has been rated. A room is worth nothing either way, and an affliction
    // sits mid-scale - an unrated affliction is a cost of unknown size, not a free one.
    public const int RoomDefaultTier = SanctumValues.RoomNeutralTier;
    public const int AfflictionDefaultTier = 3;

    // Observed quantities per reward slot, keyed by the category's CurrencyName. Measured
    // from offer text across floors 1 to 4: quantity depends on the currency and the slot
    // and not at all on the floor - chaos is 5/10/14 on floor 3 exactly as on floor 4.
    //
    // The default matches the shape every single-item reward takes. The game's own
    // DeferredRewards table has now been read, so every currency in it is accounted for.
    public static readonly int[] DefaultRewardQuantity = { 1, 1, 1 };

    public static readonly IReadOnlyDictionary<string, int[]> RewardQuantities = new Dictionary<string, int[]>
    {
        ["Orbs of Alteration"] = new[] { 9, 20, 30 },
        ["Orbs of Chance"] = new[] { 9, 20, 30 },
        ["Jeweller's Orbs"] = new[] { 9, 20, 30 },
        ["Orbs of Alchemy"] = new[] { 6, 14, 20 },
        ["Orbs of Fusing"] = new[] { 6, 14, 20 },
        ["Chaos Orbs"] = new[] { 5, 10, 14 },
        ["Orbs of Scouring"] = new[] { 5, 10, 14 },
        ["Orbs of Regret"] = new[] { 5, 10, 14 },
        ["Orbs of Binding"] = new[] { 5, 10, 14 },
        ["Blessed Orbs"] = new[] { 4, 8, 12 },
        ["Vaal Orbs"] = new[] { 4, 8, 12 },
        ["Regal Orbs"] = new[] { 4, 8, 12 },
        ["Gemcutter's Prisms"] = new[] { 4, 8, 12 },
        ["Chromatic Orbs"] = new[] { 4, 8, 12 },
        ["Exalted Orbs"] = new[] { 4, 8, 12 },
        ["Orbs of Unmaking"] = new[] { 4, 8, 12 },
        ["Instilling Orbs"] = new[] { 4, 8, 12 },
        ["Ancient Orbs"] = new[] { 1, 1, 1 },
        ["Divine Orbs"] = new[] { 1, 1, 1 },
        ["Mirrors of Kalandra"] = new[] { 1, 1, 1 },
        ["Fracturing Orbs"] = new[] { 1, 1, 1 },
        ["Divine Vessels"] = new[] { 1, 1, 1 },
        ["Orbs of Annulment"] = new[] { 1, 1, 1 },
        ["Volatile Vaal Orbs"] = new[] { 1, 1, 1 },
        ["Sacred Orbs"] = new[] { 1, 1, 1 },
    };

    // Read from the game's DeferredRewards table, which lists every count a currency can
    // pay. Most single-item rewards top out at 2 there, which the doubling rule covers.
    // These two do not: a mirror is 1 in every row it has, and a divine vessel reaches 3.
    public static readonly IReadOnlyDictionary<string, int> FinalFloorLastSlotQuantity = new Dictionary<string, int>
    {
        ["Mirrors of Kalandra"] = 1,
        ["Divine Vessels"] = 3,
    };

    public static int GetRewardQuantity(string currencyName, int slot, int floor)
    {
        var quantities = currencyName != null && RewardQuantities.TryGetValue(currencyName, out var known)
            ? known
            : DefaultRewardQuantity;
        var quantity = quantities[Math.Clamp(slot, 0, quantities.Length - 1)];

        // Single-item rewards double in the last slot on floor 4. Reported for divine,
        // fracturing and volatile vaal, and corroborated by sacred orbs: the logs show it
        // as 2 at floor 4 slot 2 while every other single-item reward reads 1 elsewhere.
        // Stacked currencies do not do this - chaos is 14 in that slot on floors 2 and 4
        // alike - so the rule is tied to the quantity, not applied across the board.
        if (slot != 2 || floor < 4)
        {
            return quantity;
        }

        if (currencyName != null && FinalFloorLastSlotQuantity.TryGetValue(currencyName, out var finalCount))
        {
            return finalCount;
        }

        return quantity == 1 ? 2 : quantity;
    }


    // Fight rooms are graded on the resolve they tend to cost, reward rooms on what they
    // hand you, and 5 is worth nothing either way. Boss and Final sit at neutral
    // deliberately: they are in the last layer every route passes through, so their value
    // cannot separate two routes.
    //
    // Deal is nudged one step attractive rather than left neutral. That is on top of what
    // a deal pays, which is its own setting, since the map reads a deal's rewards as
    // empty - so the step is a thumb on the scale for entering one at all, not a price.
    //
    // These are the tuned values from a played profile, not a first guess.
    public static readonly IReadOnlyDictionary<string, int> DefaultRoomTiers = new Dictionary<string, int>
    {
        ["Explore"] = 4,
        ["Maze"] = 5,
        ["Puzzle"] = 5,
        ["Gauntlet"] = 5,
        ["Lair"] = 5,
        ["Vault"] = 5,
        ["Boss"] = 5,
        ["Miniboss"] = 5,
        ["Arena"] = 7,
        ["Merchant"] = 5,
        ["BoonFountain"] = 4,
        ["RainbowFountain"] = 4,
        ["Deferral"] = 5,
        ["Fountain"] = 5,
        ["Treasure"] = 5,
        ["TreasureMinor"] = 5,
        ["Deal"] = 4,
        ["Final"] = 5,
        ["CurseFountain"] = 5,
    };

    // 6 is reserved for the run-enders and is absolute; 0 to 5 are costs running from
    // nothing up to the whole anchor. These are the tuned values from a played profile.
    //
    // Death Toll is deliberately absent and falls to the unrated tier, since it has never
    // been rated in play. Anything else the game adds lands there too.
    public static readonly IReadOnlyDictionary<string, int> DefaultAfflictionTiers = new Dictionary<string, int>
    {
        // Never walked into
        ["Accursed Prism"] = 6,
        ["Golden Smoke"] = 6,
        ["Deadly Snare"] = 6,
        ["Ghastly Scythe"] = 6,
        ["Purple Smoke"] = 6,

        // The whole anchor: run-shaping, but a rich enough room still buys past them
        ["Poisoned Water"] = 5,
        ["Orb of Negation"] = 5,
        ["Liquid Cowardice"] = 5,
        ["Deceptive Mirror"] = 5,
        ["Glass Shard"] = 5,
        ["Cutpurse"] = 5,
        ["Unholy Urn"] = 5,
        ["Tight Choker"] = 5,

        ["Rusted Coin"] = 4,
        ["Chiselled Stone"] = 4,
        ["Fiendish Wings"] = 4,
        ["Demonic Skull"] = 4,
        ["Unassuming Brick"] = 4,
        ["Rapid Quicksand"] = 4,
        ["Phantom Illusion"] = 4,
        ["Black Smoke"] = 3,
        ["Anomaly Attractor"] = 4,
        ["Corrupted Lockpick"] = 4,

        ["Veiled Sight"] = 3,
        ["Red Smoke"] = 3,
        ["Floor Tax"] = 3,
        ["Unhallowed Amulet"] = 3,
        ["Empty Trove"] = 3,
        ["Door Tax"] = 3,
        ["Unhallowed Ring"] = 3,
        ["Concealed Anomaly"] = 3,

        ["Worn Sandals"] = 2,
        ["Mark of Terror"] = 2,
        ["Spiked Shell"] = 2,
        ["Honed Claws"] = 2,
        ["Charred Coin"] = 2,
        ["Blunt Sword"] = 2,
        ["Weakened Flesh"] = 2,

        ["Tattered Blindfold"] = 1,
        ["Unquenched Thirst"] = 1,
        ["Dark Pit"] = 1,
        ["Haemorrhage"] = 1,
        ["Spilt Purse"] = 1,
        ["Hungry Fangs"] = 1,
        ["Voodoo Doll"] = 1,
        ["Gargoyle Totem"] = 1,
        ["Spiked Exit"] = 1,
        ["Chains of Binding"] = 1,
        ["Rusted Mallet"] = 1,

        // Costs nothing worth routing around
        ["Corrosive Concoction"] = 0,
        ["Shattered Shield"] = 0,
        ["Sharpened Arrowhead"] = 0,
        ["Iron Manacles"] = 0,
    };

    // Adjustments shift a tier by a step before it is priced, rather than adding chaos, so
    // they stay meaningful whatever the anchors are set to and however the economy moves.
    //
    // Default applies none of them, which is the honest baseline to judge the rest against.
    // Normal is how a run without a duplicating relic actually plays. The two relic runs
    // duplicate the final reward, so both also mark the offers not worth taking.
    public const int RunTypeDefault = 0;
    public const int RunTypeNormal = 1;
    public const int RunTypeHourOfDivinity = 2;
    public const int RunTypeGildedChalice = 3;

    public static readonly string[] RunTypeNames =
    {
        "Default (no adjustments)", "Normal", "The Hour of Divinity", "The Gilded Chalice",
    };

    // Coins stop mattering once there is little run left to spend them in, so the
    // afflictions that attack them get cheaper on the last two floors. Read off the
    // description rather than a second list, which cannot then fall out of step with it.
    private static readonly HashSet<string> AureusAfflictions = AfflictionTypes
        .Where(x => x.Item2.Contains("Aureus", StringComparison.InvariantCultureIgnoreCase))
        .Select(x => x.Item1)
        .ToHashSet();

    public static bool AfflictionAffectsAureus(string effectName) =>
        effectName != null && AureusAfflictions.Contains(effectName);


    // Floors are identified by the prefix on their room ids; the area name does not
    // track the floor. Nave and Crypt are the last two in some order, which no rule
    // distinguishes, so their relative order does not matter.
    private static readonly Dictionary<string, int> FloorsByRoomPrefix = new()
    {
        ["Cellar"] = 1,
        ["Vaults"] = 2,
        ["Nave"] = 3,
        ["Crypt"] = 4,
    };

    public static int GetFloorForRoomPrefix(string prefix)
    {
        return prefix != null && FloorsByRoomPrefix.TryGetValue(prefix, out var floor) ? floor : 0;
    }

    // A tenth of a divine, which is the bottom colour band and little more than a floor
    // under the noise. Deliberately low: silencing one currency is what an override of
    // zero is for, and a default that hid by price would take that decision away from
    // every currency at once.
    //
    // A share of a divine rather than a chaos figure, so it tracks the economy the way
    // the routing anchors do. The override beside it is flat chaos, for a line that stays
    // exactly where you put it.
    public const int DefaultHidePercentOfDivine = 10;

    public const int CurrentScaleVersion = 8;

    // Version 7 moved all three axes onto chaos, and onto scales of different lengths:
    // currency 0-4, rooms 0-10, afflictions 0-6. A tier from the old 0-8 scale does not
    // mean anything on any of them - a 6 was a bad affliction and is now the hard block,
    // a 6 was a poor room and is now mildly bad - so an old profile is replaced rather
    // than converted. Reading numbers across as if they still meant the same thing would
    // be worse than starting from defaults, which is where these are chosen to be usable.
    private static void MigrateProfile(ProfileContent profile)
    {
        if (profile.ScaleVersion >= CurrentScaleVersion)
        {
            return;
        }

        profile.CurrencyUnitPriceOverrides = new Dictionary<string, int>();
        profile.RoomTiers = new Dictionary<string, int>(DefaultRoomTiers);
        profile.AfflictionTiers = new Dictionary<string, int>(DefaultAfflictionTiers);
        profile.HideRewardsBelowChaos = -1;
        profile.RunType = RunTypeNormal;
        profile.ScaleVersion = CurrentScaleVersion;
    }

    // The plugin owns the divine price, so the settings borrow it to show what a
    // percentage actually comes to. This is the market rate and zero when there is none,
    // rather than the figure routing settles on, so the hint can say which of the two it
    // is quoting instead of presenting the fallback as a price somebody paid.
    [JsonIgnore]
    public Func<double> DivineChaosProvider { get; set; }

    // Says which part is missing when there is no price, rather than leaving the hint to
    // guess at one of the three reasons.
    [JsonIgnore]
    public Func<string> DivinePriceStatusProvider { get; set; }

    private string DescribeDefaultHideChaos()
    {
        var market = DivineChaosProvider?.Invoke() ?? 0;
        var divine = market > 0 ? market : Routing.DivineChaosFallback.Value;
        var chaos = divine * DefaultHidePercentOfDivine / 100.0;
        if (market > 0)
        {
            return $", about {chaos:0}c with a divine at {divine:0}c";
        }

        var reason = DivinePriceStatusProvider?.Invoke();
        return $", about {chaos:0}c against the fallback divine price of {divine:0}c" +
               (reason is { Length: > 0 } ? $", because {reason}" : "");
    }

    // Shared with the settings groups below, which draw their own controls
    private static void Hint(string text) => SettingsHelp.Hint(text);

    private static void Tooltip(string text) => SettingsHelp.Tooltip(text);

    private static void DisabledText(string text) => SettingsHelp.DisabledText(text);

    private (string profileName, ProfileContent profile) GetCurrentProfile()
    {
        var profileName = CurrentProfile != null && Profiles.ContainsKey(CurrentProfile) ? CurrentProfile : Profiles.Keys.FirstOrDefault() ?? "Default";
        if (!Profiles.ContainsKey(profileName))
        {
            Profiles[profileName] = ProfileContent.CreateNew();
        }

        // Only fill in a name that was never set. Writing the fallback back unconditionally
        // destroyed the saved selection whenever this ran before Profiles was populated,
        // which is why the active profile did not survive a restart. A name that is set
        // but not yet found is left alone so it can match once the profiles load.
        if (CurrentProfile == null)
        {
            CurrentProfile = profileName;
        }

        MigrateProfile(Profiles[profileName]);

        var profile = Profiles[profileName];
        return (profileName, profile);
    }

    public int GetRoomTier(string type)
    {
        return GetCurrentProfile().profile.RoomTiers.GetValueOrDefault(type ?? "", RoomDefaultTier);
    }

    // Zero means "worth nothing", which is how a currency is ignored, so absent has to be
    // a different answer - the caller falls back to the market price.
    public bool TryGetCurrencyOverride(string type, out double chaos)
    {
        if (type != null && GetCurrentProfile().profile.CurrencyUnitPriceOverrides.TryGetValue(type, out var value) && value >= 0)
        {
            chaos = value;
            return true;
        }

        chaos = 0;
        return false;
    }

    // Read off the active profile, so the plugin keeps reading Settings.X unchanged.
    // JsonIgnore, or Newtonsoft would write these back out alongside the profiles.
    [JsonIgnore]
    // Only the relic runs duplicate the final reward; Normal is an ordinary run with
    // ordinary offers.
    public bool DuplicateRun => GetCurrentProfile().profile.RunType is RunTypeHourOfDivinity or RunTypeGildedChalice;

    [JsonIgnore]
    public int RunType => GetCurrentProfile().profile.RunType;

    [JsonIgnore]
    // Raw, with -1 meaning unset. The plugin resolves that against the live divine price
    // rather than a figure baked in here, so the default moves with the economy the same
    // way every other anchor does.
    public int HideRewardsBelowChaos => GetCurrentProfile().profile.HideRewardsBelowChaos;

    public int GetAfflictionTier(string type)
    {
        return GetCurrentProfile().profile.AfflictionTiers.GetValueOrDefault(type ?? "", AfflictionDefaultTier);
    }



    public ToggleNode Enable { get; set; } = new ToggleNode(true);

    public RoutingSettings Routing { get; set; } = new RoutingSettings();
    public MapDisplaySettings MapDisplay { get; set; } = new MapDisplaySettings();
    public TierColorSettings TierColors { get; set; } = new TierColorSettings();
    public InRoomSettings InRoom { get; set; } = new InRoomSettings();
    public RunTrackingSettings RunTracking { get; set; } = new RunTrackingSettings();
    public DebugSettings Debug { get; set; } = new DebugSettings();

    // Replace, or the shipped Default is merged back into a saved set every load and
    // cannot be deleted for good.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, ProfileContent> Profiles = new Dictionary<string, ProfileContent>
    {
        ["Default"] = ProfileContent.CreateNew(),
    };

    public string CurrentProfile = "Default";

    [JsonIgnore]
    public CustomNode TieringNode { get; set; }

}

public class ProfileContent
{
    // Profiles written before the 0-7 scale have no ScaleVersion, so Newtonsoft leaves
    // this at 1 and MigrateProfile knows to remap them. Code-created profiles are stamped
    // current by CreateNew.
    public int ScaleVersion = 1;

    // Normal rather than Default. Default applies no adjustments, which is the honest
    // baseline to compare the rest against and not what anybody actually runs.
    public int RunType = BetterSanctumDevSettings.RunTypeNormal;

    public int HideRewardsBelowChaos = -1;
    // Chaos per unit where you disagree with the market. Absent means use the price;
    // zero means the reward is worth nothing and should not pull a route.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> CurrencyUnitPriceOverrides = new();

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> RoomTiers = new(BetterSanctumDevSettings.DefaultRoomTiers);

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> AfflictionTiers = new(BetterSanctumDevSettings.DefaultAfflictionTiers);

    public static ProfileContent CreateNew()
    {
        return new ProfileContent { ScaleVersion = BetterSanctumDevSettings.CurrentScaleVersion };
    }
}

// A short description drawn at the top of a settings group. ExileCore renders the nodes
// themselves, so this is where the explanation of what a group does has to live.
public static class SettingsHelp
{
    // Hover marker after the control it explains
    public static void Hint(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            Tooltip(text);
        }
    }

    // ImGui's text calls take a printf format string, so a literal percent sign in any of
    // this prose is read as a specifier and swallows what follows it: "10% of a Divine
    // Orb" printed as "100f a Divine Orb". Affliction descriptions are full of them.
    // TextUnformatted does no formatting at all, which is what every one of these wants.
    //
    // Tooltips are wrapped as well. Unwrapped, a paragraph is laid out as one line and
    // the tooltip grows wider than the screen.
    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 40f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // Same colour as TextDisabled, without the format string
    public static void DisabledText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static CustomNode Block(params string[] lines)
    {
        return new CustomNode
        {
            DrawDelegate = () =>
            {
                foreach (var line in lines)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.Gray.ToImgui());
                    // TextWrapped is a format call, so a percent sign in the prose would
                    // be read as a specifier. Wrap by hand and print the string as it is.
                    ImGui.PushTextWrapPos(0f);
                    ImGui.TextUnformatted(line);
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleColor();
                }

                ImGui.Separator();
            }
        };
    }
}

[Submenu(CollapsedByDefault = true)]
public class RoutingSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Picks one room per layer from where you stand to the boss and frames it. Routes are scored in chaos: the reward you would take, less what the rooms and afflictions on the way cost.",
        "Rewards are priced from the price plugin and multiplied by the measured quantity for their slot, so a single-item reward in the third slot on floor 4 counts double. Any plugin registering the NinjaPrice.GetBaseItemTypeValue bridge method will do - Ninja Price and Get-Chaos-Value both answer to it.",
        "With no price plugin at all, the divine falls back to the figure below and every reward reads as the unknown reward figure, so rewards stop separating routes and only rooms and afflictions do. Price the ones you care about by hand with the currency overrides.",
        "Rooms and afflictions have no price of their own, so they are set as a percentage of a divine and move with it. A room at tier 0 is worth the room anchor, tier 5 nothing, tier 10 costs the anchor. An affliction at tier 5 costs the affliction anchor, and tier 6 is never walked into.",
        "Two things are absolute and are compared before any chaos: a reward worth more than Must Take Percent Of Divine is routed to through anything, including an affliction at 6, and an affliction at 6 is otherwise never entered.");

    public ToggleNode EnablePathfinding { get; set; } = new ToggleNode(true);
    public ColorNode BestPathColor { get; set; } = new(Color.Cyan);
    public RangeNode<int> BestPathFrameThickness { get; set; } = new RangeNode<int>(3, 0, 10);
    public RangeNode<int> BestPathLineThickness { get; set; } = new RangeNode<int>(4, 0, 10);

    // Anchors, as a percentage of a divine rather than a chaos figure, so the trade
    // between a reward and the pain of reaching it holds as the economy moves. Percent
    // rather than a fraction because the settings menu draws integer sliders.
    public RangeNode<int> RoomValuePercentOfDivine { get; set; } = new RangeNode<int>(40, 0, 300);
    public RangeNode<int> AfflictionCostPercentOfDivine { get; set; } = new RangeNode<int>(80, 0, 300);

    // What a reward is assumed to be worth when it cannot be read: a room the map has not
    // revealed, or a currency the price plugin does not know. Zero would make an unknown
    // room worthless and route you around everything you have not seen yet.
    public RangeNode<int> UnknownRewardPercentOfDivine { get; set; } = new RangeNode<int>(20, 0, 300);

    // A deal reads its rewards as empty on the map - they only exist once you are inside -
    // so it is worth an assumption rather than a price. Late floors only; before floor 3
    // a deal is worth the unknown reward figure above.
    public RangeNode<int> DealValuePercentOfDivine { get; set; } = new RangeNode<int>(50, 0, 300);

    // Falls back to this when no price plugin is present, so the anchors still resolve to
    // something and rooms and afflictions keep scoring against each other.
    public RangeNode<int> DivineChaosFallback { get; set; } = new RangeNode<int>(400, 1, 100000);

    // Past this much chaos a reward is worth walking through an affliction that would
    // otherwise be refused outright. It replaces the must-take rating: 500 is five divine,
    // the same line the top colour band is drawn at. Zero switches the override off, and
    // then nothing gets past a hard block.
    public RangeNode<int> MustTakePercentOfDivine { get; set; } = new RangeNode<int>(500, 0, 5000);
}

[Submenu(CollapsedByDefault = true)]
public class MapDisplaySettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Text and connection lines drawn over the Sanctum floor map.",
        "Each connection carries three stacked lines - currency, room type, affliction - coloured by the best of that kind reachable through it. Set line thickness to 0 to hide them and leave only the route frame.",
        "Hide under game UI drops any text, frame or line that would be covered by an open panel or the chat box, the same way the overlay already gives way to a room tooltip.",
        "Show reward prices needs a price plugin registering the NinjaPrice.GetBaseItemTypeValue bridge method - Ninja Price and Get-Chaos-Value both do. On the map it prices every reward that has a price, as the count and what that many come to (\"2x = 800c\"), which is the figure the route is scoring rather than a unit price beside it. Quantity is measured, since room data does not expose it. In the reward window all three offers are priced, with the quantity read from the offer text.",
        "Show prices in divine converts using the live Divine Orb price, read from the game's own reward list, and falls back to chaos while that is unknown.",
        "Isolate hovered room hides every other room's text and the connection lines while you hover, so a floor does not write more than can be read at once. The route itself stays visible.");

    public ColorNode TextColor { get; set; } = new ColorNode(Color.White);
    public ColorNode BackgroundColor { get; set; } = new ColorNode(Color.Black with { A = 128 });
    public RangeNode<int> ConnectionLineThickness { get; set; } = new RangeNode<int>(0, 0, 10);
    public ToggleNode HideUnderGameUi { get; set; } = new ToggleNode(true);
    // Needs a plugin registering the NinjaPrice.GetBaseItemTypeValue bridge method;
    // without one, prices are simply omitted
    public ToggleNode ShowRewardPrices { get; set; } = new ToggleNode(false);

    // Divine instead of chaos, using the live rate rather than a fixed number
    public ToggleNode ShowPricesInDivine { get; set; } = new ToggleNode(false);

    // Hovering a room hides every other room's text and the connection lines
    public ToggleNode IsolateHoveredRoom { get; set; } = new ToggleNode(true);
    public ToggleNode ShowEffectId { get; set; } = new ToggleNode(false);
    public ToggleNode ShowEffectName { get; set; } = new ToggleNode(true);
    public ToggleNode ShowEffectDescription { get; set; } = new ToggleNode(true);
}

[Submenu(CollapsedByDefault = true)]
public class TierColorSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "One ramp of nine shared by rewards, room types and afflictions, so a value reads the same wherever it appears. The three run on scales of different lengths, so each is mapped onto the ramp rather than indexing it directly.",
        "Rewards take 0 to 5 by what they are worth: 0 is 5 divine or more, then 1d, 0.5d, 0.3d, 0.1d, and 5 is everything below that.",
        "Room types spread across 1 to 8, tier 0 to tier 10. Afflictions only ever get worse, so they start at neutral and run 4 to 8, tier 0 to the hard block at 6. Empty colours anything the map has not revealed.");

    public ColorNode Tier0Color { get; set; } = new(Color.Magenta);
    public ColorNode Tier1Color { get; set; } = new(Color.Cyan);
    public ColorNode Tier2Color { get; set; } = new(Color.GreenYellow);
    public ColorNode Tier3Color { get; set; } = new(Color.PaleGreen);
    public ColorNode Tier4Color { get; set; } = new(Color.White);
    public ColorNode Tier5Color { get; set; } = new(Color.Orange);
    public ColorNode Tier6Color { get; set; } = new(Color.OrangeRed);
    public ColorNode Tier7Color { get; set; } = new(Color.Red);
    public ColorNode Tier8Color { get; set; } = new(Color.DarkRed);
    public ColorNode EmptyColor { get; set; } = new(Color.Gray);
}

[Submenu(CollapsedByDefault = true)]
public class InRoomSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Drawn in the room you are fighting in, rather than on the floor map.",
        "Spawners show lime while active and as a small marker while dormant. Hazards circle the meteor and holy beam telegraphs. Draw distance is in world units from your character.");

    public ToggleNode ShowGuardSpawners { get; set; } = new ToggleNode(true);
    public ToggleNode ShowHazards { get; set; } = new ToggleNode(true);
    public RangeNode<int> EffectDrawDistance { get; set; } = new RangeNode<int>(100, 20, 300);
    public ColorNode ActiveSpawnerColor { get; set; } = new(Color.Lime);
    public ColorNode DormantSpawnerColor { get; set; } = new(Color.LightBlue);
    public ColorNode HazardColor { get; set; } = new(Color.Red);
}

[Submenu(CollapsedByDefault = true)]
public class RunTrackingSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Records one run at a time and writes it on End Run. Logs/BetterSanctumDev/tracking/<tracking list>/ holds what belongs to a list: sanctum-runs.csv a row per run, sanctum-run-rooms.csv a row per room and reward slot, and sanctum-run-wide.csv a row per run with a column per currency.",
        "sanctum-deals.csv and sanctum-run-currency.csv sit above those in Logs/BetterSanctumDev/, and every run adds to them whichever list is selected. Their columns are fixed, so nothing in them depends on which currencies a list tracks, and what a deal is worth is a question answered by volume rather than by keeping the runs apart. Both carry a list column, since the duplicate-run rule changes which slot counts as taken and a shared file has to say which rules a row was written under.",
        "A tracking list is a set of columns and a folder of its own, so ordinary runs and duplicate runs can be recorded apart rather than averaged together. Switching lists switches which files are written; nothing is deleted by switching, or by deleting the list.",
        "The two spreadsheet files hold the same figures in the two shapes a spreadsheet wants: the currency file is what a pivot table groups, the wide file is what a chart plots. The wide file numbers its own runs and holds counts only - what a haul was worth is a valuation rather than something that dropped, and the per-currency chaos for that is in the currency file.",
        "A run writes two rows there: what it paid, and what the deals in it paid, in the same columns. The deal row is part of the run row rather than an addition to it, so filter on source rather than summing both. Column headings are shortened - divine, gcp, annul - while an override is still keyed on the full name.",
        "Which currencies get a column follows the price threshold below, so the sheet keeps up with the economy rather than a list written once. Chaos Orbs is always one of them - it is the unit the others are measured in. Tick the columns by hand instead to track something whatever it is worth, or to stop tracking something whatever it is worth.",
        "A wide file keeps the columns it was started with, so changing any of this only takes effect in a new one. Delete the file to pick up a changed set; the quantity columns will not add up to chaos either way, since the tail is what the file leaves out.",
        "Rows are marked map or window. Map is what the floor map showed. Window is what the reward window said while you stood in the room, which for a Deal room is the only place its rewards appear at all - the map reads them as empty.",
        "Start and End sit in a window that appears while you are in the Forbidden Sanctum hub. An unfinished run is kept in run-state.json, so restarting the HUD part way through does not lose it.",
        "What a run produced is worked out from the rooms you entered, assuming you took the most valuable slot in each. Nothing reads what you actually clicked, so treat the haul as an estimate - and an optimistic one, since the best slot is usually the end-of-Sanctum deferral, which pays nothing if the run ends early.",
        "On a duplicate run the assumption follows the same rule the offer window draws: the slots crossed out on screen are not counted as taken, so the haul cannot credit you with a reward the overlay told you to walk past.",
        "Track rewards appends every distinct reward seen to Logs/BetterSanctumDev/sanctum-rewards.csv: what the map offers and where, the room tooltip, and the reward window text. It fills in the measured quantity table the routing prices rewards from, so it is worth leaving on across a league.");

    public ToggleNode TrackRuns { get; set; } = new ToggleNode(false);

    // Records rather than diagnoses, which is why it is here and not under Debug. It fills
    // in the quantity table the routing prices rewards from, so it is worth leaving on
    // across a league rather than turning on to look at something.
    public ToggleNode TrackRewards { get; set; } = new ToggleNode(false);

    // A share of a divine rather than a chaos figure, so the columns keep up with the
    // economy instead of a list written once and left to rot - an exalt led the sheet this
    // was modelled on and is worth under two chaos now. -1 follows it; anything else is
    // that many chaos, flat.
    public const double DefaultTrackedPercentOfDivine = 1.5;

    public const int OverrideInclude = 0;
    public const int OverrideReplace = 1;

    public static readonly string[] OverrideModeNames =
    {
        "Include - tracked as well as anything over the threshold",
        "Replace - track only what is ticked",
    };

    // A tracking profile is a set of columns, and two sets of columns cannot share a file,
    // so each writes into a folder of its own under tracking/. Switching profiles switches
    // which set of files is being written, which is what makes separate records of, say,
    // ordinary runs and duplicate runs possible.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, TrackingProfile> Profiles { get; set; } =
        new Dictionary<string, TrackingProfile> { ["Default"] = new TrackingProfile() };

    public string CurrentProfile { get; set; } = "Default";

    // Set by the plugin so the threshold hint can say what the share comes to in chaos
    [JsonIgnore]
    public Func<double> TrackedFloorProvider { get; set; }

    // Set by the plugin, which knows where the files go
    [JsonIgnore]
    public Action OpenTrackingFolder { get; set; }

    [JsonIgnore]
    public Action ExportWorkbook { get; set; }

    [JsonIgnore]
    public Action ExportTemplate { get; set; }

    public TrackingProfile Profile()
    {
        var name = CurrentProfile != null && Profiles.ContainsKey(CurrentProfile)
            ? CurrentProfile
            : Profiles.Keys.FirstOrDefault() ?? "Default";

        if (!Profiles.ContainsKey(name))
        {
            Profiles[name] = new TrackingProfile();
        }

        CurrentProfile ??= name;
        return Profiles[name];
    }

    // A folder name, so it has to survive being one. Anything Windows will not take in a
    // path becomes an underscore rather than an error nobody would connect to the name.
    public string ProfileFolder()
    {
        var name = CurrentProfile != null && Profiles.ContainsKey(CurrentProfile)
            ? CurrentProfile
            : "Default";

        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_').ToArray()).Trim();
        return safe.Length > 0 ? safe : "Default";
    }

    [JsonIgnore]
    public CustomNode TrackedCurrencyNode { get; set; }

    public RunTrackingSettings()
    {
        var filter = "";
        var renameBuffer = "";
        string renameBufferOwner = null;
        TrackedCurrencyNode = new CustomNode
        {
            DrawDelegate = () =>
            {
                var profile = Profile();

                if (ImGui.Button("Open tracking folder##openTracking"))
                {
                    OpenTrackingFolder?.Invoke();
                }

                SettingsHelp.Hint("Opens the folder this profile writes into. Each profile has one of its own, since a profile is a set of columns and two sets cannot share a file.");

                ImGui.SameLine();
                if (ImGui.Button("Export xlsx##exportTracking"))
                {
                    ExportWorkbook?.Invoke();
                }

                SettingsHelp.Hint("Rebuilds sanctum-run-wide.xlsx from the wide CSV: header frozen, columns sized, numbers written as numbers. Without runId, which is in the CSV so the other files can join on it and has nothing to join to here." +
                     "\n\nTwo sheets. Runs holds a row per run and per deal, with the totals as formulas against Prices - so correcting a price there re-totals every run in the file. Prices lists every currency, not only the tracked ones, since a workbook may be totalling a file written when a different set was tracked." +
                     "\n\nThe CSV stays the record. It is appended a line at a time, where a workbook is rewritten whole - so this is generated on demand and can be deleted and rebuilt whenever.");

                ImGui.SameLine();
                if (ImGui.Button("Blank template##templateTracking"))
                {
                    ExportTemplate?.Invoke();
                }

                SettingsHelp.Hint("Writes sanctum-run-template.xlsx: the same workbook with no runs in it, twenty pairs of empty rows to fill in by hand." +
                     "\n\nThe totals are the same formulas, so a quantity typed into a currency column prices itself. For recording runs without the HUD having written them - the columns are the ones this list tracks and the prices are today's.");

                foreach (var key in Profiles.Keys.OrderBy(x => x).ToList())
                {
                    if (key == CurrentProfile)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, Color.DarkGreen.ToImgui());
                    }

                    if (ImGui.Button($"{key}##selectTracking{key}") && key != CurrentProfile)
                    {
                        CurrentProfile = key;
                    }

                    if (key == CurrentProfile)
                    {
                        ImGui.PopStyleColor();
                    }

                    ImGui.SameLine();
                }

                ImGui.NewLine();

                // The buffer survives across frames and is written back on Enter only.
                // Committing every keystroke renames the profile out from under the widget.
                if (renameBufferOwner != CurrentProfile)
                {
                    renameBufferOwner = CurrentProfile;
                    renameBuffer = CurrentProfile ?? "";
                }

                if (ImGui.InputText("Name##renameTracking", ref renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    RenameProfile(CurrentProfile, renameBuffer);
                }

                SettingsHelp.Hint("Press Enter to rename. The folder the profile writes into is named after it, so renaming starts a new one - the old folder keeps the runs already in it.");

                if (ImGui.Button("Add tracking list##addTracking"))
                {
                    var name = Enumerable.Range(0, 100)
                        .Select(x => $"Tracking list {x}")
                        .First(x => !Profiles.ContainsKey(x));
                    Profiles[name] = new TrackingProfile();
                    CurrentProfile = name;
                }

                // Deleting the last one would leave nothing to fall back to
                if (Profiles.Count > 1)
                {
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete {CurrentProfile}##deleteTracking"))
                    {
                        Profiles.Remove(CurrentProfile);
                        CurrentProfile = Profiles.Keys.First();
                    }
                }

                SettingsHelp.Hint("Deleting a list does not delete what it recorded. Its folder stays where it is.");

                ImGui.Separator();

                var threshold = profile.MinChaos;
                if (ImGui.InputInt("Track currencies worth at least (chaos) override", ref threshold))
                {
                    profile.MinChaos = threshold < 0 ? -1 : threshold;
                }

                var floor = TrackedFloorProvider?.Invoke() ?? 0;
                SettingsHelp.Hint("A currency worth at least this much a unit gets a column in the wide run file." +
                     $"\n\n-1 uses the default, which is {DefaultTrackedPercentOfDivine}% of a Divine Orb" +
                     (floor > 0 ? $", about {floor:0.#}c right now" : "") + "." +
                     " Being a share of a divine it moves as the economy does." +
                     "\n0 tracks everything." +
                     "\n\nAnything else is that many chaos, flat." +
                     "\n\nChaos Orbs always has a column whatever the threshold, being the unit the rest are counted in." +
                     "\n\nA wide file keeps the columns it was started with, so a change here only takes effect in a new one.");

                var over = profile.Override;
                if (ImGui.Checkbox("Override tracked currencies", ref over))
                {
                    profile.Override = over;
                }

                if (!profile.Override)
                {
                    return;
                }

                var mode = profile.OverrideMode;
                if (ImGui.Combo("Ticked currencies", ref mode, OverrideModeNames, OverrideModeNames.Length))
                {
                    profile.OverrideMode = mode;
                }

                SettingsHelp.Hint("Include keeps the threshold and adds what is ticked, for something worth following whatever it prices at." +
                     "\nReplace ignores the threshold entirely and tracks the ticked list alone - including dropping Chaos Orbs, if it is not ticked.");

                ImGui.InputTextWithHint("##TrackedCurrencyFilter", "Filter", ref filter, 100);
                foreach (var type in BetterSanctumDevSettings.CurrencyTypes)
                {
                    if (filter.Length > 0 &&
                        !type.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    var tracked = profile.Currencies.GetValueOrDefault(type, false);
                    if (ImGui.Checkbox(type, ref tracked))
                    {
                        profile.Currencies[type] = tracked;
                    }
                }
            }
        };
    }

    private void RenameProfile(string oldName, string newName)
    {
        newName = newName?.Trim();
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
            newName == oldName || Profiles.ContainsKey(newName) ||
            !Profiles.Remove(oldName, out var content))
        {
            return;
        }

        Profiles[newName] = content;
        CurrentProfile = newName;
    }
}

public class TrackingProfile
{
    // -1 follows the divine price; anything else is that many chaos, flat
    public int MinChaos { get; set; } = -1;

    public bool Override { get; set; }

    public int OverrideMode { get; set; } = RunTrackingSettings.OverrideInclude;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, bool> Currencies { get; set; } = new Dictionary<string, bool>();
}

[Submenu(CollapsedByDefault = true)]
public class DebugSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Writes Logs/BetterSanctumDev/room-dump.txt once each time the floor map is opened, listing the raw data behind every room.",
        "Probe sanctum state appends to Logs/BetterSanctumDev/sanctum-probe.txt: every area you enter, and the floor data - gold, resolve, room choices and accrued rewards - each time it changes. Turn it on for one full run, from the Forbidden Sanctum through all four floors and back out, then read the file.");

    public ToggleNode DebugDumpRoomData { get; set; } = new ToggleNode(false);
    public ToggleNode ProbeSanctumState { get; set; } = new ToggleNode(false);
}
