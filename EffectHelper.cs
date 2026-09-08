using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace BetterSanctum;

// In-room overlay, as opposed to everything else here which draws on the floor map:
// marks where guards spawn from and where hazards are about to land.
public class EffectHelper
{
    private readonly GameController _gameController;
    private readonly Graphics _graphics;
    private readonly BetterSanctumDevSettings _settings;

    public EffectHelper(GameController gameController, Graphics graphics, BetterSanctumDevSettings settings)
    {
        _gameController = gameController;
        _graphics = graphics;
        _settings = settings;
    }

    private void DrawHazard(string text, Vector2 screenPosition, Vector3 worldPosition, float radius, int segments, Color color)
    {
        var textSize = ImGui.CalcTextSize(text);
        var textPosition = screenPosition with { Y = screenPosition.Y - textSize.Y / 2 };
        _graphics.DrawTextWithBackground(text, textPosition, color, FontAlign.Center, Color.Black with { A = 200 });
        _graphics.DrawFilledCircleInWorld(worldPosition, radius, color with { A = 150 }, segments);
    }

    private IEnumerable<Entity> GetEntities(EntityType type)
    {
        var entities = _gameController?.EntityListWrapper?.ValidEntitiesByType;
        return entities != null && entities.TryGetValue(type, out var list) ? list : Enumerable.Empty<Entity>();
    }

    private void DrawSpawners()
    {
        foreach (var entity in GetEntities(EntityType.Terrain))
        {
            // Off-screen spawners are noise; the ones that matter are the ones near you
            if (entity.DistancePlayer >= _settings.InRoom.EffectDrawDistance)
            {
                continue;
            }

            if (!entity.Metadata.Contains("/Sanctum/Objects/Spawners/SanctumSpawner") &&
                !entity.Metadata.Contains("/Sanctum/Objects/SanctumSpawner"))
            {
                continue;
            }

            var position = RemoteMemoryObject.pTheGame.IngameState.Camera.WorldToScreen(entity.PosNum);
            entity.TryGetComponent<StateMachine>(out var stateComponent);
            var isActive = stateComponent?.States.FirstOrDefault(x => x.Name == "active") is { Value: 1 };

            if (isActive)
            {
                DrawHazard("Spawner", position, entity.PosNum, 60.0f, 4, _settings.InRoom.ActiveSpawnerColor);
            }
            else
            {
                DrawHazard(" + ", position, entity.PosNum, 20.0f, 4, _settings.InRoom.DormantSpawnerColor);
            }
        }
    }

    private void DrawHazards()
    {
        foreach (var entity in GetEntities(EntityType.Effect))
        {
            if (entity.DistancePlayer >= _settings.InRoom.EffectDrawDistance ||
                !entity.Metadata.Contains("/Effects/Effect") ||
                !entity.TryGetComponent<Animated>(out var animated) ||
                animated?.BaseAnimatedObjectEntity?.Metadata is not { } metadata)
            {
                continue;
            }

            var position = RemoteMemoryObject.pTheGame.IngameState.Camera.WorldToScreen(entity.PosNum);
            var color = _settings.InRoom.HazardColor;

            if (metadata.Contains("League_Sanctum/hazards/hazard_meteor"))
            {
                DrawHazard("Meteor", position, entity.PosNum, 140.0f, 30, color);
            }
            else if (metadata.Contains("League_Sanctum/hazards/totem_holy_beam_impact"))
            {
                DrawHazard("ZAP!", position, entity.PosNum, 40.0f, 30, color);
            }
            else if (metadata.Contains("League_Necropolis/LyciaBoss/ao/lightning_strike_scourge"))
            {
                // Only while the strike is still winding up, so it reads as a warning
                if (entity.TryGetComponent<AnimationController>(out var animation) &&
                    animation.AnimationProgress is > 0.0f and < 0.3f)
                {
                    DrawHazard("Dodge", position, entity.PosNum, 100.0f, 60, color);
                }
            }
        }
    }

    public void DrawEffects()
    {
        if (_settings.InRoom.ShowGuardSpawners)
        {
            DrawSpawners();
        }

        if (_settings.InRoom.ShowHazards)
        {
            DrawHazards();
        }
    }
}
