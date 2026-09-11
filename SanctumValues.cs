using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSanctumDev;

// What a currency is called in a spreadsheet column. The game's own names are what the
// reward table reports and what an override is keyed on, so those stay the keys; these are
// for headings only, where "Orbs of Annulment" costs more width than it earns.
//
// Anything without an entry keeps its full name, so a currency added later reads plainly
// rather than breaking.
public static class CurrencyNames
{
    private static readonly IReadOnlyDictionary<string, string> Short = new Dictionary<string, string>
    {
        ["Mirrors of Kalandra"] = "mirror",
        ["Volatile Vaal Orbs"] = "volatile",
        ["Fracturing Orbs"] = "fracturing",
        ["Divine Orbs"] = "divine",
        ["Veiled Chaos Orbs"] = "veiled chaos",
        ["Sacred Orbs"] = "sacred",
        ["Orbs of Annulment"] = "annul",
        ["Ancient Orbs"] = "ancient",
        ["Divine Vessels"] = "vessel",
        ["Chaos Orbs"] = "chaos",
        ["Chromatic Orbs"] = "chrome",
        ["Gemcutter's Prisms"] = "gcp",
        ["Orbs of Alteration"] = "alt",
        ["Orbs of Chance"] = "chance",
        ["Glassblower's Baubles"] = "bauble",
        ["Jeweller's Orbs"] = "jew",
        ["Orbs of Alchemy"] = "alch",
        ["Orbs of Fusing"] = "fuse",
        ["Orbs of Scouring"] = "scour",
        ["Cartographer's Chisels"] = "chisel",
        ["Orbs of Binding"] = "binding",
        ["Orbs of Regret"] = "regret",
        ["Blessed Orbs"] = "blessed",
        ["Vaal Orbs"] = "vaal",
        ["Orbs of Horizon"] = "horizon",
        ["Instilling Orbs"] = "instilling",
        ["Regal Orbs"] = "regal",
        ["Enkindling Orbs"] = "enkindling",
        ["Orbs of Unmaking"] = "unmaking",
        ["Awakened Sextants"] = "sextant",
        ["Stacked Decks"] = "deck",
        ["Exalted Orbs"] = "exalted",
        ["Blacksmith's Whetstones"] = "whetstone",
        ["Armourer's Scraps"] = "scrap",
        ["Orbs of Transmutation"] = "transmute",
        ["Orbs of Augmentation"] = "aug",
    };

    // Both directions, because a file written before this existed has the full names in
    // its header, and the header is what says which columns a file has.
    private static readonly IReadOnlyDictionary<string, string> Full =
        Short.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    // The reward table names a currency in the plural - "Divine Orbs" - and the game's item
    // list names the item, "Divine Orb". Written out rather than worked out, because the
    // rule has exceptions: Orbs of Horizon is an Orb of Horizons.
    private static readonly IReadOnlyDictionary<string, string> Singular = new Dictionary<string, string>
    {
        ["Mirrors of Kalandra"] = "Mirror of Kalandra",
        ["Volatile Vaal Orbs"] = "Volatile Vaal Orb",
        ["Fracturing Orbs"] = "Fracturing Orb",
        ["Divine Orbs"] = "Divine Orb",
        ["Veiled Chaos Orbs"] = "Veiled Chaos Orb",
        ["Sacred Orbs"] = "Sacred Orb",
        ["Orbs of Annulment"] = "Orb of Annulment",
        ["Ancient Orbs"] = "Ancient Orb",
        ["Divine Vessels"] = "Divine Vessel",
        ["Chaos Orbs"] = "Chaos Orb",
        ["Chromatic Orbs"] = "Chromatic Orb",
        ["Gemcutter's Prisms"] = "Gemcutter's Prism",
        ["Orbs of Alteration"] = "Orb of Alteration",
        ["Orbs of Chance"] = "Orb of Chance",
        ["Glassblower's Baubles"] = "Glassblower's Bauble",
        ["Jeweller's Orbs"] = "Jeweller's Orb",
        ["Orbs of Alchemy"] = "Orb of Alchemy",
        ["Orbs of Fusing"] = "Orb of Fusing",
        ["Orbs of Scouring"] = "Orb of Scouring",
        ["Cartographer's Chisels"] = "Cartographer's Chisel",
        ["Orbs of Binding"] = "Orb of Binding",
        ["Orbs of Regret"] = "Orb of Regret",
        ["Blessed Orbs"] = "Blessed Orb",
        ["Vaal Orbs"] = "Vaal Orb",
        ["Orbs of Horizon"] = "Orb of Horizons",
        ["Instilling Orbs"] = "Instilling Orb",
        ["Regal Orbs"] = "Regal Orb",
        ["Enkindling Orbs"] = "Enkindling Orb",
        ["Orbs of Unmaking"] = "Orb of Unmaking",
        ["Awakened Sextants"] = "Awakened Sextant",
        ["Stacked Decks"] = "Stacked Deck",
        ["Exalted Orbs"] = "Exalted Orb",
        ["Blacksmith's Whetstones"] = "Blacksmith's Whetstone",
        ["Armourer's Scraps"] = "Armourer's Scrap",
        ["Orbs of Transmutation"] = "Orb of Transmutation",
        ["Orbs of Augmentation"] = "Orb of Augmentation",
    };

    public static string ToSingular(string currency) =>
        currency != null && Singular.TryGetValue(currency, out var name) ? name : currency;

    public static string ToShort(string currency) =>
        currency != null && Short.TryGetValue(currency, out var name) ? name : currency;

    public static string ToFull(string column) =>
        column != null && Full.TryGetValue(column, out var name) ? name : column;
}

// Everything a route is scored on is chaos. Rewards are priced directly; rooms and
// afflictions are priced from an anchor expressed as a percentage of a divine, so the
// trade between a reward and the pain of reaching it holds as the economy moves rather
// than needing retuning every league.
//
// The old scales were points with no unit - a tier-1 reward was 100, a bad affliction
// -70 - and the constant converting price into those points had to keep rewards above
// room types and below afflictions at the same time, which no single value did.
public static class SanctumValues
{
    // Currency has no tier list any more. A reward's band is read straight off what it is
    // worth, because that is all a tier was ever standing in for, and rating a hundred
    // currencies by hand to say what a price already says was the bulk of the settings.
    // What is left is an override on the price of a named currency, for where you
    // disagree with the market or want something ignored.
    public const int CurrencyBandMax = 5;

    // Room: 0 is worth the anchor, 5 is worth nothing, 10 costs the anchor. Symmetric,
    // because a room type can be worth seeking as easily as worth avoiding.
    public const int RoomTierMax = 10;
    public const int RoomNeutralTier = 5;

    // Affliction: 0 costs nothing through 5 costing the anchor, and 6 is absolute. Only
    // ever a cost - no affliction is worth having - so this half is not symmetric.
    public const int AfflictionTierMax = 6;
    public const int AfflictionHardBlockTier = 6;

    // Both scales move in fifths of their anchor, so a step is the same size on each and
    // the numbers stay easy to hold in your head.
    public const double StepsPerAnchor = 5.0;

    public static double RoomValue(int tier, double anchorChaos)
    {
        return anchorChaos * (1.0 - Math.Clamp(tier, 0, RoomTierMax) / StepsPerAnchor);
    }

    // Zero for the hard block: it is removed from consideration rather than paid for, so
    // giving it a price as well would count it twice on the routes that must accept one.
    public static double AfflictionCost(int tier, double anchorChaos)
    {
        if (tier >= AfflictionHardBlockTier)
        {
            return 0;
        }

        return -anchorChaos * (Math.Clamp(tier, 0, AfflictionTierMax) / StepsPerAnchor);
    }

    public static bool IsHardBlock(int afflictionTier) => afflictionTier >= AfflictionHardBlockTier;

    // One set of bands does both jobs: it colours the reward on the map and it is the
    // reward's tier. Thresholds are in divine, so they hold their meaning as prices move.
    //
    //   0  5d+      1  1d+      2  0.5d+      3  0.3d+      4  0.1d+      5  the rest
    private static readonly double[] BandsInDivine = { 5.0, 1.0, 0.5, 0.3, 0.1 };

    public static int ValueBand(double chaos, double divineChaos)
    {
        if (divineChaos <= 0)
        {
            return CurrencyBandMax;
        }

        var divine = chaos / divineChaos;
        for (var band = 0; band < BandsInDivine.Length; band++)
        {
            if (divine >= BandsInDivine[band])
            {
                return band;
            }
        }

        return CurrencyBandMax;
    }
}
