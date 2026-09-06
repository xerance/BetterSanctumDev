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

namespace BetterSanctum;

public class BetterSanctumPlugin : BaseSettingsPlugin<BetterSanctumSettings>
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

    // Logs/BetterSanctumTracker under the HUD root. Not DirectoryFullName, which is not dependable
    // for source-compiled plugins, and not the shared Logs folder directly, which every
    // other plugin writes into too.
    private static string LogFilePath(string fileName)
    {
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "BetterSanctumTracker");
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
        _effectHelper = new EffectHelper(GameController, Graphics, Settings);
        _rewardTracker = new RewardTracker(LogFilePath("sanctum-rewards.csv"));
        _probe = new SanctumProbe(LogFilePath("sanctum-probe.txt"));
        _runTracker = new SanctumRunTracker(
            LogFilePath("sanctum-runs.csv"),
            LogFilePath("sanctum-run-rooms.csv"),
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

        if (Settings.RunTracking.Enable && IsForbiddenSanctumHub(area?.Area?.Id))
        {
            _runTracker.NoteHubVisit();
        }

        base.AreaChange(area);
    }

    // Resolved lazily and retried: Ninja Price registers its bridge method in its own
    // Initialise, which may run after ours. Null simply means it is not installed, and
    // prices are then left out rather than the feature failing loudly.
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

    // Read from the game's own reward categories rather than hardcoded: Divine Orbs is one
    // of them, so its chaos price is the conversion rate. Cached, since it moves slowly and
    // this is called per reward per frame. Zero means unknown, and prices stay in chaos.
    private double GetDivineChaosRate()
    {
        if (_divineChaosRate > 0 && _sinceDivineRateStopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            return _divineChaosRate;
        }

        var lookup = ResolvePriceLookup();
        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        if (lookup == null || categories == null)
        {
            return 0;
        }

        _sinceDivineRateStopwatch.Restart();
        foreach (var category in categories)
        {
            if (category.BaseType?.BaseName != "Divine Orb")
            {
                continue;
            }

            try
            {
                _divineChaosRate = lookup(category.BaseType);
            }
            catch (Exception)
            {
                _divineChaosRate = 0;
            }

            break;
        }

        return _divineChaosRate;
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

    // A unit price only. Reward quantity is not exposed anywhere in room data, so this
    // says what one of them is worth, not what the room pays out.
    private string DescribeRewardPrice(SanctumDeferredRewardCategory reward, int assignedTier)
    {
        // Same gate as routing: a price on a tier you rated low is clutter, not information
        if (!Settings.MapDisplay.ShowRewardPrices || assignedTier > Settings.Routing.PriceMaxTier)
        {
            return "";
        }

        var lookup = ResolvePriceLookup();
        if (lookup == null || reward.BaseType == null)
        {
            return "";
        }

        try
        {
            var chaos = lookup(reward.BaseType);
            return $" ({FormatPrice(chaos)})";
        }
        catch (Exception)
        {
            return "";
        }
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

        var floor = BetterSanctumSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
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
            Floor = BetterSanctumSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix),
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
                        Quantity = BetterSanctumSettings.GetRewardQuantity(reward.CurrencyName, order, floor.Floor),
                        Tier = Settings.GetCurrencyTier(reward.CurrencyName, order),
                    });
                }

                floor.Rooms[FloorObservation.Key(layerIndex, roomIndex)] = observation;
            }
        }

        return floor;
    }

    // The price lookup is by base item type, and the tracker only ever holds a currency
    // name, so this bridges the two through the game's own reward categories - the same
    // table the offer window pricing matches against. Null when Ninja Price is absent,
    // which makes the tracker fall back to the tiers you assigned.
    private Func<string, double> ResolveUnitPriceByName()
    {
        var lookup = ResolvePriceLookup();
        var categories = RemoteMemoryObject.pTheGame?.Files?.SanctumDeferredRewardCategories?.EntriesList;
        if (lookup == null || categories == null)
        {
            return null;
        }

        return currencyName =>
        {
            if (string.IsNullOrEmpty(currencyName))
            {
                return 0;
            }

            foreach (var category in categories)
            {
                if (category?.BaseType == null || category.CurrencyName != currencyName)
                {
                    continue;
                }

                try
                {
                    return lookup(category.BaseType);
                }
                catch (Exception)
                {
                    return 0;
                }
            }

            return 0;
        };
    }

    // Shown in the hub only, which is where a run both starts and ends, and where nothing
    // else on screen is competing for attention.
    private void DrawRunTrackerWindow()
    {
        if (!Settings.RunTracking.Enable || !IsForbiddenSanctumHub(GameController?.Area?.CurrentArea?.Area?.Id))
        {
            return;
        }

        ImGui.Begin("Sanctum Run Tracker");
        if (_runTracker.IsRunning)
        {
            var run = _runTracker.Current;
            ImGui.Text($"Run {run.RunId}");
            ImGui.Text($"Started {run.Started:HH:mm:ss}, {(DateTime.Now - run.Started).TotalMinutes:0} min");
            ImGui.Text($"Floors seen: {string.Join(", ", run.Floors.Keys.OrderBy(x => x))}");
            ImGui.Text($"Rooms recorded: {run.Floors.Values.Sum(x => x.Rooms.Count)}");
            ImGui.Text($"Hub visits: {run.HubVisits}");

            if (ImGui.Button("End Run (write CSV)"))
            {
                var floors = _runTracker.EndRun(ResolveUnitPriceByName());
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
            ImGui.Text("No run in progress.");
            if (ImGui.Button("Start Run"))
            {
                _runTracker.StartRun();
            }
        }

        if (_runTracker.LastError is { Length: > 0 } error)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), error);
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
            var trackedFloor = BetterSanctumSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);
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
        if (Settings.RunTracking.Enable && _runTracker.IsRunning)
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
                        room.GetRoomsWithOrder().Select(x => Settings.GetCurrencyTier(x.room.CurrencyName, x.order)).ToList(),
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
            var floor = BetterSanctumSettings.GetFloorForRoomPrefix(_lastKnownFloorPrefix);

            var routeValue = new Dictionary<(int, int), (int[] Counts, int Next)>();
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
                    int[] nextCounts = null;
                    foreach (var connection in floorWindow.FloorData.RoomLayout[layerIndex][roomIndex])
                    {
                        if (!routeValue.TryGetValue((layerIndex + 1, connection), out var candidate))
                        {
                            continue;
                        }

                        if (nextCounts == null || CompareRoutes(candidate.Counts, nextCounts) > 0)
                        {
                            next = connection;
                            nextCounts = candidate.Counts;
                        }
                    }

                    // Nothing onward exists, so this room leads nowhere
                    if (next < 0)
                    {
                        continue;
                    }

                    var total = new int[RouteValueSize];
                    for (var tier = 0; tier < RouteValueSize; tier++)
                    {
                        total[tier] = own[tier] + nextCounts[tier];
                    }

                    routeValue[(layerIndex, roomIndex)] = (total, next);
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
            int[] routeCounts = null;
            if (startLayer < roomsByLayer.Count)
            {
                foreach (var roomIndex in startCandidates)
                {
                    if (!routeValue.TryGetValue((startLayer, roomIndex), out var candidate))
                    {
                        continue;
                    }

                    if (routeCounts == null || CompareRoutes(candidate.Counts, routeCounts) > 0)
                    {
                        routeRoom = roomIndex;
                        routeCounts = candidate.Counts;
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
                var textSize = DrawTextWithBackground(fightRoomId ?? "??", lineLocation, GetRoomColor(fightRoomId), Settings.MapDisplay.BackgroundColor);
                lineLocation.Y += textSize.Y;
                var rewardRoomId = room.Data.RewardRoom?.RoomType?.Id;
                textSize = DrawTextWithBackground($"->{rewardRoomId ?? "??"}", lineLocation, GetRoomColor(rewardRoomId), Settings.MapDisplay.BackgroundColor);
                lineLocation.Y += textSize.Y;

                if (room.GetRoomsWithOrder() is { Count: > 0 } rewards)
                {
                    textSize = DrawTextWithBackground("\nRewards:", lineLocation, Settings.MapDisplay.TextColor, Settings.MapDisplay.BackgroundColor);
                    lineLocation.Y += textSize.Y;
                    foreach (var reward in rewards)
                    {
                        var currencyName = reward.room.CurrencyName;
                        var tier = Settings.GetCurrencyTier(currencyName, reward.order);
                        if (tier <= Settings.HideCurrencyBelowTier)
                        {
                            textSize = DrawTextWithBackground(currencyName + DescribeRewardPrice(reward.room, tier), lineLocation, GetTierColor(tier), Settings.MapDisplay.BackgroundColor);
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

    // Bonuses shift a value towards the good end rather than adding points, so they stay
    // meaningful under tier comparison. They never reach 0, which is yours to assign, and
    // never improve something already at or below neutral.
    private int AdjustCurrencyValue(int value, int floor)
    {
        if (value is BetterSanctumSettings.PrioritizeValue or BetterSanctumSettings.BlockValue ||
            value >= BetterSanctumSettings.NeutralValue)
        {
            return value;
        }

        var shift = 0;
        if (floor >= 3)
        {
            // Later floors roll higher reward tiers
            shift += Settings.Routing.ContextBiasStrength.Value;
        }

        return Math.Max(value - shift, 1);
    }

    // Folds the run type and floor into the room type's value. A relic that makes a room
    // type pointless flattens it to neutral; floor rules nudge it by the bias strength.
    // Neither ever overrides an explicit 0 or 8 - those are your decisions, not context.
    private int AdjustRoomValue(int value, string roomTypeId, int floor)
    {
        if (value is BetterSanctumSettings.PrioritizeValue or BetterSanctumSettings.BlockValue)
        {
            return value;
        }

        var runType = Settings.RunType;
        if (runType == BetterSanctumSettings.RunTypeHourOfDivinity && roomTypeId == "BoonFountain" ||
            runType == BetterSanctumSettings.RunTypeGildedChalice && roomTypeId == "Fountain")
        {
            // No boons to gain, or no resolve to recover: the room has nothing to offer.
            // CurseFountain is deliberately untouched - it stays bad on its own merits.
            return BetterSanctumSettings.NeutralValue;
        }

        var bias = Settings.Routing.ContextBiasStrength.Value;
        if (bias == 0)
        {
            return value;
        }

        // Deals gate the larger rewards late, and coins matter early - but only when
        // boons are buyable, which Hour of Divinity rules out.
        // Deals are handled separately, as flat points rather than a tier shift
        var favoured = floor is >= 1 and <= 2 &&
                       runType != BetterSanctumSettings.RunTypeHourOfDivinity &&
                       roomTypeId is "Treasure" or "Merchant";

        // Lower is better on this scale, and 1 is as good as a weight gets
        return favoured ? Math.Max(value - bias, 1) : value;
    }

    // A route is counted per axis, because the same tier means very different things
    // depending on what wears it: a tier-6 affliction can end a run, a tier-6 room type
    // is an inconvenience. One shared table forced those to cost the same.
    private const int TierCount = 9;
    private const int AxisReward = 0;
    private const int AxisAffliction = 1;
    private const int AxisRoom = 2;
    private const int AxisCount = 3;
    private const int BonusIndex = AxisCount * TierCount;
    private const int RouteValueSize = BonusIndex + 1;

    private static int Slot(int axis, int tier) => axis * TierCount + tier;

    // Rewards are deliberately bimodal: tier 1 decides routes, everything below it is a
    // bonus that should never outweigh a calmer path. It takes 24 tier-2 rewards to
    // justify one bad affliction, which an eight layer floor cannot hold.
    private static readonly int[] RewardWeights = { 0, 100, 3, 1, 0, -1, -3, -10, 0 };

    // Calibrated against the trades that matter: one tier-1 reward is worth one bad
    // affliction (100 - 70) but not two (100 - 140), and a tier-7 needs three.
    private static readonly int[] AfflictionWeights = { 0, 100, 30, 10, 0, -20, -70, -250, 0 };

    // Room type is about how hard the run is, not what it pays, so it sits between the
    // two: enough to prefer a calm route, never enough to turn down a tier-1.
    private static readonly int[] RoomWeights = { 0, 20, 10, 4, 0, -4, -15, -40, 0 };

    private static readonly int[] TierWeights = BuildTierWeights();

    private static int[] BuildTierWeights()
    {
        var weights = new int[RouteValueSize];
        for (var tier = 0; tier < TierCount; tier++)
        {
            weights[Slot(AxisReward, tier)] = RewardWeights[tier];
            weights[Slot(AxisAffliction, tier)] = AfflictionWeights[tier];
            weights[Slot(AxisRoom, tier)] = RoomWeights[tier];
        }

        // Flat bonuses are already expressed in these units
        weights[BonusIndex] = 1;
        return weights;
    }

    private static int WeighTiers(int[] counts)
    {
        var total = 0;
        for (var slot = 0; slot < RouteValueSize; slot++)
        {
            total += counts[slot] * TierWeights[slot];
        }

        return total;
    }

    // Constraint tiers score nothing on any axis and are counted across all three
    private static int ConstraintCount(int[] counts, int tier)
    {
        return counts[Slot(AxisReward, tier)] + counts[Slot(AxisAffliction, tier)] + counts[Slot(AxisRoom, tier)];
    }

    // Positive when route a is preferable to route b. Must-takes outrank everything,
    // including any number of never-enter rooms standing in the way; among routes tied on
    // those, fewest never-enters wins; only then does the weighted total decide.
    private static int CompareRoutes(int[] a, int[] b)
    {
        var mustTake = ConstraintCount(a, BetterSanctumSettings.PrioritizeValue)
            .CompareTo(ConstraintCount(b, BetterSanctumSettings.PrioritizeValue));
        if (mustTake != 0)
        {
            return mustTake;
        }

        var neverEnter = ConstraintCount(b, BetterSanctumSettings.BlockValue)
            .CompareTo(ConstraintCount(a, BetterSanctumSettings.BlockValue));
        if (neverEnter != 0)
        {
            return neverEnter;
        }

        return WeighTiers(a).CompareTo(WeighTiers(b));
    }

    // How many rooms of each tier this room contributes. Currency counts its best slot
    // only, since the three offers are one reward at different timings and you take one.
    // Quantity comes from measured offer text rather than an assumption. It varies by
    // currency and slot and not by floor, so no floor term appears here.
    private int PricePointsFor(SanctumDeferredRewardCategory reward, int order, int floor, int value)
    {
        // Gated on the tier you assigned rather than the floor-adjusted one. The floor 3
        // bonus promotes mid currency a tier, which would drag chaos into the priced band
        // on exactly the floors that matter - and chaos is where assuming a quantity of
        // one is most wrong, arriving in stacks of ten. The four currencies this is meant
        // for only spawn from floor 3 anyway, so nothing is lost by ignoring the bonus.
        //
        // The cap bounds a single room, not a whole route, so letting every tier
        // contribute would let a path stacked with middling currency out-score one
        // holding a genuine tier 1.
        if (!Settings.Routing.UsePricesInRouting || value > Settings.Routing.PriceMaxTier)
        {
            return 0;
        }

        var lookup = ResolvePriceLookup();
        if (lookup == null || reward?.BaseType == null)
        {
            return 0;
        }

        try
        {
            var quantity = BetterSanctumSettings.GetRewardQuantity(reward.CurrencyName, order, floor);
            var chaos = lookup(reward.BaseType) * quantity;
            if (chaos <= 0)
            {
                return 0;
            }

            return (int)Math.Min(chaos / Settings.Routing.ChaosPerPoint.Value, Settings.Routing.PricePointCap.Value);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // How many rooms of each tier this room contributes, kept per axis. Currency counts
    // its best slot only, since the three offers are one reward at different timings and
    // you take one.
    private int[] EvaluateRoom(SanctumRoomElement room, int floor)
    {
        var counts = new int[RouteValueSize];

        // The third slot is the end-of-sanctum deferral. It only pays double from floor 4;
        // before that it is an ordinary offer.
        var thirdSlotMultiplier = floor >= 4 ? 2 : 1;

        // Chosen on what the slot is worth with its multiplier applied, so a doubled
        // tier-2 does not displace a tier-1 you could take immediately.
        var bestSlotValue = -1;
        var bestSlotWorth = 0;
        var bestSlotMultiplier = 1;
        var bestSlotPricePoints = 0;
        foreach (var (reward, order) in room.GetRoomsWithOrder())
        {
            var assignedValue = Settings.GetCurrencyTier(reward.CurrencyName, order);
            var value = AdjustCurrencyValue(assignedValue, floor);
            if (value == BetterSanctumSettings.PrioritizeValue)
            {
                counts[Slot(AxisReward, BetterSanctumSettings.PrioritizeValue)]++;
                continue;
            }

            // A currency you never want is a reason to skip the offer, not the room
            if (value == BetterSanctumSettings.BlockValue)
            {
                continue;
            }

            var multiplier = order == 2 ? thirdSlotMultiplier : 1;
            var pricePoints = PricePointsFor(reward, order, floor, assignedValue);
            var worth = RewardWeights[value] * multiplier + pricePoints;
            if (bestSlotValue < 0 || worth > bestSlotWorth)
            {
                bestSlotValue = value;
                bestSlotWorth = worth;
                bestSlotMultiplier = multiplier;
                bestSlotPricePoints = pricePoints;
            }
        }

        if (bestSlotValue >= 0)
        {
            // Counting it twice is what doubles its weight
            counts[Slot(AxisReward, bestSlotValue)] += bestSlotMultiplier;

            // Capped, so price orders currencies you rated alike without ever
            // reordering the tiers themselves
            counts[BonusIndex] += bestSlotPricePoints;
        }

        foreach (var roomTypeId in new[] { room.Data.FightRoom?.RoomType?.Id, room.Data.RewardRoom?.RoomType?.Id })
        {
            if (roomTypeId == null)
            {
                continue;
            }

            counts[Slot(AxisRoom, AdjustRoomValue(Settings.GetRoomTier(roomTypeId), roomTypeId, floor))]++;

            // From floor 3 a deal is effectively a reward, but an unknown one, so it is
            // worth less than a tier-1 you can read off the map. It clears a low reward
            // and a good room comfortably, and roughly breaks even against a bad
            // affliction - which is where the judgement call actually sits.
            if (roomTypeId == "Deal" && floor >= 3)
            {
                counts[BonusIndex] += Settings.Routing.DealValueLateFloors.Value;
            }
        }

        if (room.Data.RoomEffect?.ReadableName is { } effectName)
        {
            counts[Slot(AxisAffliction, Settings.GetAfflictionTier(effectName))]++;
        }

        return counts;
    }


    private Color GetAfflictionColor(string effectName) => GetTierColor(Settings.GetAfflictionTier(effectName));
    private Color GetRoomColor(string fightRoomId) => GetTierColor(Settings.GetRoomTier(fightRoomId));

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
