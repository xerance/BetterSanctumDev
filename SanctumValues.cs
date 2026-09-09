using System;

namespace BetterSanctumDev;

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
    private const double StepsPerAnchor = 5.0;

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
