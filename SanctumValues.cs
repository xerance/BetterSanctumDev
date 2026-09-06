using System;

namespace BetterSanctum;

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
    // Currency: 0 must-take, 4 ignored. The tiers between are what you are after in
    // descending order of intent, and by default they all count their full price - the
    // multipliers exist to bias that, not to express value, which the price already does.
    public const int CurrencyMustTake = 0;
    public const int CurrencyIgnore = 4;
    public const int CurrencyTierMax = 4;

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

    // Reward text is coloured by what it is worth rather than by the tier you assigned,
    // so the map reads as prices at a glance and the tiers stay free to mean intent.
    // Thresholds are in divine.
    private static readonly double[] ColourBandsInDivine = { 5.0, 1.0, 0.5, 0.3, 0.1 };

    public static int ColourBandForValue(double chaos, double divineChaos)
    {
        if (divineChaos <= 0)
        {
            return ColourBandsInDivine.Length;
        }

        var divine = chaos / divineChaos;
        for (var band = 0; band < ColourBandsInDivine.Length; band++)
        {
            if (divine >= ColourBandsInDivine[band])
            {
                return band;
            }
        }

        return ColourBandsInDivine.Length;
    }
}
