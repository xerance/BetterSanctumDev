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

namespace BetterSanctum;

public class BetterSanctumTrackerSettings : ISettings
{
    private static readonly IReadOnlyList<string> CurrencyTypes = new List<string>
    {
        "Orbs of Alteration",
        "Orbs of Chance",
        "Glassblower's Baubles",
        "Chromatic Orbs",
        "Jeweller's Orbs",
        "Orbs of Alchemy",
        "Orbs of Fusing",
        "Orbs of Scouring",
        "Cartographer's Chisels",
        "Chaos Orbs",
        "Orbs of Binding",
        "Orbs of Regret",
        "Gemcutter's Prisms",
        "Blessed Orbs",
        "Vaal Orbs",
        "Orbs of Horizon",
        "Instilling Orbs",
        "Regal Orbs",
        "Enkindling Orbs",
        "Orbs of Unmaking",
        "Awakened Sextants",
        "Stacked Decks",
        "Veiled Chaos Orbs",
        "Orbs of Annulment",
        "Divine Orbs",
        "Exalted Orbs",
        "Divine Vessels",
        "Sacred Orbs",
        "Mirrors of Kalandra",
        "Blacksmith's Whetstones",
        "Armourer's Scraps",
        "Orbs of Transmutation",
        "Orbs of Augmentation",
        "Fracturing Orbs",
        "Volatile Vaal Orbs",
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

    public BetterSanctumTrackerSettings()
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

                Hint("A profile holds the tier values, the run type and the currency cutoff. Colours and display settings are shared across all profiles.");

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
                     "\nHour of Divinity blocks boons: BoonFountain drops to worth nothing and the coin bias is dropped, since coins buy boons." +
                     "\nGilded Chalice blocks resolve recovery: Fountain drops to worth nothing. CurseFountain is never adjusted." +
                     "\n\nA hard-blocked affliction is never adjusted by any of them.");

                // -1 for "unset" the same way the currency overrides read it, so the two
                // fields behave alike rather than one clamping where the other clears.
                var hideRewardsBelowChaos = profile.HideRewardsBelowChaos;
                if (ImGui.InputInt("Hide rewards worth less than (chaos)", ref hideRewardsBelowChaos))
                {
                    profile.HideRewardsBelowChaos = hideRewardsBelowChaos < 0 ? -1 : hideRewardsBelowChaos;
                }

                Hint("Rewards worth less than this are left out of the room text on the map. It does not affect routing - a hidden reward is still scored." +
                     $"\n-1 follows the divine price, at {DefaultHidePercentOfDivine}% of one, so it moves as the economy does. 0 shows everything." +
                     "\nA figure you type is absolute chaos and stays where you put it, so it is worth revisiting when prices move.");

                ImGui.TextDisabled("Every axis is scored in chaos. Rewards are priced; rooms and afflictions are priced from an anchor set in Routing.");
                Hint("Currency 0-4: 0 is taken whatever stands in the way, 4 is ignored, 1-3 all count their full price until you bias them in Routing." +
                     "\nRoom 0-10: 0 is worth the room anchor, 5 is worth nothing, 10 costs the anchor." +
                     "\nAffliction 0-6: 0 costs nothing, 5 costs the affliction anchor, 6 is never walked into unless a currency 0 lies beyond it.");

                if (ImGui.TreeNode("Currency price overrides"))
                {
                    ImGui.TextDisabled("A reward's band is read off what it is worth, so there is nothing to rate. Override the chaos each one is worth where you disagree with the market, or set 0 to ignore it.");
                    ImGui.InputTextWithHint("##CurrencyFilter", "Filter", ref currencyFilter, 100);
                    var (currencyTypes, fromGameFiles) = GetKnownCurrencyTypes();
                    ImGui.TextDisabled($"{currencyTypes.Count} currencies ({(fromGameFiles ? "from game files" : "fallback list")})");
                    ImGui.TextDisabled("Blank or -1 leaves the price alone. The value is per unit; the quantity of the slot is applied on top.");

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
                    ImGui.TextDisabled("Applies to both the fight room and the reward room, so a room is counted twice from this one list.");
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
                    ImGui.TextDisabled("Filter matches names and descriptions, and several words all have to match.");
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
                            ImGui.SetTooltip(description);
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
    // Deal is neutral too. What a deal pays is its own setting, since the map reads its
    // rewards as empty, and grading the room type as well would count it twice.
    public static readonly IReadOnlyDictionary<string, int> DefaultRoomTiers = new Dictionary<string, int>
    {
        ["Explore"] = 4,
        ["Maze"] = 5,
        ["Puzzle"] = 5,
        ["Gauntlet"] = 5,
        ["Lair"] = 5,
        ["Vault"] = 5,
        ["Boss"] = 5,
        ["Miniboss"] = 6,
        ["Arena"] = 6,
        ["Merchant"] = 4,
        ["BoonFountain"] = 4,
        ["RainbowFountain"] = 3,
        ["Deferral"] = 5,
        ["Fountain"] = 5,
        ["Treasure"] = 3,
        ["TreasureMinor"] = 4,
        ["Deal"] = 5,
        ["Final"] = 5,
        ["CurseFountain"] = 7,
    };

    // 6 is reserved for the run-enders and is absolute; 0 to 5 are costs running from
    // nothing up to the whole anchor.
    public static readonly IReadOnlyDictionary<string, int> DefaultAfflictionTiers = new Dictionary<string, int>
    {
        ["Accursed Prism"] = 6,
        ["Poisoned Water"] = 6,
        ["Golden Smoke"] = 6,
        ["Deadly Snare"] = 6,
        ["Ghastly Scythe"] = 6,
        ["Orb of Negation"] = 6,
        ["Purple Smoke"] = 4,
        ["Liquid Cowardice"] = 4,
        ["Deceptive Mirror"] = 4,
        ["Glass Shard"] = 4,
        ["Cutpurse"] = 3,
        ["Veiled Sight"] = 3,
        ["Red Smoke"] = 3,
        ["Floor Tax"] = 3,
        ["Unhallowed Amulet"] = 3,
        ["Rusted Coin"] = 3,
        ["Chiselled Stone"] = 3,
        ["Fiendish Wings"] = 3,
        ["Empty Trove"] = 3,
        ["Demonic Skull"] = 3,
        ["Unassuming Brick"] = 3,
        ["Rapid Quicksand"] = 3,
        ["Door Tax"] = 3,
        ["Unhallowed Ring"] = 3,
        ["Phantom Illusion"] = 3,
        ["Worn Sandals"] = 2,
        ["Black Smoke"] = 2,
        ["Tattered Blindfold"] = 2,
        ["Anomaly Attractor"] = 2,
        ["Unquenched Thirst"] = 2,
        ["Dark Pit"] = 2,
        ["Unholy Urn"] = 2,
        ["Haemorrhage"] = 2,
        ["Mark of Terror"] = 2,
        ["Concealed Anomaly"] = 2,
        ["Spiked Shell"] = 2,
        ["Honed Claws"] = 2,
        ["Tight Choker"] = 2,
        ["Spilt Purse"] = 2,
        ["Charred Coin"] = 2,
        ["Corrupted Lockpick"] = 2,
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

    // A tenth of a divine while one is around 400 chaos, which is the bottom colour band
    // and little more than a floor under the noise. Deliberately low: silencing one
    // currency is what an override of zero is for, and a default that hid by price would
    // take that decision away from every currency at once.
    //
    // Absolute rather than a fraction of a divine, because it is a display filter you set
    // once and want to stay where you put it - worth revisiting when the economy moves.
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
        profile.RunType = RunTypeDefault;
        profile.ScaleVersion = CurrentScaleVersion;
    }

    // Hover marker after the control it explains
    private static void Hint(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

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

    public int RunType = BetterSanctumTrackerSettings.RunTypeDefault;

    // Superseded by RunType. Read once by MigrateProfile, unused after.
    public bool DuplicateRun = false;
    public int HideRewardsBelowChaos = -1;
    // Chaos per unit where you disagree with the market. Absent means use the price;
    // zero means the reward is worth nothing and should not pull a route.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> CurrencyUnitPriceOverrides = new();

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> RoomTiers = new(BetterSanctumTrackerSettings.DefaultRoomTiers);

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> AfflictionTiers = new(BetterSanctumTrackerSettings.DefaultAfflictionTiers);

    public static ProfileContent CreateNew()
    {
        return new ProfileContent { ScaleVersion = BetterSanctumTrackerSettings.CurrentScaleVersion };
    }
}

// A short description drawn at the top of a settings group. ExileCore renders the nodes
// themselves, so this is where the explanation of what a group does has to live.
public static class SettingsHelp
{
    public static CustomNode Block(params string[] lines)
    {
        return new CustomNode
        {
            DrawDelegate = () =>
            {
                foreach (var line in lines)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.Gray.ToImgui());
                    ImGui.TextWrapped(line);
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
        "Rewards are priced from the price plugin and multiplied by the measured quantity for their slot, so the third slot on floor 4 counts double. Without a price plugin nothing has a price and only the anchors below score.",
        "Rooms and afflictions have no price of their own, so they are set as a percentage of a divine and move with it. A room at tier 0 is worth the room anchor, tier 5 nothing, tier 10 costs the anchor. An affliction at tier 5 costs the affliction anchor, and tier 6 is never walked into.",
        "Two things are absolute and are compared before any chaos: a currency at tier 0 is routed to through anything, including an affliction at 6, and an affliction at 6 is otherwise never entered.");

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
        "Show reward prices needs the Ninja Price plugin. On the map it prices only the tiers Price max tier allows, since a price on something you rated low is clutter; in the reward window it prices all three offers, which is where choosing between them happens. Either way it is the price of one: reward quantity is not exposed anywhere in room data.",
        "Show prices in divine converts using the live Divine Orb price, read from the game's own reward list, and falls back to chaos while that is unknown.",
        "Isolate hovered room hides every other room's text and the connection lines while you hover, so a floor does not write more than can be read at once. The route itself stays visible.");

    public ColorNode TextColor { get; set; } = new ColorNode(Color.White);
    public ColorNode BackgroundColor { get; set; } = new ColorNode(Color.Black with { A = 128 });
    public RangeNode<int> ConnectionLineThickness { get; set; } = new RangeNode<int>(0, 0, 10);
    public ToggleNode HideUnderGameUi { get; set; } = new ToggleNode(true);
    // Needs the Ninja Price plugin; without it prices are simply omitted
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
        "One ramp shared by currency, room types and afflictions, so a value reads the same wherever it appears.",
        "0 always route through, 1-3 good, 4 neutral, 5-7 bad, 8 never route through. Empty colours anything the map has not revealed.");

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
        "Records one run at a time and writes it to Logs/BetterSanctumTracker/ on End Run: sanctum-runs.csv holds a row per floor, sanctum-run-rooms.csv a row per room and reward slot.",
        "Rows are marked map or window. Map is what the floor map showed. Window is what the reward window said while you stood in the room, which for a Deal room is the only place its rewards appear at all - the map reads them as empty.",
        "Start and End sit in a window that appears while you are in the Forbidden Sanctum hub. An unfinished run is kept in run-state.json, so restarting the HUD part way through does not lose it.",
        "What a run produced is worked out from the rooms you entered, assuming you took the most valuable slot in each. Nothing reads what you actually clicked, so treat the haul as an estimate - and an optimistic one, since the best slot is usually the end-of-Sanctum deferral, which pays nothing if the run ends early.");

    public ToggleNode TrackRuns { get; set; } = new ToggleNode(false);
}

[Submenu(CollapsedByDefault = true)]
public class DebugSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Writes Logs/BetterSanctumTracker/room-dump.txt once each time the floor map is opened, listing the raw data behind every room.",
        "Track rewards appends every distinct reward seen to Logs/BetterSanctumTracker/sanctum-rewards.csv: what the map offers and where, the room tooltip, and the reward window text. Leave it on across runs and the table fills in.",
        "Probe sanctum state appends to Logs/BetterSanctumTracker/sanctum-probe.txt: every area you enter, and the floor data - gold, resolve, room choices and accrued rewards - each time it changes. Turn it on for one full run, from the Forbidden Sanctum through all four floors and back out, then read the file.");

    public ToggleNode DebugDumpRoomData { get; set; } = new ToggleNode(false);
    public ToggleNode TrackRewards { get; set; } = new ToggleNode(false);
    public ToggleNode ProbeSanctumState { get; set; } = new ToggleNode(false);
}
