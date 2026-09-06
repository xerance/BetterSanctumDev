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

                Hint("Both relics duplicate the final reward, so either marks the offers not worth taking." +
                     "\n\nHour of Divinity blocks boons: BoonFountain drops to neutral and the early Treasure and Merchant bias is dropped." +
                     "\nGilded Chalice blocks resolve recovery: Fountain drops to neutral. CurseFountain is never adjusted.");

                var hideCurrencyBelowTier = profile.HideCurrencyBelowTier;
                if (ImGui.SliderInt("Hide currency below tier", ref hideCurrencyBelowTier, PrioritizeValue, BlockValue))
                {
                    profile.HideCurrencyBelowTier = hideCurrencyBelowTier;
                }

                Hint("Currencies rated worse than this are left out of the room text on the map. It does not affect routing.");

                ImGui.TextDisabled("0 = always route through, 1-3 = good, 4 = neutral, 5-7 = bad (7 heavily so), 8 = never route through");
                Hint("Below 4 adds to a route, above 4 subtracts, and the further from 4 the more it counts." +
                     "\nWeights: 1 = +100, 2 = +10, 3 = +5, 5 = -5, 6 = -10, 7 = -120." +
                     "\n0 and 8 are absolute: a 0 is always routed to, an 8 never is unless a 0 lies beyond it.");

                if (ImGui.TreeNode("Currency tiering"))
                {
                    ImGui.TextDisabled("Rated per reward slot. Only the best slot of a room counts, and the third slot counts twice since it pays double.");
                    ImGui.InputTextWithHint("##CurrencyFilter", "Filter", ref currencyFilter, 100);
                    var (currencyTypes, fromGameFiles) = GetKnownCurrencyTypes();
                    ImGui.TextDisabled($"{currencyTypes.Count} currencies ({(fromGameFiles ? "from game files" : "fallback list")})");
                    foreach (var type in currencyTypes)
                    {
                        for (int order = 0; order < 3; order++)
                        {
                            // Filter against the full label so the slot words are searchable too
                            var label = $"{type} ({order switch { 0 => "first", 1 => "second", 2 => "third" }})";
                            if (!MatchesFilter(label, currencyFilter))
                            {
                                continue;
                            }

                            var currentValue = GetCurrencyTier(type, order);
                            if (ImGui.SliderInt(label, ref currentValue, PrioritizeValue, BlockValue))
                            {
                                profile.CurrencyTiers[$"{type}/{order}"] = currentValue;
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
                        if (ImGui.SliderInt(type, ref currentValue, PrioritizeValue, BlockValue))
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
                        if (ImGui.SliderInt(type, ref currentValue, PrioritizeValue, BlockValue))
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

    // Values run 0-8 with 4 neutral. The ends are constraints rather than weights and
    // score nothing: 0 means route through this if at all possible, 8 means never. 1-3
    // and 5-7 are ordinary positive and negative weights, 7 being an outsized penalty.
    public const int PrioritizeValue = 0;
    public const int NeutralValue = 4;
    public const int BlockValue = 8;

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

    // A bare name applies to every reward slot; a "name/slot" key overrides one slot.
    public static readonly IReadOnlyDictionary<string, int> DefaultCurrencyTiers = new Dictionary<string, int>
    {
        ["Mirrors of Kalandra"] = 0,
        ["Divine Orbs"] = 1,
        ["Fracturing Orbs"] = 1,
        ["Volatile Vaal Orbs"] = 1,
        ["Chaos Orbs"] = 2,
        ["Stacked Decks"] = 2,
        ["Veiled Chaos Orbs"] = 2,
        ["Orbs of Annulment"] = 2,
        ["Exalted Orbs"] = 2,
        ["Chaos Orbs/0"] = 3,
        ["Chaos Orbs/1"] = 3,
        ["Chaos Orbs/2"] = 4,
        ["Orbs of Annulment/0"] = 3,
        ["Orbs of Annulment/2"] = 4,
        ["Orbs of Annulment/1"] = 3,
        ["Exalted Orbs/0"] = 3,
        ["Exalted Orbs/1"] = 3,
        ["Exalted Orbs/2"] = 3,
        ["Ancient Orbs/0"] = 3,
        ["Ancient Orbs/1"] = 3,
        ["Sacred Orbs/0"] = 3,
        ["Sacred Orbs/1"] = 3,
        ["Sacred Orbs/2"] = 3,
        ["Stacked Decks/0"] = 3,
        ["Stacked Decks/1"] = 3,
        ["Stacked Decks/2"] = 4,
        ["Chromatic Orbs/0"] = 3,
        ["Chromatic Orbs/1"] = 3,
    };

    // Fight rooms are graded on the resolve they tend to cost, reward rooms on what
    // they hand you. Boss and Final sit at neutral deliberately: they are in the last
    // layer that every route passes through, so their value cannot separate two routes.
    public static readonly IReadOnlyDictionary<string, int> DefaultRoomTiers = new Dictionary<string, int>
    {
        ["Explore"] = 3,
        ["Maze"] = 4,
        ["Puzzle"] = 4,
        ["Gauntlet"] = 4,
        ["Lair"] = 4,
        ["Vault"] = 4,
        ["Boss"] = 4,
        ["Miniboss"] = 4,
        ["Arena"] = 6,
        ["Merchant"] = 3,
        ["BoonFountain"] = 3,
        ["RainbowFountain"] = 3,
        ["Deferral"] = 4,
        ["Fountain"] = 4,
        ["Treasure"] = 3,
        ["TreasureMinor"] = 4,
        ["Deal"] = 4,
        ["Final"] = 4,
        ["CurseFountain"] = 4,
    };

    // 8 is reserved for the run-enders. Everything else is a weight, not a bar.
    public static readonly IReadOnlyDictionary<string, int> DefaultAfflictionTiers = new Dictionary<string, int>
    {
        ["Accursed Prism"] = 8,
        ["Poisoned Water"] = 8,
        ["Cutpurse"] = 6,
        ["Purple Smoke"] = 7,
        ["Veiled Sight"] = 6,
        ["Red Smoke"] = 6,
        ["Golden Smoke"] = 8,
        ["Deadly Snare"] = 8,
        ["Floor Tax"] = 6,
        ["Liquid Cowardice"] = 7,
        ["Unhallowed Amulet"] = 6,
        ["Rusted Coin"] = 6,
        ["Chiselled Stone"] = 6,
        ["Fiendish Wings"] = 6,
        ["Empty Trove"] = 6,
        ["Demonic Skull"] = 6,
        ["Unassuming Brick"] = 6,
        ["Worn Sandals"] = 5,
        ["Ghastly Scythe"] = 8,
        ["Rapid Quicksand"] = 6,
        ["Black Smoke"] = 5,
        ["Deceptive Mirror"] = 7,
        ["Tattered Blindfold"] = 5,
        ["Door Tax"] = 6,
        ["Anomaly Attractor"] = 5,
        ["Unquenched Thirst"] = 5,
        ["Dark Pit"] = 5,
        ["Orb of Negation"] = 8,
        ["Unholy Urn"] = 5,
        ["Haemorrhage"] = 5,
        ["Mark of Terror"] = 5,
        ["Concealed Anomaly"] = 5,
        ["Spiked Shell"] = 5,
        ["Honed Claws"] = 5,
        ["Unhallowed Ring"] = 6,
        ["Tight Choker"] = 5,
        ["Spilt Purse"] = 5,
        ["Charred Coin"] = 5,
        ["Phantom Illusion"] = 6,
        ["Corrupted Lockpick"] = 5,
        ["Glass Shard"] = 7,
    };


    public const int RunTypeNormal = 0;
    public const int RunTypeHourOfDivinity = 1;
    public const int RunTypeGildedChalice = 2;

    public static readonly string[] RunTypeNames = { "Normal", "The Hour of Divinity", "The Gilded Chalice" };

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

    public const int CurrentScaleVersion = 6;

    // The old scales were 1-5 for currency and 1-3 for rooms and afflictions, both with
    // 1 best. Nothing is mapped onto 0 or 7 - promoting a room to "never enter" is a
    // decision to make deliberately, not one to inherit from a rescale.
    private static int MigrateTriTier(int value) => value switch { 1 => 2, 2 => 4, 3 => 6, _ => NeutralValue };

    private static int MigrateCurrencyTier(int value) => value switch { 1 => 1, 2 => 2, 3 => 4, 4 => 5, 5 => 6, _ => NeutralValue };

    private static void MigrateProfile(ProfileContent profile)
    {
        if (profile.ScaleVersion >= CurrentScaleVersion)
        {
            return;
        }

        profile.CurrencyTiers = profile.CurrencyTiers.ToDictionary(x => x.Key, x => MigrateCurrencyTier(x.Value));
        profile.RoomTiers = profile.RoomTiers.ToDictionary(x => x.Key, x => MigrateTriTier(x.Value));
        profile.AfflictionTiers = profile.AfflictionTiers.ToDictionary(x => x.Key, x => MigrateTriTier(x.Value));
        // 5 was the old maximum and meant "hide nothing", which is 7 on the new scale
        profile.HideCurrencyBelowTier = profile.HideCurrencyBelowTier >= 5 ? BlockValue : MigrateCurrencyTier(profile.HideCurrencyBelowTier);
        if (profile.ScaleVersion < 3 && profile.DuplicateRun)
        {
            // The old checkbox meant Hour of Divinity specifically
            profile.RunType = RunTypeHourOfDivinity;
        }

        if (profile.ScaleVersion < 4)
        {
            // Seed only the room types the profile never had an entry for. Upstream set
            // four and left the rest implicitly neutral, which was minimalism rather than
            // a judgement, so filling them in does not overwrite anything you chose.
            foreach (var (roomType, tier) in DefaultRoomTiers)
            {
                if (!profile.RoomTiers.ContainsKey(roomType))
                {
                    profile.RoomTiers[roomType] = tier;
                }
            }
        }

        if (profile.ScaleVersion < 5 && profile.HideCurrencyBelowTier == 7)
        {
            // 7 was the top of the scale and meant "hide nothing" until 8 was added
            profile.HideCurrencyBelowTier = BlockValue;
        }

        if (profile.ScaleVersion < 6)
        {
            foreach (var (currency, tier) in DefaultCurrencyTiers)
            {
                if (!profile.CurrencyTiers.ContainsKey(currency))
                {
                    profile.CurrencyTiers[currency] = tier;
                }
            }

            // Only lift entries still holding the value they were shipped with, so a
            // rating you actually chose is never overwritten by a change of default.
            if (profile.CurrencyTiers.GetValueOrDefault("Mirrors of Kalandra") == 1)
            {
                profile.CurrencyTiers["Mirrors of Kalandra"] = PrioritizeValue;
            }

            if (profile.RoomTiers.GetValueOrDefault("Explore") == 2)
            {
                profile.RoomTiers["Explore"] = 3;
            }
        }

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
        return GetCurrentProfile().profile.RoomTiers.GetValueOrDefault(type ?? "", NeutralValue);
    }

    public int GetCurrencyTier(string type, int order)
    {
        var currencyTiers = GetCurrentProfile().profile.CurrencyTiers;
        return currencyTiers.TryGetValue($"{type ?? ""}/{order}", out var tier) ||
               currencyTiers.TryGetValue(type ?? "", out tier)
            ? tier
            : NeutralValue;
    }

    // Read off the active profile, so the plugin keeps reading Settings.X unchanged.
    // JsonIgnore, or Newtonsoft would write these back out alongside the profiles.
    [JsonIgnore]
    public bool DuplicateRun => GetCurrentProfile().profile.RunType != RunTypeNormal;

    [JsonIgnore]
    public int RunType => GetCurrentProfile().profile.RunType;

    [JsonIgnore]
    public int HideCurrencyBelowTier => GetCurrentProfile().profile.HideCurrencyBelowTier;

    public int GetAfflictionTier(string type)
    {
        return GetCurrentProfile().profile.AfflictionTiers.GetValueOrDefault(type ?? "", NeutralValue);
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

    public int RunType = BetterSanctumTrackerSettings.RunTypeNormal;

    // Superseded by RunType. Read once by MigrateProfile, unused after.
    public bool DuplicateRun = false;
    public int HideCurrencyBelowTier = 3;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> CurrencyTiers = new(BetterSanctumTrackerSettings.DefaultCurrencyTiers);

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

[Submenu(CollapsedByDefault = false)]
public class RoutingSettings
{
    [JsonIgnore]
    public CustomNode Help { get; set; } = SettingsHelp.Block(
        "Picks one room per layer from where you stand to the boss and frames it.",
        "Rooms are counted by tier and weighted per axis, because the same tier means different things: a tier-1 reward is 100 while a tier-2 is only 3, so lesser rewards never outweigh a calmer route. A bad affliction is -70, so one tier-1 reward is worth one but not two. Room type sits between the two.",
        "Tier 0 is always routed to and tier 8 never is, unless a 0 lies beyond it.",
        "Use prices in routing asks the Ninja Price plugin what a reward is worth and adds it as capped points, so currencies you rated the same are ordered by value - fracturing over divine when it is worth more. Quantity is assumed to be one, or two for the third slot on floor 4, which is close for the expensive currencies that decide routes and understates cheap ones that do not. Only tiers up to Price max tier are affected, since the cap bounds one room rather than a whole route, and the tier counted is the one you assigned rather than the floor-adjusted one - otherwise the floor 3 bonus would drag cheap stacked currency into the priced band.",
        "Bias strength scales the floor adjustments only, each point being one tier step: on floors 3-4 good currency improves and Deal gains 50 points, on floors 1-2 Treasure and Merchant improve unless you are running Hour of Divinity. Set it to 0 to score purely on the tiers you assigned.",
        "Relic adjustments are not scaled and apply at any strength: Hour of Divinity flattens BoonFountain to neutral, Gilded Chalice flattens Fountain.");

    public ToggleNode EnablePathfinding { get; set; } = new ToggleNode(true);
    public ColorNode BestPathColor { get; set; } = new(Color.Cyan);
    public RangeNode<int> BestPathFrameThickness { get; set; } = new RangeNode<int>(3, 0, 10);
    public RangeNode<int> BestPathLineThickness { get; set; } = new RangeNode<int>(4, 0, 10);
    // Scales the floor-based adjustments by one tier step per point. Relic nullification
    // is a constraint rather than a preference and is deliberately not scaled by it.
    // Prices come from the Ninja Price plugin; without it this does nothing.
    public ToggleNode UsePricesInRouting { get; set; } = new ToggleNode(false);

    // Chaos per point, and the most points a price may ever contribute. The cap keeps
    // price subordinate: at 20 it can outweigh a room type but never a tier step in
    // rewards, which is 97, nor a bad affliction at -70.
    public RangeNode<int> ChaosPerPoint { get; set; } = new RangeNode<int>(50, 1, 500);
    public RangeNode<int> PricePointCap { get; set; } = new RangeNode<int>(20, 0, 100);

    // Highest tier a price may influence. Tier 1 by default: those are the rewards that
    // decide routes, and they are rare enough per route that accumulated price cannot
    // outgrow a tier step.
    public RangeNode<int> PriceMaxTier { get; set; } = new RangeNode<int>(1, 0, 8);

    // What a deal room is worth from floor 3, in the same units as the tier weights:
    // a tier-1 reward is 100, a bad affliction is -70.
    public RangeNode<int> DealValueLateFloors { get; set; } = new RangeNode<int>(80, 0, 200);

    public RangeNode<int> ContextBiasStrength { get; set; } = new RangeNode<int>(1, 0, 5);
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
        "Records one run at a time and writes it to Logs/BetterSanctumTracker/ on End Run: sanctum-runs.csv holds a row per floor, sanctum-run-rooms.csv a row per reward slot.",
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
