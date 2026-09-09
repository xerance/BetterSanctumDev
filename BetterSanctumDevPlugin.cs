using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.Sanctum;
using ExileCore.PoEMemory.FilesInMemory.Sanctum;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Models;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BetterSanctumDev;

public class BetterSanctumDevPlugin : BaseSettingsPlugin<BetterSanctumDevSettings>
{
    private readonly Stopwatch _sinceLastReloadStopwatch = Stopwatch.StartNew();
    private Random rndColor = new Random();
    private bool _debugDumpPending = true;
    // Remembered from the floor map: the reward window can be open when the map is not,
    // and the area name follows the room you stand in rather than the floor.
    private string _lastKnownFloorPrefix;
    // Panels only, without the room tooltip. While isolating, the hovered room's own text
    // is drawn against this: the tooltip sits on top of the room you are pointing at, so
    // testing against it would hide exactly the information the hover asked for.
    private List<RectangleF> _panelObstructions = new List<RectangleF>();
    private List<RectangleF> _obstructions = new List<RectangleF>();
    private List<RectangleF> _activeObstructions;
    private EffectHelper _effectHelper;
    private RewardTracker _rewardTracker;
    private SanctumProbe _probe;
    private string _lastBuffProbeArea;
    private SanctumRunTracker _runTracker;
    private Func<BaseItemType, double> _currencyPrice;
    private readonly Stopwatch _sincePriceLookupStopwatch = Stopwatch.StartNew();
    private double _divineChaosRate;
    private readonly Stopwatch _sinceDivineRateStopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _priceByCurrency = new Dictionary<string, double>();
    private readonly Stopwatch _sincePriceCacheStopwatch = Stopwatch.StartNew();

    // Logs/BetterSanctumDev under the HUD root. Not DirectoryFullName, which is not dependable
    // for source-compiled plugins, and not the shared Logs folder directly, which every
    // other plugin writes into too.
    private static string LogFilePath(string fileName)
    {
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "BetterSanctumDev");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception)
        {
            // Fall back to the HUD root if the folder cannot be created
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        return Path.Combine(directory, fileName);
    }

    public override bool Initialise()
    {
        // Lets a settings hint quote the chaos figure a percentage comes to. The market
        // rate rather than DivineChaos, so the hint can tell a real price from the fallback,
        // and the reason alongside it so it can say what is missing when there is none.
        Settings.DivineChaosProvider = GetDivineChaosRate;
        Settings.DivinePriceStatusProvider = DivinePriceUnavailableReason;
        _effectHelper = new EffectHelper(GameController, Graphics, Settings);
        _rewardTracker = new RewardTracker(LogFilePath("sanctum-rewards.csv"));
        _probe = new SanctumProbe(LogFilePath("sanctum-probe.txt"));
        _runTracker = new SanctumRunTracker(
            LogFilePath("sanctum-runs.csv"),
            LogFilePath("sanctum-run-rooms.csv"),
            LogFilePath("sanctum-deals.csv"),
            LogFilePath("sanctum-run-currency.csv"),
            LogFilePath("run-state.json"));
        // Picks a run back up after a HUD restart part way through one
        _runTracker.Load();
        return base.Initialise();
    }

    // The hub is a static zone reached from the map device, so it never shows up in the
    // room dump, which only fires while a floor map is open. This is the only place its
    // name can be caught.
    public override void AreaChange(AreaInstance area)
    {
        if (Settings.Debug.ProbeSanctumState)
        {
            _probe.LogAreaChange(area, GameController?.IngameState?.IngameUi?.SanctumFloorWindow);
        }

        if (Settings.RunTracking.TrackRuns && IsForbiddenSanctumHub(area?.Area?.Id))
        {
            _runTracker.NoteHubVisit();
        }

        base.AreaChange(area);
    }

    // Asked for by bridge name rather than by plugin: Ninja Price and Get-Chaos-Value both
    // register this one, and either will do. Resolved lazily and retried, since whichever
    // is installed registers it in its own Initialise, which may run after ours. Null
    // simply means neither is there, and prices are then left out rather than the feature
    // failing loudly.
    private Func<BaseItemType, double> ResolvePriceLookup()
    {
        if (_currencyPrice != null || _sincePriceLookupStopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            return _currencyPrice;
        }

        _sincePriceLookupStopwatch.Restart();
        try
        {
            _currencyPrice = GameController.PluginBridge.GetMethod<Func<BaseItemType, double>>("NinjaPrice.GetBaseItemTypeValue");
        }
        catch (Exception)
        {
            _currencyPrice = null;
        }

        return _currencyPrice;
    }

    // The reward categories are keyed by the plural name a reward reports, so this is the
    // key every other price lookup here uses.
    private const string DivineCurrencyName = "Divine Orbs";

    // Read from the game's own reward categories rather than hardcoded: Divine Orbs is one
    // of them, so its chaos price is the conversion rate. Cached, since it moves slowly and
    // this is called per reward per frame. Zero means unknown, and prices stay in chaos.
    //
    // Found down the same path as every other reward, by CurrencyName. Scanning for a
    // BaseType named "Divine Orb" instead matched nothing, so the rate read as zero and
    // every anchor quietly fell back while ordinary reward prices went on working.
    private double GetDivineChaosRate()
    {
        if (_divineChaosRate > 0 && _sinceDivineRateStopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            return _divineChaosRate;
        }

        var baseType = FindDivineBaseType();
        if (baseType == null)
        {
            return 0;
        }

        _sinceDivineRateStopwatch.Restart();
        _divineChaosRate = PriceOf(baseType);
        return _divineChaosRate;
    }

    // By reward name first, since that is the key the table is built on, and by base item
    // name second in case a league renames the category out from under it.
    private static BaseItemType FindDivineBaseType()
    {
        if (FindCategoryBaseType(DivineCurrencyName) is { } byCurrencyName)
        {
            return byCurrencyName;
        }

        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        return categories?.FirstOrDefault(x => x?.BaseType?.BaseName == "Divine Orb")?.BaseType;
    }

    // Why there is no divine price, for the settings hint to say which of the three
    // things is missing rather than blaming the price plugin for all of them. Null when
    // there is nothing wrong.
    private string DivinePriceUnavailableReason()
    {
        if (ResolvePriceLookup() == null)
        {
            return "nothing is answering the NinjaPrice.GetBaseItemTypeValue bridge";
        }

        if (RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList == null)
        {
            return "the game's Sanctum reward table has not loaded yet";
        }

        if (FindDivineBaseType() == null)
        {
            return "the Sanctum reward table has no Divine Orbs entry to price";
        }

        return GetDivineChaosRate() > 0
            ? null
            : "the price plugin returned no price for a Divine Orb";
    }

    private string FormatPrice(double chaos)
    {
        if (Settings.MapDisplay.ShowPricesInDivine)
        {
            var rate = GetDivineChaosRate();
            if (rate > 0)
            {
                return $"{chaos / rate:0.##}d";
            }
        }

        return chaos < 10 ? $"{chaos:0.#}c" : $"{chaos:0}c";
    }

    // Priced by name through the game's own reward category table rather than off the
    // BaseType hanging on the room's reward. The reward window has always priced correctly
    // and takes its BaseType from that table; the floor map priced nothing at all and took
    // its BaseType from room data. That was the only difference between them, and it left
    // every reward on the map unpriced - which also flattened routing, since two tier-1
    // rewards with no price between them tie exactly and the route falls to room types.
    //
    // Cached by currency name: this is called for every reward of every room every frame,
    // and each miss costs a lookup across the whole table plus a bridge call.
    private double UnitPriceForCurrency(string currencyName)
    {
        if (string.IsNullOrEmpty(currencyName))
        {
            return 0;
        }

        if (_sincePriceCacheStopwatch.Elapsed > TimeSpan.FromMinutes(1))
        {
            _priceByCurrency.Clear();
            _sincePriceCacheStopwatch.Restart();
        }

        if (_priceByCurrency.TryGetValue(currencyName, out var cached))
        {
            return cached;
        }

        var chaos = PriceOf(FindCategoryBaseType(currencyName));

        // Only a real price is remembered. Caching a zero was pinning whatever went wrong
        // in the first frame for a whole minute, and the reward tables are not always
        // loaded by the time the floor map first opens - Render already reloads them for
        // the room list on the same grounds.
        if (chaos > 0)
        {
            _priceByCurrency[currencyName] = chaos;
        }

        return chaos;
    }

    // Both sources are tried before giving up: the BaseType the room's own reward carries,
    // and the one on the matching entry of the game's reward category table. The reward
    // window prices from the table and works; the floor map priced from room data and did
    // not, and neither is obviously the wrong answer, so this takes whichever answers.
    private double UnitPriceFor(SanctumDeferredRewardCategory reward)
    {
        var currencyName = reward?.CurrencyName;
        if (string.IsNullOrEmpty(currencyName))
        {
            return 0;
        }

        if (_priceByCurrency.TryGetValue(currencyName, out var cached))
        {
            return cached;
        }

        var chaos = PriceOf(reward.BaseType);
        if (chaos <= 0)
        {
            chaos = PriceOf(FindCategoryBaseType(currencyName));
        }

        if (chaos > 0)
        {
            _priceByCurrency[currencyName] = chaos;
        }

        return chaos;
    }

    private double PriceOf(BaseItemType baseType)
    {
        var lookup = ResolvePriceLookup();
        if (lookup == null || baseType == null)
        {
            return 0;
        }

        try
        {
            return lookup(baseType);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static BaseItemType FindCategoryBaseType(string currencyName)
    {
        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        if (categories == null || string.IsNullOrEmpty(currencyName))
        {
            return null;
        }

        foreach (var category in categories)
        {
            if (category?.BaseType != null && category.CurrencyName == currencyName)
            {
                return category.BaseType;
            }
        }

        return null;
    }

    // What the room pays out, not what one of them is worth. The quantity is the measured
    // figure for this currency in this slot - single-item rewards double in the last slot
    // on floor 4 - which is the same number routing has always scored on, so the map now
    // agrees with the route instead of showing a unit price beside it.
    //
    // Quantity is measured rather than read: nothing in room data exposes it.
    private string DescribeRewardPrice(SanctumDeferredRewardCategory reward, int order)
    {
        // The caller decides whether the reward is shown at all
        if (!Settings.MapDisplay.ShowRewardPrices)
        {
            return "";
        }

        // The override, where there is one, so the number on the map is the number the
        // route is scoring. Reading the market price here instead would show one figure
        // and route on another.
        if (!Settings.TryGetCurrencyOverride(reward?.CurrencyName, out var chaos))
        {
            chaos = UnitPriceFor(reward);
        }

        if (chaos <= 0)
        {
            return "";
        }

        var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
        var quantity = BetterSanctumDevSettings.GetRewardQuantity(reward?.CurrencyName, order, floor);

        // Always the count, then what that many come to. Showing it only where it was not
        // one left the same currency reading two ways down a floor, and a doubled last
        // slot looked like a price that had moved rather than a reward that had doubled.
        return $" ({quantity}x = {FormatPrice(chaos * quantity)})";
    }

    // Returns the size whether or not it draws, so a suppressed line still advances the
    // layout and the rest of the block stays where it belongs. Tested per line because a
    // room's text runs well below its own box and can reach a tooltip the box does not.
    private Vector2 DrawTextWithBackground(string text, Vector2 position, Color color, Color backgroundColor)
    {
        var textSize = Graphics.MeasureText(text);
        if (IsObstructed(_activeObstructions ?? _obstructions, new RectangleF(position.X, position.Y, textSize.X, textSize.Y)))
        {
            return textSize;
        }

        Graphics.DrawBox(position, textSize + position, backgroundColor);
        Graphics.DrawText(text, position, color);
        return textSize;
    }

    // The overlay already gave way to a room tooltip. Panels the game opens over the map
    // are the same problem, so they are collected here and treated identically.
    private List<RectangleF> CollectPanelObstructions()
    {
        var obstructions = new List<RectangleF>();
        if (!Settings.MapDisplay.HideUnderGameUi)
        {
            return obstructions;
        }

        var ui = GameController.IngameState.IngameUi;
        // UIHover is deliberately absent: it is the room under the cursor as often as it
        // is a panel, and blanking the room you are pointing at helps nobody.
        foreach (var panel in new[] { ui.OpenLeftPanel, ui.OpenRightPanel, ui.ChatBox })
        {
            if (panel is not { IsVisible: true })
            {
                continue;
            }

            var rect = panel.GetClientRectCache;
            if (rect.Width > 0 && rect.Height > 0)
            {
                obstructions.Add(rect);
            }
        }

        return obstructions;
    }

    private static bool IsObstructed(List<RectangleF> obstructions, RectangleF rect)
    {
        foreach (var obstruction in obstructions)
        {
            if (obstruction.Intersects(rect))
            {
                return true;
            }
        }

        return false;
    }

    // The window moves between two child paths depending on the room, so both are tried
    private Element GetOfferWindow()
    {
        var rewardWindow = GameController.IngameState.IngameUi.SanctumRewardWindow;
        if (!rewardWindow.IsVisible)
        {
            return null;
        }

        var offerWindow = rewardWindow.GetChildFromIndices(new[] { 0, 1, 0, 1 });
        if (offerWindow is { IsVisible: true })
        {
            return offerWindow;
        }

        offerWindow = rewardWindow.GetChildFromIndices(new[] { 0, 1, 0, 2 });
        return offerWindow is { IsVisible: true } ? offerWindow : null;
    }

    // Reward quantity is not in room data - every member but CurrencyName reads absent -
    // so the game's own tooltip is the only place it appears while the map is open.
    private static void CollectText(Element element, List<string> into, int depth)
    {
        if (element == null || depth > 6)
        {
            return;
        }

        var text = element.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            into.Add(text.Trim());
        }

        foreach (var child in element.Children)
        {
            CollectText(child, into, depth + 1);
        }
    }

    private void TrackHoveredTooltip(SanctumRoomElement hoveredRoom, int floor)
    {
        var texts = new List<string>();
        CollectText(hoveredRoom.Tooltip, texts, 0);
        if (texts.Count == 0)
        {
            return;
        }

        var joined = string.Join(" | ", texts).Replace(";", ",").Replace("\n", " ");
        foreach (var (reward, order) in hoveredRoom.GetRoomsWithOrder())
        {
            _rewardTracker.Add("tooltip", floor, _lastKnownFloorPrefix, "", "", order.ToString(),
                reward.CurrencyName ?? "", joined);
        }
    }

    // Offer text carries the quantity too. Logged verbatim rather than parsed, so the
    // real format can be read off actual data first.
    private void TrackOfferWindow()
    {
        var offerWindow = GetOfferWindow();
        if (offerWindow == null)
        {
            return;
        }

        var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
        foreach (var offer in offerWindow.Children)
        {
            var text = offer.Children.Count > 1 ? offer.Children[1].Text : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _rewardTracker.Add("offer", floor, _lastKnownFloorPrefix, "", "", offer.IndexInParent.ToString(), "", text.Replace(";", ",").Replace("\n", " "));
            }
        }
    }

    // The offer window gives text, not a reward object, so the base item has to be found
    // by name. Every deferred reward category carries its BaseType, and BaseName is the
    // singular item name the offer text is built from, so matching on that is exact
    // rather than a guess at pluralisation.
    private void DrawOfferPrices()
    {
        if (!Settings.MapDisplay.ShowRewardPrices)
        {
            return;
        }

        var offerWindow = GetOfferWindow();
        var lookup = ResolvePriceLookup();
        if (offerWindow == null || lookup == null)
        {
            return;
        }

        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        if (categories == null)
        {
            return;
        }

        foreach (var offer in offerWindow.Children)
        {
            var text = offer.Children.Count > 1 ? offer.Children[1].Text : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Both names are tried: BaseName is singular ("Orb of Fusing") and matches an
            // offer of one, while CurrencyName is plural ("Orbs of Fusing") and matches a
            // stack. Neither alone covers both, because the plural s sits in the middle.
            //
            // The longest match wins, or "Receive 1x Volatile Vaal Orb" would be priced as
            // a Vaal Orb depending on which category came first.
            SanctumDeferredRewardCategory matched = null;
            var matchedLength = 0;
            foreach (var category in categories)
            {
                if (category?.BaseType == null)
                {
                    continue;
                }

                foreach (var name in new[] { category.BaseType.BaseName, category.CurrencyName })
                {
                    if (string.IsNullOrEmpty(name) ||
                        name.Length <= matchedLength ||
                        !text.Contains(name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    matched = category;
                    matchedLength = name.Length;
                }
            }

            if (matched == null)
            {
                continue;
            }

            double chaos;
            try
            {
                chaos = lookup(matched.BaseType);
            }
            catch (Exception)
            {
                continue;
            }

            var quantity = 1;
            var match = Regex.Match(text, @"\b(\d+)\s*x\b", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed > 0)
            {
                quantity = parsed;
            }

            // Every offer is priced, however cheap. A blank row reads as a broken lookup,
            // where "0.9c" reads as the answer it is.
            var rect = offer.GetClientRect();
            Graphics.DrawText(FormatPrice(chaos * quantity), new Vector2(rect.Left + 6, rect.Top + 6), Settings.MapDisplay.TextColor);
        }
    }

    private void PreventLastOffer()
    {
        var sanctumOfferWindow = GetOfferWindow();
        if (sanctumOfferWindow == null)
        {
            return;
        }

        var dupOffer = sanctumOfferWindow.Children.Where(x => Settings.CurrencyDuplicate.Any(y => x.Children[1].Text.Contains(y)));
        var noDupOffer = sanctumOfferWindow.Children.Where(x => Settings.CurrencyDuplicate.Any(y => !x.Children[1].Text.Contains(y)));
        var entitiesByType = GameController.EntityListWrapper.ValidEntitiesByType;
        var floorFinalChest = entitiesByType.TryGetValue(EntityType.Chest, out var chests)
            ? chests
            : Enumerable.Empty<Entity>();

        foreach (var offer in dupOffer)
        {
            Graphics.DrawFrame(offer.GetClientRect(), RandomUtil.NextColor(rndColor), 6);
        }

        foreach (var offer in noDupOffer.Where(x => !dupOffer.Contains(x)))
        {
            // The end-of-sanctum slot is never worth taking on a duplicate run, and on the
            // last floors the end-of-floor slot is not either. Keyed on the floor's room
            // prefix, since the area name tracks the room you stand in, not the floor.
            var crossOut = offer.IndexInParent == 2 ||
                           _lastKnownFloorPrefix == "Crypt" && offer.IndexInParent is 1 or 2 ||
                           _lastKnownFloorPrefix == "Nave" && offer.IndexInParent is 1 or 2 &&
                           floorFinalChest.FirstOrDefault(x => x.Metadata.Contains("FloorFinalRewardChest")) != null;
            if (!crossOut)
            {
                continue;
            }

            var rect = offer.Children[1].Parent.GetClientRect();
            Graphics.DrawLine(rect.TopLeft.ToVector2Num(), rect.BottomRight.ToVector2Num(), 4, Color.Red);
            Graphics.DrawLine(rect.TopRight.ToVector2Num(), rect.BottomLeft.ToVector2Num(), 4, Color.Red);
            Graphics.DrawFrame(rect, Color.Red, 4);
        }
    }

    // Sampled when the floor map opens rather than on area change: the player entity and
    // its buffs are not necessarily loaded the moment a zone changes, and the map is
    // opened once per room anyway, which is exactly the rate an affliction needs watching
    // at. Keyed on the instance id, so re-entering the same floor still samples again.
    private void ProbeBuffs()
    {
        var floorWindow = GameController?.IngameState?.IngameUi?.SanctumFloorWindow;
        if (floorWindow is not { IsVisible: true })
        {
            return;
        }

        var areaKey = $"{GameController.Area?.CurrentArea?.Area?.RawName}/{GameController.Area?.CurrentArea?.InstanceId}";
        if (areaKey == _lastBuffProbeArea)
        {
            return;
        }

        _lastBuffProbeArea = areaKey;
        try
        {
            var buffs = GameController.Player?.GetComponent<Buffs>()?.BuffsList;
            _probe.LogBuffs(buffs?.Select(x => x.DisplayName is { Length: > 0 } display && display != x.Name
                ? $"{x.Name} ({display})"
                : x.Name));
        }
        catch (Exception e)
        {
            LogError($"[BetterSanctum] could not read buffs: {e.Message}", 10);
        }
    }

    // The hub and the floor entrances are all SanctumFoyer areas; only the suffix tells
    // them apart. The floor ones are numbered - SanctumFoyer_3_3 is the way in to floor 3
    // - while the hub takes the name of the map it was opened from, SanctumFoyer_Fellshrine
    // among them. Keying on the shape rather than on that name is what stops this breaking
    // in the next map, and on a client in another language.
    private static bool IsForbiddenSanctumHub(string areaId)
    {
        return areaId != null &&
               areaId.StartsWith("SanctumFoyer_", StringComparison.Ordinal) &&
               !Regex.IsMatch(areaId, @"^SanctumFoyer_\d+_\d+$");
    }

    // Everything the tracker needs off one map opening. Partial by nature - rooms reveal a
    // few layers ahead - so this is a sighting to be merged, never the floor itself.
    private FloorObservation CaptureFloor(SanctumFloorWindow floorWindow, List<List<SanctumRoomElement>> roomsByLayer)
    {
        var floor = new FloorObservation
        {
            Floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix),
            Prefix = _lastKnownFloorPrefix,
            LayerCount = roomsByLayer.Count,
        };

        if (floor.Floor <= 0)
        {
            return null;
        }

        try
        {
            if (floorWindow.FloorData?.RoomChoices is IEnumerable rawChoices)
            {
                floor.Choices = rawChoices.Cast<object>().Select(Convert.ToInt32).ToList();
            }
        }
        catch (Exception)
        {
            // A floor with no choices yet reads as empty, which is also the honest answer
        }

        for (var layerIndex = 0; layerIndex < roomsByLayer.Count; layerIndex++)
        {
            var roomLayer = roomsByLayer[layerIndex];
            for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
            {
                var room = roomLayer[roomIndex];
                var observation = new RoomObservation
                {
                    Layer = layerIndex,
                    Room = roomIndex,
                    FightRoomId = room.Data?.FightRoom?.RoomType?.Id,
                    RewardRoomId = room.Data?.RewardRoom?.RoomType?.Id,
                    Affliction = room.Data?.RoomEffect?.ReadableName,
                };

                try
                {
                    var connections = floorWindow.FloorData?.RoomLayout;
                    if (connections != null && layerIndex < connections.Length && roomIndex < connections[layerIndex].Length)
                    {
                        observation.Connections = connections[layerIndex][roomIndex].Select(x => (int)x).ToList();
                    }
                }
                catch (Exception)
                {
                    // An unrevealed room has no layout yet; the route solve treats an
                    // empty connection list as "anything onward" rather than a dead end.
                }

                foreach (var (reward, order) in room.GetRoomsWithOrder())
                {
                    observation.Slots.Add(new SlotObservation
                    {
                        Slot = order,
                        Currency = reward.CurrencyName,
                        Quantity = BetterSanctumDevSettings.GetRewardQuantity(reward.CurrencyName, order, floor.Floor),
                        Tier = RewardBand(reward, order, floor.Floor),
                    });
                }

                floor.Rooms[FloorObservation.Key(layerIndex, roomIndex)] = observation;
            }
        }

        return floor;
    }

    // The price lookup is by base item type, and the tracker only ever holds a currency
    // name, so this bridges the two through the game's own reward categories - the same
    // table the offer window pricing matches against. Null when there is no price plugin,
    // which makes the tracker fall back to the value bands recorded with each slot.
    // Offer text names the currency in either its singular or plural form - "Receive 1x
    // Volatile Vaal Orb" against "Receive 10x Chaos Orbs" - so both are tried and the
    // longest match wins, or a volatile vaal reads as a vaal. The same rule the offer
    // window pricing already uses.
    private static string MatchOfferCurrency(string text)
    {
        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        if (categories == null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string matched = null;
        var matchedLength = 0;
        foreach (var category in categories)
        {
            if (category?.BaseType == null)
            {
                continue;
            }

            foreach (var name in new[] { category.BaseType.BaseName, category.CurrencyName })
            {
                if (string.IsNullOrEmpty(name) ||
                    name.Length <= matchedLength ||
                    !text.Contains(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                matched = category.CurrencyName;
                matchedLength = name.Length;
            }
        }

        return matched;
    }

    // A Deal reads Reward1/2/3 as null on the map - its contents only exist once you are
    // standing in it - so the reward window is the only place a deal can be read at all.
    // Captured for every room rather than only deals, since it also puts the window's own
    // quantities beside the measured ones.
    //
    // Attributed to the room you are in, which the map cannot say because the map is shut
    // while this window is up. RoomChoices records the room index taken in each completed
    // layer, so its last entry is where you are standing. Read behind a sanity check: just
    // after a zone change FloorData resolves to a stale struct reading zero gold and zero
    // resolve, and a choice list taken from that means nothing.
    private void CaptureRoomOffers()
    {
        if (!Settings.RunTracking.TrackRuns || !_runTracker.IsRunning)
        {
            return;
        }

        var offerWindow = GetOfferWindow();
        if (offerWindow == null)
        {
            return;
        }

        var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
        if (floor <= 0)
        {
            return;
        }

        try
        {
            var floorData = GameController?.IngameState?.IngameUi?.SanctumFloorWindow?.FloorData;
            if (floorData == null)
            {
                return;
            }

            // Deliberately not gated on resolve. Entering a room leaves FloorData resolving
            // to a stale struct until the map is next opened, and the reward window opens
            // inside exactly that gap - so a resolve check rejects every offer there is.
            // RoomChoices reads correctly on that struct even while the numbers beside it
            // do not, and the tracker validates the room against the floor it already
            // mapped, which catches a bad read without relying on resolve at all.
            var choices = floorData.RoomChoices is IEnumerable rawChoices
                ? rawChoices.Cast<object>().Select(Convert.ToInt32).ToList()
                : new List<int>();
            if (choices.Count == 0)
            {
                return;
            }

            var offers = new List<OfferObservation>();
            foreach (var offer in offerWindow.Children)
            {
                var text = offer.Children.Count > 1 ? offer.Children[1].Text : null;
                if (string.IsNullOrWhiteSpace(text) || offer.IndexInParent is not { } slot)
                {
                    continue;
                }

                var currency = MatchOfferCurrency(text);
                var quantity = 1;
                var match = Regex.Match(text, @"\b(\d+)\s*x\b", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed > 0)
                {
                    quantity = parsed;
                }

                offers.Add(new OfferObservation
                {
                    Slot = slot,
                    Text = text.Trim(),
                    Currency = currency,
                    Quantity = quantity,
                    // Priced from the window text rather than the map, since a deal has no map entry
                    Tier = SanctumValues.ValueBand(UnitPriceForCurrency(currency) * quantity, DivineChaos()),
                });
            }

            var layer = choices.Count - 1;
            var accepted = _runTracker.NoteOffers(floor, _lastKnownFloorPrefix, layer, choices[^1], offers);

            // Silence here is what let this go unnoticed for a week of runs, so the probe
            // says whether each window was taken and, when it was not, why.
            if (Settings.Debug.ProbeSanctumState)
            {
                _probe.LogOfferCapture(floor, layer, choices[^1], offers.Count, accepted);
            }
        }
        catch (Exception e)
        {
            LogError($"[BetterSanctumDev] could not read the reward window: {e.Message}", 10);
        }
    }

    private Func<string, double> ResolveUnitPriceByName()
    {
        // Null when there is no price source at all, which is what tells the tracker to
        // fall back to the recorded bands rather than scoring every currency zero.
        return ResolvePriceLookup() == null ? null : UnitPriceForCurrency;
    }

    // The same rule PreventLastOffer draws on screen, so the tracked haul matches what the
    // overlay told you to take. Null on an ordinary run, where every slot is fair game.
    //
    // On a duplicate run the end-of-Sanctum slot is never worth taking, and on floors 3
    // and 4 the end-of-floor slot is not either - the relic duplicates the final reward,
    // so anything deferred past it is currency you walk away from. A Divine Orb or a
    // Mirror is the exception, being the reward the run exists to duplicate.
    //
    // Floor 4 is taken on the floor alone. The overlay also checks for the final chest
    // having spawned, and a finished run cannot be asked about an entity that is long gone.
    private Func<int, int, string, bool> ResolveTakeableSlotRule()
    {
        if (!Settings.DuplicateRun)
        {
            return null;
        }

        return (floor, slot, currency) =>
        {
            if (currency != null && Settings.CurrencyDuplicate.Any(currency.Contains))
            {
                return true;
            }

            return floor >= 3 ? slot is not (1 or 2) : slot != 2;
        };
    }

    // Shown in the hub only, which is where a run both starts and ends, and where nothing
    // else on screen is competing for attention.
    private void DrawRunTrackerWindow()
    {
        if (!Settings.RunTracking.TrackRuns || !IsForbiddenSanctumHub(GameController?.Area?.CurrentArea?.Area?.Id))
        {
            return;
        }

        ImGui.Begin("Sanctum Run Tracker");
        if (_runTracker.IsRunning)
        {
            var run = _runTracker.Current;
            ImGui.TextUnformatted($"Run {run.RunId}");
            ImGui.TextUnformatted($"Started {run.Started:HH:mm:ss}, {(DateTime.Now - run.Started).TotalMinutes:0} min");
            ImGui.TextUnformatted($"Floors seen: {string.Join(", ", run.Floors.Keys.OrderBy(x => x))}");
            ImGui.TextUnformatted($"Rooms recorded: {run.Floors.Values.Sum(x => x.Rooms.Count)}");
            ImGui.TextUnformatted($"Hub visits: {run.HubVisits}");

            if (ImGui.Button("End Run (write CSV)"))
            {
                var floors = _runTracker.EndRun(ResolveUnitPriceByName(), ResolveTakeableSlotRule());
                if (floors < 0)
                {
                    LogError($"[BetterSanctum] could not write the run: {_runTracker.LastError}", 30);
                }
                else
                {
                    LogMessage($"[BetterSanctum] wrote {floors} floor rows to sanctum-runs.csv", 30);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Discard"))
            {
                _runTracker.AbandonRun();
            }
        }
        else
        {
            ImGui.TextUnformatted("No run in progress.");
            if (ImGui.Button("Start Run"))
            {
                _runTracker.StartRun();
            }
        }

        if (_runTracker.LastError is { Length: > 0 } error)
        {
            // Not TextColored: it formats, and an exception message is the one string
            // here that really can arrive with a percent sign in it.
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1, 0.4f, 0.4f, 1));
            ImGui.TextUnformatted(error);
            ImGui.PopStyleColor();
        }

        ImGui.End();
    }

    public override void Render()
    {
        // Ahead of every early return below, since the point of the probe is the state
        // outside a floor - in the hub, where the floor map is not open at all.
        if (Settings.Debug.ProbeSanctumState)
        {
            _probe.LogState(GameController?.IngameState?.IngameUi?.SanctumFloorWindow,
                GameController?.Area?.CurrentArea?.Area?.RawName);
            ProbeBuffs();
        }

        DrawRunTrackerWindow();

        // Ahead of the floor-map gate below, since this is read while the map is shut and
        // you are standing in the room
        CaptureRoomOffers();

        if (Settings.DuplicateRun)
        {
            PreventLastOffer();
        }

        if (Settings.Debug.TrackRewards)
        {
            TrackOfferWindow();
        }

        DrawOfferPrices();
        
        // Only inside a Sanctum, and before the floor-map return below, since these draw
        // in the room rather than on the map
        if (GameController.Area.CurrentArea.Area.RawName.StartsWith("Sanctum"))
        {
            _effectHelper.DrawEffects();
        }

        var floorWindow = GameController.IngameState.IngameUi.SanctumFloorWindow;
        if (!floorWindow.IsVisible)
        {
            _debugDumpPending = true;
            return;
        }

        if (!GameController.Files.SanctumRooms.EntriesList.Any() && _sinceLastReloadStopwatch.Elapsed > TimeSpan.FromSeconds(5))
        {
            GameController.Files.LoadFiles();
            _sinceLastReloadStopwatch.Restart();
        }

        var hoveredRoom = floorWindow.Rooms.FirstOrDefault(x =>
            ImGui.IsMouseHoveringRect(x.GetClientRectCache.TopLeft.ToVector2Num(), x.GetClientRectCache.BottomRight.ToVector2Num(), false));
        var tooltipRect = RectangleF.Empty;
        if (hoveredRoom != null)
        {
            tooltipRect = hoveredRoom.Tooltip.GetClientRectCache;
        }

        _panelObstructions = CollectPanelObstructions();
        _obstructions = new List<RectangleF>(_panelObstructions);
        if (tooltipRect.Width > 0 && tooltipRect.Height > 0)
        {
            _obstructions.Add(tooltipRect);
        }

        _activeObstructions = _obstructions;

        var tierMap = new Dictionary<(int, int), (List<int> CurrencyTier, int? RoomTier, int? AfflictionTier)>();
        var roomsByLayer = floorWindow.RoomsByLayer;

        foreach (var probeLayer in roomsByLayer)
        {
            foreach (var probeRoom in probeLayer)
            {
                var probeId = probeRoom.Data?.FightRoom?.Id ?? probeRoom.Data?.RewardRoom?.Id;
                if (probeId != null)
                {
                    _lastKnownFloorPrefix = probeId.Split('_')[0];
                    break;
                }
            }
        }

        if (Settings.Debug.TrackRewards)
        {
            var trackedFloor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
            if (hoveredRoom != null)
            {
                TrackHoveredTooltip(hoveredRoom, trackedFloor);
            }

            for (var layerIndex = 0; layerIndex < roomsByLayer.Count; layerIndex++)
            {
                var roomLayer = roomsByLayer[layerIndex];
                for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
                {
                    foreach (var (reward, order) in roomLayer[roomIndex].GetRoomsWithOrder())
                    {
                        // Anything the reward object exposes beyond the name, in case one
                        // of these turns out to carry the quantity
                        var detail = string.Join(" ", DebugRewardMemberNames
                            .Where(name => name != "CurrencyName")
                            .Select(name => DescribeMember(reward, name))
                            .Where(x => !x.EndsWith("<absent>") && !x.EndsWith("null")));
                        _rewardTracker.Add("map", trackedFloor, _lastKnownFloorPrefix,
                            layerIndex.ToString(), roomIndex.ToString(), order.ToString(),
                            reward.CurrencyName ?? "", detail);
                    }
                }
            }
        }

        // Only while the map is open. The probe showed FloorData resolving to a stale
        // struct otherwise - zero gold and resolve just after a zone change, and outright
        // garbage in the hub - so a read taken with the map shut is not worth merging.
        if (Settings.RunTracking.TrackRuns && _runTracker.IsRunning)
        {
            _runTracker.Merge(CaptureFloor(floorWindow, roomsByLayer));
        }

        if (Settings.Debug.DebugDumpRoomData && _debugDumpPending)
        {
            _debugDumpPending = false;
            var dumpPath = LogFilePath("room-dump.txt");
            try
            {
                // Room ids carry the floor name; the area name does not reliably
                var floorPrefix = "unknown";
                foreach (var probeLayer in roomsByLayer)
                {
                    foreach (var probeRoom in probeLayer)
                    {
                        var probeId = probeRoom.Data?.FightRoom?.Id ?? probeRoom.Data?.RewardRoom?.Id;
                        if (!string.IsNullOrEmpty(probeId))
                        {
                            floorPrefix = probeId.Split('_')[0];
                            break;
                        }
                    }

                    if (floorPrefix != "unknown")
                    {
                        break;
                    }
                }

                var lines = new List<string>
                {
                    $"{DateTime.Now:s} floorPrefix={floorPrefix} area={GameController.Area.CurrentArea.Area.RawName} layers={roomsByLayer.Count}",
                    "WINDOW " + string.Join(", ", DebugWindowMemberNames.Select(name => DescribeMember(floorWindow, name))),
                    "FLOORDATA " + string.Join(", ", DebugWindowMemberNames.Select(name => DescribeMember(floorWindow.FloorData, name))),
                };

                DumpRewardTables(lines);
                DumpPricing(lines, roomsByLayer);
                for (var layerIndex = 0; layerIndex < roomsByLayer.Count; layerIndex++)
                {
                    var roomLayer = roomsByLayer[layerIndex];
                    for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
                    {
                        var data = roomLayer[roomIndex].Data;
                        lines.Add($"L{layerIndex}R{roomIndex} " +
                                  string.Join(", ", DebugMemberNames.Select(name => DescribeMember(data, name))));
                        foreach (var (reward, order) in roomLayer[roomIndex].GetRoomsWithOrder())
                        {
                            lines.Add($"  L{layerIndex}R{roomIndex} reward{order} " +
                                      string.Join(", ", DebugRewardMemberNames.Select(name => DescribeMember(reward, name))));
                        }
                    }
                }

                File.WriteAllLines(dumpPath, lines);
                LogMessage($"[BetterSanctum] wrote {lines.Count - 1} rooms to {dumpPath}", 30);
            }
            catch (Exception e)
            {
                LogError($"[BetterSanctum] could not write {dumpPath}: {e.Message}", 30);
            }
        }

        if (Settings.MapDisplay.ConnectionLineThickness > 0)
        {
            for (var layerIndex = roomsByLayer.Count - 2; layerIndex >= 0; layerIndex--)
            {
                var roomLayer = roomsByLayer[layerIndex];
                for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
                {
                    var room = roomLayer[roomIndex];
                    (List<int> CurrencyTier, int? RoomTier, int? AfflictionTier) thisRoomData = (
                        room.GetRoomsWithOrder().Select(x => RewardBand(x.room, x.order, BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix))).ToList(),
                        room.Data.RewardRoom?.RoomType?.Id switch
                        {
                            null => null,
                            var o => Settings.GetRoomTier(o)
                        },
                        (room.Data.RewardRoom?.RoomType?.Id, room.Data.RoomEffect?.ReadableName) switch
                        {
                            (not null, null) => 1,
                            (null, _) => null,
                            (not null, { } o) => Settings.GetAfflictionTier(o)
                        }
                    );
                    var connections = floorWindow.FloorData.RoomLayout[layerIndex][roomIndex];
                    var connectedRoomData = connections.Select(x => tierMap.GetValueOrDefault((layerIndex + 1, x)))
                        .Where(x => x != default).ToList();
                    if (connectedRoomData.Any())
                    {
                        var aggregateConnectionData = connectedRoomData
                            .Aggregate((current, connectionData) => (
                                current.CurrencyTier.Union(connectionData.CurrencyTier).ToList(),
                                (current.RoomTier, connectionData.RoomTier) switch
                                {
                                    ({ } tier1, { } tier2) => Math.Min(tier1, tier2),
                                    var (tier1, tier2) => tier1 ?? tier2
                                },
                                (current.AfflictionTier, connectionData.AfflictionTier) switch
                                {
                                    ({ } tier1, { } tier2) => Math.Min(tier1, tier2),
                                    var (tier1, tier2) => tier1 ?? tier2
                                }));
                        thisRoomData = (
                            thisRoomData.CurrencyTier.Union(aggregateConnectionData.CurrencyTier).ToList(),
                            (thisRoomData.RoomTier, aggregateConnectionData.RoomTier) switch
                            {
                                ({ } tier1, { } tier2) => Math.Min(tier1, tier2),
                                var (tier1, tier2) => tier1 ?? tier2
                            },
                            (thisRoomData.AfflictionTier, aggregateConnectionData.AfflictionTier) switch
                            {
                                ({ } tier1, { } tier2) => Math.Max(tier1, tier2),
                                var (tier1, tier2) => tier1 ?? tier2
                            });
                    }

                    tierMap[(layerIndex, roomIndex)] = thisRoomData;
                }
            }
        }


        // Route planning. Sanctum floors are layered and you enter exactly one room per
        // layer, so every route holds the same number of rooms and their tier counts are
        // directly comparable. Routes are ranked by comparing those counts tier by tier
        // rather than by summing points, so two tier-1 rewards beat one tier-1 however
        // much middling filler sits behind it.
        var bestRoute = new HashSet<(int, int)>();
        var bestRouteOrder = new List<(int Layer, int Room)>();
        if (Settings.Routing.EnablePathfinding && Settings.Routing.BestPathFrameThickness > 0 && roomsByLayer.Count > 0)
        {
            var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);

            var routeValue = new Dictionary<(int, int), (RouteValue Value, int Next)>();
            for (var layerIndex = roomsByLayer.Count - 1; layerIndex >= 0; layerIndex--)
            {
                var roomLayer = roomsByLayer[layerIndex];
                for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
                {
                    var own = EvaluateRoom(roomLayer[roomIndex], floor);
                    if (layerIndex == roomsByLayer.Count - 1)
                    {
                        routeValue[(layerIndex, roomIndex)] = (own, -1);
                        continue;
                    }

                    var next = -1;
                    RouteValue? nextValue = null;
                    foreach (var connection in floorWindow.FloorData.RoomLayout[layerIndex][roomIndex])
                    {
                        if (!routeValue.TryGetValue((layerIndex + 1, connection), out var candidate))
                        {
                            continue;
                        }

                        if (nextValue == null || CompareRoutes(candidate.Value, nextValue.Value) > 0)
                        {
                            next = connection;
                            nextValue = candidate.Value;
                        }
                    }

                    // Nothing onward exists, so this room leads nowhere
                    if (next < 0)
                    {
                        continue;
                    }

                    routeValue[(layerIndex, roomIndex)] = (own + nextValue.Value, next);
                }
            }

            // Anchor the route to where you actually stand. FloorData.RoomChoices holds
            // the room index taken in each completed layer, so its count is the layer you
            // are choosing from next and its last entry is your current room. Empty at the
            // start of a floor, where every room in layer 0 is a candidate.
            var roomChoices = floorWindow.FloorData.RoomChoices is IEnumerable rawChoices
                ? rawChoices.Cast<object>().Select(x => Convert.ToInt32(x)).ToList()
                : new List<int>();
            var startLayer = roomChoices.Count;
            IEnumerable<int> startCandidates;
            if (startLayer == 0)
            {
                startCandidates = Enumerable.Range(0, roomsByLayer[0].Count);
            }
            else
            {
                // Only rooms connected to the current one can be entered next
                startCandidates = floorWindow.FloorData.RoomLayout[startLayer - 1][roomChoices[startLayer - 1]]
                    .Select(x => (int)x);
            }

            var routeRoom = -1;
            RouteValue? routeBest = null;
            if (startLayer < roomsByLayer.Count)
            {
                foreach (var roomIndex in startCandidates)
                {
                    if (!routeValue.TryGetValue((startLayer, roomIndex), out var candidate))
                    {
                        continue;
                    }

                    if (routeBest == null || CompareRoutes(candidate.Value, routeBest.Value) > 0)
                    {
                        routeRoom = roomIndex;
                        routeBest = candidate.Value;
                    }
                }
            }

            // routeRoom goes negative at the last layer, ending the walk
            for (var layerIndex = startLayer; routeRoom >= 0 && layerIndex < roomsByLayer.Count; layerIndex++)
            {
                bestRoute.Add((layerIndex, routeRoom));
                bestRouteOrder.Add((layerIndex, routeRoom));
                routeRoom = routeValue[(layerIndex, routeRoom)].Next;
            }
        }

        // Join the route up so it reads as a path rather than a row of separate frames
        if (Settings.Routing.BestPathLineThickness > 0)
        {
            for (var step = 1; step < bestRouteOrder.Count; step++)
            {
                var from = roomsByLayer[bestRouteOrder[step - 1].Layer][bestRouteOrder[step - 1].Room].GetClientRectCache;
                var to = roomsByLayer[bestRouteOrder[step].Layer][bestRouteOrder[step].Room].GetClientRectCache;
                if (IsObstructed(_obstructions, from) || IsObstructed(_obstructions, to))
                {
                    continue;
                }

                Graphics.DrawLine(
                    new Vector2(from.Right - 15, from.Center.Y),
                    new Vector2(to.Left + 15, to.Center.Y),
                    Settings.Routing.BestPathLineThickness.Value,
                    Settings.Routing.BestPathColor);
            }
        }

        for (var layerIndex = 0;
             layerIndex < roomsByLayer.Count;
             layerIndex++)
        {
            var roomLayer = roomsByLayer[layerIndex];
            for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
            {
                var room = roomLayer[roomIndex];
                var fightRoomId = room.Data.FightRoom?.RoomType?.Id;
                var isolating = Settings.MapDisplay.IsolateHoveredRoom && hoveredRoom != null;
                if (fightRoomId != null && Settings.MapDisplay.ConnectionLineThickness > 0 && !isolating)
                {
                    var connections = floorWindow.FloorData.RoomLayout[layerIndex][roomIndex];
                    var connectedRoomData = connections.Select(index => (index, tierMap.GetValueOrDefault((layerIndex + 1, index))))
                        .Where(x => x.Item2 != default).ToList();
                    if (connectedRoomData.Any())
                    {
                        var leftPoint = new Vector2(room.GetClientRectCache.Right - 15, room.GetClientRectCache.Center.Y);
                        foreach (var (index, (currencyTier, roomTier, afflictionTier)) in connectedRoomData)
                        {
                            var connectedRoom = roomsByLayer[layerIndex + 1][index];
                            if (connectedRoom.Data.FightRoom?.RoomType?.Id == null)
                            {
                                continue;
                            }

                            var rightPoint = new Vector2(connectedRoom.GetClientRectCache.Left + 15, connectedRoom.GetClientRectCache.Center.Y);
                            if (IsObstructed(_obstructions, new RectangleF(leftPoint.X, Math.Min(leftPoint.Y, rightPoint.Y),
                                    rightPoint.X - leftPoint.X,
                                    Math.Max(leftPoint.Y, rightPoint.Y) -
                                    Math.Min(leftPoint.Y, rightPoint.Y))))
                            {
                                continue;
                            }

                            var leftPointOffset = new Vector2(0, (rightPoint.Y - leftPoint.Y) * 0.25f);
                            var overlapOffsetVector = new Vector2(0,
                                Settings.MapDisplay.ConnectionLineThickness * (0.5f + 0.5f * (rightPoint - leftPoint).Length() / (rightPoint.X - leftPoint.X)));
                            Graphics.DrawLine(leftPoint + leftPointOffset - overlapOffsetVector,
                                rightPoint - leftPointOffset - overlapOffsetVector,
                                Settings.MapDisplay.ConnectionLineThickness,
                                currencyTier.Any() ? GetTierColor(currencyTier.Min()) : Settings.TierColors.EmptyColor);
                            Graphics.DrawLine(leftPoint + leftPointOffset,
                                rightPoint - leftPointOffset,
                                Settings.MapDisplay.ConnectionLineThickness,
                                roomTier is { } ? GetTierColor(roomTier.Value) : Settings.TierColors.EmptyColor);
                            Graphics.DrawLine(leftPoint + leftPointOffset + overlapOffsetVector,
                                rightPoint - leftPointOffset + overlapOffsetVector,
                                Settings.MapDisplay.ConnectionLineThickness,
                                afflictionTier is { } ? GetTierColor(afflictionTier.Value) : Settings.TierColors.EmptyColor);
                        }
                    }
                }

                // Hovering isolates a room: its own text stays, everything else gets out of
                // the way, since a floor of eight rooms writes more than can be read at once.
                // Compared by address: Rooms and RoomsByLayer hand back separate wrapper
                // objects for the same room, so reference equality is always false.
                var isHovered = hoveredRoom != null && room.Address == hoveredRoom.Address;
                if (isolating && !isHovered)
                {
                    continue;
                }

                // The tooltip covers the room it belongs to, so testing the hovered room
                // against it would hide the text the hover was asking for.
                _activeObstructions = isolating && isHovered ? _panelObstructions : _obstructions;
                if (IsObstructed(_activeObstructions, room.GetClientRectCache))
                {
                    continue;
                }

                if (bestRoute.Contains((layerIndex, roomIndex)))
                {
                    Graphics.DrawFrame(room.GetClientRectCache, Settings.Routing.BestPathColor, Settings.Routing.BestPathFrameThickness.Value);
                }

                var textTopLeft = room.GetClientRectCache.TopLeft.ToVector2Num();
                var lineLocation = textTopLeft;
                var rewardRoomId = room.Data.RewardRoom?.RoomType?.Id;

                // A room reads as nothing both before it is revealed and after it is
                // behind you, and neither is worth a line of text - the room's own art
                // already says which. Written as two independent lines rather than one
                // with a placeholder, so a room that knows half of itself still says so.
                if (fightRoomId != null)
                {
                    var fightSize = DrawTextWithBackground(fightRoomId, lineLocation, GetRoomColor(fightRoomId), Settings.MapDisplay.BackgroundColor);
                    lineLocation.Y += fightSize.Y;
                }

                Vector2 textSize;
                if (rewardRoomId != null)
                {
                    textSize = DrawTextWithBackground($"->{rewardRoomId}", lineLocation, GetRoomColor(rewardRoomId), Settings.MapDisplay.BackgroundColor);
                    lineLocation.Y += textSize.Y;
                }

                if (room.GetRoomsWithOrder() is { Count: > 0 } rewards)
                {
                    textSize = DrawTextWithBackground("\nRewards:", lineLocation, Settings.MapDisplay.TextColor, Settings.MapDisplay.BackgroundColor);
                    lineLocation.Y += textSize.Y;
                    foreach (var reward in rewards)
                    {
                        var currencyName = reward.room.CurrencyName;
                        var rewardFloor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
                        var rewardChaos = RewardChaos(reward.room, reward.order, rewardFloor);

                        // A display filter only - a reward too cheap to write down is
                        // still scored, since a route is worth the sum of what is on it.
                        if (rewardChaos >= HideRewardsBelow())
                        {
                            textSize = DrawTextWithBackground(currencyName + DescribeRewardPrice(reward.room, reward.order), lineLocation, GetRewardColor(rewardChaos), Settings.MapDisplay.BackgroundColor);
                            lineLocation.Y += textSize.Y;
                        }
                    }
                }

                if (room.Data.RoomEffect is { } effect)
                {
                    var text = "";
                    if (Settings.MapDisplay.ShowEffectId)
                    {
                        text += $"{effect.Id}\n";
                    }

                    var effectName = effect.ReadableName;
                    if (Settings.MapDisplay.ShowEffectName)
                    {
                        text += $"{effectName}\n";
                    }

                    if (Settings.MapDisplay.ShowEffectDescription)
                    {
                        var maxWidth = room.GetClientRectCache.Width;
                        var splitDescription = effect.Description.Split(" ").Aggregate(new List<string> { "" }, (l, i) =>
                        {
                            if (l.Last().Length > 0 && Graphics.MeasureText(l.Last() + i).X > maxWidth)
                            {
                                return l.Append(i).ToList();
                            }

                            return l.SkipLast(1).Append($"{l.Last()} {i}").ToList();
                        });
                        text += $"{string.Join("\n", splitDescription)}\n";
                    }

                    textSize = DrawTextWithBackground(text, lineLocation, GetAfflictionColor(effectName), Settings.MapDisplay.BackgroundColor);
                    lineLocation.Y += textSize.Y;
                }
            }
        }

    }

    // Read by reflection on purpose: several of these are guesses from the ExileCore
    // metadata, and a name that turns out not to exist should report itself as absent
    // rather than stop the plugin compiling.
    // Floor-window level, to find which rooms are currently choosable
    private static readonly string[] DebugWindowMemberNames =
    {
        "RoomChoices", "Rooms", "RoomData", "RoomDataArray", "RoomName", "Room",
    };

    // On the reward objects themselves, to find whether a reward quantity is readable
    private static readonly string[] DebugRewardMemberNames =
    {
        "CurrencyName", "Cost", "CostMultiplier", "CostStat", "DeferralCategory",
        "Min", "Max", "StackSize", "Amount", "Quantity", "Id", "Name",
    };

    private static readonly string[] DebugMemberNames =
    {
        "FightRoom", "RewardRoom", "RewardRooms", "RoomEffect",
        "Reward1", "Reward2", "Reward3",
        "Cost", "CostStat", "CostMultiplier", "DeferralCategory",
    };

    // Every readable property, for types whose members are not known in advance
    private static string DescribeAllMembers(object target)
    {
        if (target == null)
        {
            return "<null>";
        }

        var parts = new List<string>();
        foreach (var property in target.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                parts.Add($"{property.Name}={Describe(property.GetValue(target), 1)}");
            }
            catch (Exception e)
            {
                parts.Add($"{property.Name}=<{e.GetType().Name}>");
            }
        }

        return string.Join(", ", parts);
    }

    // The reward amount is in neither the room nor its reward category, so the game's own
    // reward tables are the remaining place it could live.
    private void DumpRewardTables(List<string> lines)
    {
        var files = RemoteMemoryObject.pTheGame?.Files;
        if (files == null)
        {
            return;
        }

        foreach (var (name, entries) in new (string, System.Collections.IEnumerable)[]
                 {
                     ("DeferredRewards", files.SanctumDeferredRewards?.EntriesList),
                     ("DeferredRewardCategories", files.SanctumDeferredRewardCategories?.EntriesList),
                 })
        {
            if (entries == null)
            {
                lines.Add($"{name} <absent>");
                continue;
            }

            var index = 0;
            foreach (var entry in entries)
            {
                lines.Add($"{name}[{index}] {DescribeAllMembers(entry)}");
                if (++index >= 500)
                {
                    lines.Add($"{name} truncated at {index}");
                    break;
                }
            }

            lines.Add($"{name} count={index}");
        }
    }

    // Why a reward on the map has no price. Two guesses at this have now been wrong, so
    // rather than a third this reports every step for each currency the floor is showing:
    // whether the price bridge resolved at all, whether the reward carries a BaseType,
    // whether the category table holds an entry of that name, and what the lookup returns
    // for each. Names are quoted, since a trailing space would explain a failed match and
    // is invisible otherwise.
    private void DumpPricing(List<string> lines, List<List<SanctumRoomElement>> roomsByLayer)
    {
        var lookup = ResolvePriceLookup();
        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        lines.Add($"PRICING bridge={(lookup == null ? "<null>" : "resolved")} " +
                  $"categories={(categories == null ? "<null>" : categories.Count.ToString())} " +
                  $"showRewardPrices={Settings.MapDisplay.ShowRewardPrices.Value} " +
                  $"roomAnchor={AnchorChaos(Settings.Routing.RoomValuePercentOfDivine.Value):0.#} " +
                  $"afflictionAnchor={AnchorChaos(Settings.Routing.AfflictionCostPercentOfDivine.Value):0.#} divine={DivineChaos():0.#}");

        var seen = new HashSet<string>();
        foreach (var roomLayer in roomsByLayer)
        {
            foreach (var room in roomLayer)
            {
                foreach (var (reward, order) in room.GetRoomsWithOrder())
                {
                    var currencyName = reward.CurrencyName;
                    if (currencyName == null || !seen.Add(currencyName))
                    {
                        continue;
                    }

                    var fromCategory = FindCategoryBaseType(currencyName);
                    lines.Add($"PRICING \"{currencyName}\" " +
                              $"rewardBaseType={(reward.BaseType == null ? "<null>" : $"\"{reward.BaseType.BaseName}\"")} " +
                              $"rewardPrice={PriceOf(reward.BaseType)} " +
                              $"categoryBaseType={(fromCategory == null ? "<no match>" : $"\"{fromCategory.BaseName}\"")} " +
                              $"categoryPrice={PriceOf(fromCategory)}");
                }
            }
        }

        // The table's own names, to read against the reward names above when a match
        // fails: the two are supposed to be the same strings.
        if (categories == null)
        {
            return;
        }

        foreach (var category in categories)
        {
            lines.Add($"PRICINGTABLE \"{category?.CurrencyName}\" base=\"{category?.BaseType?.BaseName}\" " +
                      $"price={PriceOf(category?.BaseType)}");
        }
    }

    private static string DescribeMember(object target, string name)
    {
        if (target == null)
        {
            return $"{name}=<no data>";
        }

        var member = target.GetType().GetProperty(name);
        if (member == null)
        {
            return $"{name}=<absent>";
        }

        try
        {
            return $"{name}={Describe(member.GetValue(target), 0)}";
        }
        catch (Exception e)
        {
            return $"{name}=<{e.GetType().Name}>";
        }
    }

    private static string Describe(object value, int depth)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string text)
        {
            return text;
        }

        var type = value.GetType();
        if (type.IsPrimitive || value is decimal)
        {
            return value.ToString();
        }

        if (depth > 2)
        {
            return type.Name;
        }

        if (value is IEnumerable items)
        {
            return "[" + string.Join("|", items.Cast<object>().Select(x => Describe(x, depth + 1))) + "]";
        }

        // Report every identifying member rather than the first one found. Returning
        // early on Id hid RoomType, which is the member the tiering actually keys on.
        var parts = new List<string>();
        foreach (var name in new[] { "Id", "ReadableName", "CurrencyName", "RoomType" })
        {
            try
            {
                if (type.GetProperty(name)?.GetValue(value) is { } inner)
                {
                    parts.Add($"{name}={Describe(inner, depth + 1)}");
                }
            }
            catch (Exception)
            {
                // an unreadable member tells us nothing useful, so try the next one
            }
        }

        return parts.Count > 0 ? $"{type.Name}({string.Join(" ", parts)})" : type.Name;
    }
    // What a route is worth. Must-takes and hard blocks are counted rather than priced,
    // because both are absolute: a currency you always want is taken through anything, and
    // an affliction you never want is refused however rich the room behind it. Everything
    // else is chaos, so the two halves never have to be converted into one another.
    private readonly record struct RouteValue(int MustTakes, int HardBlocks, double Chaos)
    {
        public static RouteValue operator +(RouteValue a, RouteValue b) =>
            new(a.MustTakes + b.MustTakes, a.HardBlocks + b.HardBlocks, a.Chaos + b.Chaos);
    }

    // Most must-takes first, then fewest hard blocks, then most chaos. The order is what
    // makes a must-take override a hard block: it is settled before blocks are looked at.
    private static int CompareRoutes(RouteValue a, RouteValue b)
    {
        var mustTakes = a.MustTakes.CompareTo(b.MustTakes);
        if (mustTakes != 0)
        {
            return mustTakes;
        }

        var blocks = b.HardBlocks.CompareTo(a.HardBlocks);
        if (blocks != 0)
        {
            return blocks;
        }

        return a.Chaos.CompareTo(b.Chaos);
    }

    // A divine in chaos, which every anchor is expressed against. Falls back to a figure
    // you set rather than to zero: without it the anchors collapse and rooms and
    // afflictions stop scoring against each other at all.
    private double DivineChaos()
    {
        var rate = GetDivineChaosRate();
        return rate > 0 ? rate : Settings.Routing.DivineChaosFallback.Value;
    }

    private double AnchorChaos(int percentOfDivine) => DivineChaos() * percentOfDivine / 100.0;

    // A figure you typed is absolute chaos and stays put; unset follows the divine price
    // like every other anchor, so the map does not fill with noise as the economy inflates
    // past a threshold nobody thought to revisit.
    private double HideRewardsBelow()
    {
        var configured = Settings.HideRewardsBelowChaos;
        return configured >= 0
            ? configured
            : AnchorChaos(BetterSanctumDevSettings.DefaultHidePercentOfDivine);
    }

    // What one offer pays, in chaos. Quantity is the measured figure for that slot, so the
    // last slot on floor 4 is worth double.
    //
    // An override wins over the market outright, including an override of zero, which is
    // how a currency is told to pull no route at all. Only where there is neither an
    // override nor a price does the unknown reward figure stand in - a currency the price
    // plugin has never heard of should not read as worthless.
    private double RewardChaos(SanctumDeferredRewardCategory reward, int order, int floor)
    {
        var quantity = BetterSanctumDevSettings.GetRewardQuantity(reward?.CurrencyName, order, floor);
        if (Settings.TryGetCurrencyOverride(reward?.CurrencyName, out var overridden))
        {
            return overridden * quantity;
        }

        var unit = UnitPriceFor(reward);
        return unit > 0
            ? unit * quantity
            : AnchorChaos(Settings.Routing.UnknownRewardPercentOfDivine.Value);
    }

    // The band a reward falls in, read off what it is worth. This is the tier now: there
    // is no list to rate, and the same bands colour the text on the map.
    private int RewardBand(SanctumDeferredRewardCategory reward, int order, int floor)
    {
        return SanctumValues.ValueBand(RewardChaos(reward, order, floor), DivineChaos());
    }

    // What a room's rewards are worth when the map does not list any. A deal hides its
    // rewards until you are inside it; a room the map has not revealed hides everything.
    // Neither is empty - a fountain is.
    private double UnreadRewardChaos(SanctumRoomElement room, int floor)
    {
        var fightRoomId = room.Data?.FightRoom?.RoomType?.Id;
        var rewardRoomId = room.Data?.RewardRoom?.RoomType?.Id;

        if (rewardRoomId == "Deal")
        {
            // Before floor 3 a deal is not worth what a late one is, and the unknown
            // figure is the honest stand-in rather than a second slider nobody would tune.
            return floor >= 3
                ? AnchorChaos(Settings.Routing.DealValuePercentOfDivine.Value)
                : AnchorChaos(Settings.Routing.UnknownRewardPercentOfDivine.Value);
        }

        // Nothing known about the room at all, or a reward room whose rewards are hidden
        var unrevealed = fightRoomId == null && rewardRoomId == null;
        return unrevealed || rewardRoomId == "Deferral"
            ? AnchorChaos(Settings.Routing.UnknownRewardPercentOfDivine.Value)
            : 0;
    }

    // The best slot only, since the three offers are one reward at different timings and
    // you take exactly one.
    private (double Chaos, bool MustTake) BestRewardChaos(SanctumRoomElement room, int floor)
    {
        var best = 0.0;
        var any = false;
        var mustTake = false;
        var mustTakeAt = AnchorChaos(Settings.Routing.MustTakePercentOfDivine.Value);
        foreach (var (reward, order) in room.GetRoomsWithOrder())
        {
            any = true;
            var chaos = RewardChaos(reward, order, floor);

            // A must-take is now a price rather than a rating: past this much chaos a
            // reward is worth walking through an affliction you would otherwise refuse.
            // Zero switches that off, since every reward would otherwise qualify.
            if (mustTakeAt > 0 && chaos >= mustTakeAt)
            {
                mustTake = true;
            }

            if (chaos > best)
            {
                best = chaos;
            }
        }

        return any ? (best, mustTake) : (UnreadRewardChaos(room, floor), false);
    }

    // The run type shifts a tier by a step before it is priced, rather than adding chaos,
    // so an adjustment keeps its meaning whatever the anchors are set to.
    //
    // Coins buy boons, so a shop is worth more on any floor of an ordinary run, and the
    // treasure rooms that pay them are worth more early while there is still a run left to
    // spend in. The relic runs each nullify one room type outright: no boons to gain, or
    // no resolve to recover, leaves the room with nothing to offer, which is tier 5.
    private int AdjustRoomTier(int tier, string roomTypeId, int floor)
    {
        var runType = Settings.RunType;
        if (runType == BetterSanctumDevSettings.RunTypeHourOfDivinity && roomTypeId == "BoonFountain" ||
            runType == BetterSanctumDevSettings.RunTypeGildedChalice && roomTypeId == "Fountain")
        {
            return SanctumValues.RoomNeutralTier;
        }

        if (runType == BetterSanctumDevSettings.RunTypeDefault)
        {
            return tier;
        }

        // Early only: coins are worth having while there is still a run left to spend them
        // in, and by the last floors the rooms that pay them have little left to buy.
        //
        // Hour of Divinity has no boons to buy at all, so coins are worth less throughout
        // and none of this applies. CurseFountain is never adjusted.
        var boonsAreWorthBuying = runType != BetterSanctumDevSettings.RunTypeHourOfDivinity;
        var favoured = boonsAreWorthBuying &&
                       floor <= 2 &&
                       roomTypeId is "Merchant" or "Treasure" or "TreasureMinor";

        return favoured ? Math.Max(tier - 1, 0) : tier;
    }

    // Coins stop mattering once there is little run left to spend them in, so the
    // afflictions that attack them cost a step less on the last two floors.
    //
    // A hard block is never adjusted. It is a decision that this is not walked through,
    // not a weight, and a run type is not grounds to overturn it.
    private int AdjustAfflictionTier(int tier, string effectName, int floor)
    {
        if (SanctumValues.IsHardBlock(tier) || Settings.RunType == BetterSanctumDevSettings.RunTypeDefault)
        {
            return tier;
        }

        return floor >= 3 && BetterSanctumDevSettings.AfflictionAffectsAureus(effectName)
            ? Math.Max(tier - 1, 0)
            : tier;
    }

    // What this room adds to a route, in chaos, plus the two absolutes.
    private RouteValue EvaluateRoom(SanctumRoomElement room, int floor)
    {
        var (rewardChaos, mustTake) = BestRewardChaos(room, floor);
        var chaos = rewardChaos;

        var roomAnchor = AnchorChaos(Settings.Routing.RoomValuePercentOfDivine.Value);
        foreach (var roomTypeId in new[] { room.Data?.FightRoom?.RoomType?.Id, room.Data?.RewardRoom?.RoomType?.Id })
        {
            if (roomTypeId != null)
            {
                chaos += SanctumValues.RoomValue(AdjustRoomTier(Settings.GetRoomTier(roomTypeId), roomTypeId, floor), roomAnchor);
            }
        }

        var hardBlocks = 0;
        if (room.Data?.RoomEffect?.ReadableName is { } effectName)
        {
            var tier = AdjustAfflictionTier(Settings.GetAfflictionTier(effectName), effectName, floor);
            if (SanctumValues.IsHardBlock(tier))
            {
                hardBlocks = 1;
            }
            else
            {
                chaos += SanctumValues.AfflictionCost(tier, AnchorChaos(Settings.Routing.AfflictionCostPercentOfDivine.Value));
            }
        }

        return new RouteValue(mustTake ? 1 : 0, hardBlocks, chaos);
    }


    // The three axes run on scales of different lengths now, so each is mapped onto the
    // nine colours rather than indexing them directly. Afflictions start at neutral and
    // only ever get worse, since none of them is worth having.
    // Coloured on the adjusted tier, not the assigned one, so a room the run type has
    // made better or worse reads that way on the map instead of only in the route.
    private Color GetAfflictionColor(string effectName)
    {
        var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
        var tier = AdjustAfflictionTier(Settings.GetAfflictionTier(effectName), effectName, floor);
        return GetTierColor(4 + (int)Math.Round(tier * 4.0 / SanctumValues.AfflictionTierMax));
    }

    // Rooms span most of the range but never reach 0, which means must-take and belongs
    // to rewards alone.
    private Color GetRoomColor(string roomTypeId)
    {
        var floor = BetterSanctumDevSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
        var tier = AdjustRoomTier(Settings.GetRoomTier(roomTypeId), roomTypeId, floor);
        return GetTierColor(1 + (int)Math.Round(tier * 7.0 / SanctumValues.RoomTierMax));
    }

    // Rewards are coloured by what they are worth rather than by the tier you gave them,
    // so the map reads as prices at a glance and the tier stays free to mean intent.
    private Color GetRewardColor(double chaos) => GetTierColor(SanctumValues.ValueBand(chaos, DivineChaos()));

    private ColorNode GetTierColor(int value)
    {
        return value switch
        {
            0 => Settings.TierColors.Tier0Color,
            1 => Settings.TierColors.Tier1Color,
            2 => Settings.TierColors.Tier2Color,
            3 => Settings.TierColors.Tier3Color,
            4 => Settings.TierColors.Tier4Color,
            5 => Settings.TierColors.Tier5Color,
            6 => Settings.TierColors.Tier6Color,
            7 => Settings.TierColors.Tier7Color,
            8 => Settings.TierColors.Tier8Color,
            _ => Settings.TierColors.EmptyColor,
        };
    }
}
