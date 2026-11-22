using ASToolkit.Engine;
using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Routes;

internal sealed class WalkWorld
{
    private const float LinkReach = 24;
    private const float AreaMargin = 0.96f;
    private const float MinimumAreaReach = 6;

    private readonly MapResourceBuilder _map;
    private readonly LevelLogic _logic;
    private readonly SpritePartReader _parts;

    public WalkWorld(MapResourceBuilder map, LevelLogic logic, SpritePartReader parts)
    {
        _map = map;
        _logic = logic;
        _parts = parts;
    }

    public HashSet<int> Pressed { get; } = [];

    public static float GetAreaReach(float radius) => Math.Max(radius * AreaMargin, MinimumAreaReach);

    public int CountOpen(int timeLine) => _logic.Openings.Values.Count(opensAt => opensAt <= timeLine) + Pressed.Count;

    public WalkGrid BuildGrid(GameObject walker, int timeLine, IReadOnlySet<int> pressed)
    {
        var grid = new WalkGrid((int)MathF.Ceiling(_map.Size.Width / WalkGrid.CellSize), (int)MathF.Ceiling(_map.Size.Height / WalkGrid.CellSize), walker.StepHeight,
            walker.DropHeight);
        var standing = new List<SpritePart>();
        foreach (var sprite in _map.Sprites.Where(sprite => sprite.Id != KnownObject.SequelPlayer))
        {
            bool isOpen = _logic.Openings.TryGetValue(sprite.Handle, out int opensAt) && opensAt <= timeLine || pressed.Contains(sprite.Handle);
            foreach (var part in _parts.GetParts(sprite))
            {
                if (!isOpen)
                {
                    standing.Add(part);
                }

                if (part.File is null)
                {
                    continue;
                }

                if (IsFloor(part))
                {
                    var file = part.File.Value;
                    grid.Cover(part.Position, new Vector2(file.Size.Width, file.Size.Height) * part.Scale.AsVector2() * 0.5f);
                }
                else if (!isOpen && RaisesGround(part.Object) && !IsDestructible(part.Object))
                {
                    grid.Stamp(_parts.GetHeightPoints(part), part.Position, part.Scale, part.Object.SpriteClass == SpriteClass.BuiltTerrain);
                }
            }
        }

        var walkerHalf = walker.Size.AsVector2() * walker.Scale.AsVector2() * 0.5f;
        grid.SetGround(walkerHalf);
        foreach (var part in standing.Where(part => part.Object.Blocks(walker)))
        {
            var size = part.Object.Size * part.Object.Scale * part.Scale;
            grid.Block(part.Position, size * 0.5f, size.Z, walkerHalf, walker.Size.Z * walker.Scale.Z);
        }

        foreach (var marker in _logic.Markers.Where(marker => marker.Target is not null))
        {
            grid.Link(marker.Position, marker.Radius > 0 ? GetAreaReach(marker.Radius) : LinkReach, marker.Target!.Value);
        }

        return grid;
    }

    private static bool RaisesGround(GameObject obj) => obj.IsSolid || obj.Properties.HasFlag(ObjectProperties.BuildSizeToGridZ);

    private static bool IsFloor(SpritePart part)
    {
        var file = part.File!.Value;
        bool isSurface = file.Type.HasFlag(VidType.Hardware) && !file.Type.HasFlag(VidType.Surface);
        return isSurface || (!RaisesGround(part.Object) && part.Object.SpriteClass == SpriteClass.BuiltTerrain && !(file.Type.HasFlag(VidType.Surface) && file.Type.HasFlag(VidType.Blended)));
    }

    private static bool IsDestructible(GameObject obj) => obj is { IsHittable: true, MaxHp: > 0 };
}
