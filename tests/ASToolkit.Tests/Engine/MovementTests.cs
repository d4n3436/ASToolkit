using System.Drawing;
using ASToolkit.Core.Engine;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MovementTests
{
    [Fact]
    public void Remove_AmbushWall_RefreshesWalkingLocally()
    {
        var lattice = new Lattice(10, 7);
        var middle = lattice.ToWorld(22, 30.5f);
        var terrain = new MovementMap();
        var sprites = CreateSprites(terrain);
        var wall = Place(sprites, Body(new Vector3(10, 10, 40)), new Vector3(middle.X, middle.Y, 0), KnownObject.SpawnWallSlash);
        var grid = new RouteGrid
        {
            Lattice = lattice, Cells = Enumerable.Range(20, 4).Select(u => new Cell(u, 30)).ToHashSet(), Edges = [], FenceEdges = [], BlockedCells = []
        };
        var field = new WalkField(grid, false, terrain, sprites, Body(new Vector3(2, 2, 10)));
        var first = lattice.GetCenter(new Cell(21, 30));
        var second = lattice.GetCenter(new Cell(22, 30));
        var from = new Vector2(first.X, first.Y);
        var to = new Vector2(second.X, second.Y);
        Assert.False(field.CanMove(from.AsVector3(), to));
        sprites.DeleteFromHash(wall);
        field.RefreshMovement(new Vector2(middle.X, middle.Y), new Vector2(5));
        Assert.True(field.CanMove(from.AsVector3(), to));
    }

    [Fact]
    public void FindStart_PlayerOffTheFloorGrid_ReturnsAReachableCell()
    {
        var grid = new RouteGrid
        {
            Lattice = new Lattice(10, 7), Cells = Enumerable.Range(0, 4).SelectMany(u => Enumerable.Range(0, 4).Select(v => new Cell(u, v))).ToHashSet(),
            Edges = [], FenceEdges = [], BlockedCells = []
        };
        var terrain = new MovementMap();
        var field = new WalkField(grid, false, terrain, CreateSprites(terrain), Body(new Vector3(24, 18, 40), spriteClass: SpriteClass.Man));
        var outside = new Vector2(-120, 0);
        int start = field.FindStart(outside.AsVector3());

        Assert.True(start >= 0);
        Assert.True(field.CanMove(outside.AsVector3(), field.GetCenter(start)));
    }

    [Fact]
    public void Half_IgnoresTheVisualScale()
    {
        var obj = Body(new Vector3(40, 28, 73), scale: new Vector3(3, 2, 4));
        var definition = new SpriteDefinition(KnownObject.Monster1, obj, default, 0);
        Assert.Equal(new Vector2(20, 14), definition.Half);
    }

    [Fact]
    public void CanStand_StaticBox_UsesFullHalfExtentsMasksAndHeight()
    {
        var walker = Body(new Vector3(24, 18, 40), dropHeight: 100);
        var field = CreateField(walker, (Body(new Vector3(40, 28, 73)), new Vector3(50, 50, 0)));
        Assert.True(field.CanStand(walker, new Vector2(18, 50).AsVector3()));
        Assert.False(field.CanStand(walker, new Vector2(18.1f, 50).AsVector3()));
        Assert.True(field.CanStand(Body(walker.Size, mask: 2), new Vector2(50, 50).AsVector3()));
        Assert.True(field.CanStand(walker, new Vector3(new Vector2(50, 50), 74)));
        Assert.False(field.CanStand(walker, new Vector3(new Vector2(50, 50), 73)));
    }

    [Fact]
    public void GetSupportHeight_PlayerFootprint_SamplesWholeCellStepsFromItsLeftEdge()
    {
        var movement = new MovementMap();
        var player = Body(new Vector3(24, 24, 40), spriteClass: SpriteClass.Man);
        movement.RaiseTemporary(new Vector3(24, 16, 40));
        Assert.Equal(0, movement.GetSupportHeight(player, new Vector2(16, 20)));
        Assert.True(movement.CanStandOnGround(player, new Vector2(16, 20).AsVector3()));
        var step = new Vector2(17, 20);
        Assert.Equal(40, movement.GetSupportHeight(player, step));
        Assert.False(movement.CanStandOnGround(player, step.AsVector3()));
        Assert.True(movement.CanStandOnGround(player, new Vector3(step, 30)));
        Assert.Equal(0, movement.GetHeight(step));
    }

    [Fact]
    public void CanMove_FastMovement_CannotTunnelThroughAThinObject()
    {
        var walker = Body(new Vector3(2, 2, 40));
        var field = CreateField(walker, (Body(new Vector3(1, 10, 40)), new Vector3(10, 0, 0)));
        Assert.True(field.CanStand(walker, Vector3.Zero));
        Assert.True(field.CanStand(walker, new Vector2(20, 0).AsVector3()));
        Assert.False(field.CanMove(walker, Vector3.Zero, new Vector2(20, 0)));
    }

    [Fact]
    public void CanMove_AlongATerrainBoundary_KeepsTheStationaryAxisExact()
    {
        var terrain = new MovementMap();
        var player = Body(new Vector3(24, 18, 40), spriteClass: SpriteClass.Man);
        var field = CreateField(player, terrain);
        terrain.RaiseTemporary(new Vector3(1336, 3136, 73));
        Assert.True(field.CanMove(player, new Vector2(1334, 3150).AsVector3(), new Vector2(1339.46f, 3150)));
    }

    [Fact]
    public void CanMove_RespectsTheWalkersFootprint()
    {
        var narrow = Body(new Vector3(8, 8, 40));
        var wide = Body(new Vector3(24, 18, 40));
        var terrain = new MovementMap();
        var sprites = CreateSprites(terrain);
        Place(sprites, Body(new Vector3(6, 200, 40)), new Vector3(30, 0, 0));
        Place(sprites, Body(new Vector3(6, 200, 40)), new Vector3(50, 0, 0));
        var field = CreateField(narrow, terrain, sprites);
        Assert.True(field.CanMove(narrow, new Vector2(40, -50).AsVector3(), new Vector2(40, 50)));
        Assert.False(field.CanMove(wide, new Vector2(40, -50).AsVector3(), new Vector2(40, 50)));
        var grid = new RouteGrid
        {
            Lattice = new Lattice(10, 7), Cells = Enumerable.Range(-10, 20).SelectMany(u => Enumerable.Range(-10, 20).Select(v => new Cell(u, v))).ToHashSet(),
            Edges = [], FenceEdges = [], BlockedCells = []
        };
        var narrowField = new WalkField(grid, false, terrain, sprites, narrow);
        var wideField = new WalkField(grid, false, terrain, sprites, wide);
        Assert.True(narrowField.CanMove(new Vector2(40, -50).AsVector3(), new Vector2(40, 50)));
        Assert.False(wideField.CanMove(new Vector2(40, -50).AsVector3(), new Vector2(40, 50)));
    }

    [Fact]
    public void Trace_TestsTerrainAndBodiesAtTheTravelHeight()
    {
        var movement = new MovementMap();
        movement.RaiseTemporary(new Vector3(32, 0, 24));
        var collector = new SpriteCollector(new SizeF(256, 256), new Vector2(130));
        var walker = new SpriteDefinition(KnownObject.Monster1, Body(new Vector3(16, 16, 20)), default, 0);
        var bullet = new SpriteDefinition(KnownObject.PistolBullet, Body(new Vector3(4, 4, 2)), default, 0);
        var shooter = new Sprite(0, walker, Vector3.Zero, 0, 10);
        collector.InsertToHash(shooter);
        collector.InsertToHash(new Sprite(1, walker, new Vector3(64, 0, 30), Army.Monsters, 10));
        var collision = new ProjectileCollision(movement, collector);

        Assert.Equal(new Vector3(32, 0, 10), collision.Trace(bullet, new Vector3(0, 0, 10),
            new Vector3(128, 0, 10), out bool wall));
        Assert.True(wall);
        Assert.Equal(new Vector3(64, 0, 40), collision.Trace(bullet, new Vector3(0, 0, 40),
            new Vector3(128, 0, 40), out bool target));
        Assert.True(target);
        Assert.Equal(new Vector3(128, 0, 60), collision.Trace(bullet, new Vector3(0, 0, 60),
            new Vector3(128, 0, 60), out bool clear));
        Assert.False(clear);
    }

    private static SpriteCollector CreateSprites(MovementMap terrain) => new(terrain.MapSize, new Vector2(256));

    private static Sprite Place(SpriteCollector sprites, GameObject obj, Vector3 position, KnownObject id = KnownObject.Barrel)
    {
        var sprite = new Sprite(0, new SpriteDefinition(id, obj, default, 0), position, Army.Neutral, 10);
        sprites.InsertToHash(sprite);

        return sprite;
    }

    private static WalkField CreateField(GameObject walker, params (GameObject Obj, Vector3 Position)[] obstacles)
    {
        var terrain = new MovementMap();
        var sprites = CreateSprites(terrain);
        foreach (var (obj, position) in obstacles)
        {
            Place(sprites, obj, position);
        }

        return CreateField(walker, terrain, sprites);
    }

    private static WalkField CreateField(GameObject walker, MovementMap terrain, SpriteCollector? sprites = null)
    {
        var grid = new RouteGrid
        {
            Lattice = new Lattice(10, 7), Cells = Enumerable.Range(0, 4).SelectMany(u => Enumerable.Range(0, 4).Select(v => new Cell(u, v))).ToHashSet(),
            Edges = [], FenceEdges = [], BlockedCells = []
        };

        return new WalkField(grid, false, terrain, sprites ?? CreateSprites(terrain), walker);
    }

    private static GameObject Body(Vector3 size, int mask = 1, SpriteClass spriteClass = SpriteClass.Unit, Vector3? scale = null, float dropHeight = 55)
    {
        byte[] bytes = new byte[Marshal.SizeOf<GameObject>()];
        MemoryMarshal.Write(bytes.AsSpan(4), in spriteClass);
        var flags = ObjectProperties.ZeroZ | ObjectProperties.Hash;
        MemoryMarshal.Write(bytes.AsSpan(8), in flags);
        MemoryMarshal.Write(bytes.AsSpan(12), in mask);
        MemoryMarshal.Write(bytes.AsSpan(16), in size);
        float climb = 10;
        MemoryMarshal.Write(bytes.AsSpan(100), in climb);
        MemoryMarshal.Write(bytes.AsSpan(104), in dropHeight);
        var visualScale = scale ?? Vector3.One;
        MemoryMarshal.Write(bytes.AsSpan(692), in visualScale);
        return MemoryMarshal.Read<GameObject>(bytes);
    }
}
