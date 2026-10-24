using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Routes;

// The level a walk happens in, with the sprites its scripts have opened so far
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

    public HashSet<uint> Pressed { get; } = [];

    // Takes the cells inside a trigger area by the distance the scripts measure
    public static float GetAreaReach(float radius) => Math.Max(radius * AreaMargin, MinimumAreaReach);

    public int CountOpen(int timeLine) => _logic.Openings.Values.Count(opensAt => opensAt <= timeLine) + Pressed.Count;

    public WalkGrid BuildGrid(GameObject walker, int timeLine, IReadOnlySet<uint> pressed)
    {
        var grid = new WalkGrid((int)Math.Ceiling(_map.Size.Width / WalkGrid.CellSize), (int)Math.Ceiling(_map.Size.Height / WalkGrid.CellSize), walker.StepHeight,
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
                    grid.Cover(part.Position, new Vector2(file.Width * part.Scale.X, file.Height * part.Scale.Y) * 0.5f);
                }
                else if (!isOpen && RaisesGround(part.Object) && !IsDestructible(part.Object))
                {
                    grid.Stamp(_parts.GetHeightPoints(part), part.Position, part.Scale, part.Object.SpriteClass == SpriteClass.Scenery);
                }
            }
        }

        var walkerHalf = new Vector2(walker.Size.X * walker.Scale.X, walker.Size.Y * walker.Scale.Y) * 0.5f;
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

    private static bool RaisesGround(GameObject obj) => obj.IsSolid || obj.Flags.HasFlag(ObjectFlags.Outline);

    private static bool IsFloor(SpritePart part)
    {
        var file = part.File!.Value;
        bool isSurface = file.Flags.HasFlag(VidFlags.Surface) && !file.Flags.HasFlag(VidFlags.Cadr);
        return isSurface || (!RaisesGround(part.Object) && part.Object.SpriteClass == SpriteClass.Scenery && !(file.Flags.HasFlag(VidFlags.Cadr) && file.Flags.HasFlag(VidFlags.Blended)));
    }

    private static bool IsDestructible(GameObject obj) => obj is { IsHittable: true, MaxHp: > 0 };
}
