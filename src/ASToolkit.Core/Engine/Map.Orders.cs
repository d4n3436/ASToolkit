using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private readonly Dictionary<Sprite, PendingBuild> _builds = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the definition a building is building.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The definition, or null when the building is idle.</returns>
    public SpriteDefinition? GetPendingBuild(Sprite building) => _builds.TryGetValue(building, out var pending) ? pending.Definition : null;

    [EngineRoutine(0x00440110)]
    private void PopOrder(Sprite sprite)
    {
        var order = sprite.ActionStack.Top;
        var state = sprite.Animation;

        if (order.Value > Act.LastState &&
            (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && sprite.Speed.IsZero()))
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (order.Value == Act.StopMarker)
            return;

        sprite.ActionStack.Pop();

        if (order.Value <= Act.LastState)
        {
            sprite.ChangeAnimation((SpriteAnimation)order.Value);

            return;
        }

        Action(sprite, order.Code, order.Var1, order.Var2, order.Var3, order.Target);
    }

    // TODO: Port the goal sprite of the empty definition
    /// <summary>
    /// Draws the random number the constructor of a move order's goal sprite draws.
    /// </summary>
    public void DrawGoalSpriteRandom() => _rng.Next();

    [EngineRoutine(0x00454800)]
    [EngineRoutine(0x00447500)]
    [EngineRoutine(0x0044ca70)]
    [EngineRoutine(0x00448950)]
    [EngineRoutine(0x00440830)]
    private void NextCommand(Sprite sprite)
    {
        if (sprite == Player)
        {
            Weapons.DecidePlayer(FrameTime);
            sprite.ChangeAnimation(!sprite.Speed.IsZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);

            return;
        }

        if (sprite == Weapons.Torso)
        {
            SpawnedObjectSystem.DecideObject(sprite);

            return;
        }

        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Creature:
                _brain.Decide(sprite, Player, FrameTime, Time);
                break;
            case SpriteClass.Cannon:
                SpawnedObjectSystem.DecideCannon(sprite);
                break;
            default:
                SpawnedObjectSystem.DecideObject(sprite);
                break;
        }
    }

    [EngineRoutine(0x0044ca70)]
    private void StartBuild(Sprite building, int id, int x, int y)
    {
        var definition = Definitions.Find((KnownObject)id);

        if (building.Command != Command.None || definition is null)
            return;

        _builds[building] = new PendingBuild(definition, x, y);
        building.Timer = definition.BuildTime;
        SpriteKinematics.SetCommand(building, Command.Build);

        if (building.Animation < SpriteAnimation.Death)
        {
            building.ChangeAnimation(SpriteAnimation.StopMove);
        }
    }

    [EngineRoutine(0x0044ca70)]
    private void DecideBuilding(Sprite building)
    {
        if (building.Animation >= SpriteAnimation.Death)
            return;

        bool isBuilding = building.Command == Command.Build;

        if (isBuilding)
        {
            building.ChangeAnimation(SpriteAnimation.StopMove);
        }
        else if (building.Animation != SpriteAnimation.StandOpen)
        {
            building.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (!isBuilding || building.Timer != 0 || !_builds.TryGetValue(building, out var pending))
            return;

        var position = GetBuildPosition(building, pending.X, pending.Y);

        if (!IsFree(pending.Definition, position))
            return;

        SpriteKinematics.SetCommand(building, Command.None);
        var built = CreateBuilt(pending.Definition, position, building.Direction);

        if (built is not null)
        {
            CopyOrders(building, built);
        }

        _builds.Remove(building);
        building.ChangeAnimation(SpriteAnimation.Stand);
    }

    [EngineRoutine(0x00440830)]
    private void Build(Sprite builder, int id, int x, int y)
    {
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null)
            return;

        var built = CreateBuilt(definition, GetBuildPosition(builder, x, y), builder.Direction);

        if (built is not null)
        {
            CopyOrders(builder, built);
        }
    }

    [EngineRoutine(0x00440830)]
    private Vector3 GetBuildPosition(Sprite builder, int x, int y)
    {
        var position = builder.Position;
        float atX = x == 0 && y == 0 ? position.X : x;
        float atY = x == 0 && y == 0 ? position.Y : y;

        if (x < 0)
        {
            atX = position.X - x - _rng.Next() % (1 - x) * 2;
        }

        if (y < 0)
        {
            atY = position.Y - y - _rng.Next() % (1 - y) * 2;
        }

        return new Vector3(atX, atY, position.Z);
    }

    private Sprite? CreateBuilt(SpriteDefinition definition, Vector3 position, Angle direction)
    {
        if (definition.MaxHp > 0)
            return AddMonster(definition, position, direction);

        if (definition.Id.IsEndlessGenerator)
            return AddScripted(definition, position, direction);

        return Effects.Spawn(definition.Id, position, direction, Time);
    }

    [EngineRoutine(0x00440830)]
    [EngineRoutine(0x00445be0)]
    private static void CopyOrders(Sprite from, Sprite to)
    {
        if (to == from)
            return;

        to.ActionStack.Clear();
        foreach (var order in from.ActionStack.Orders)
        {
            if (order.Value == Act.StopMarker)
                break;

            to.ActionStack.Push(order);
        }
    }

    private readonly record struct PendingBuild(SpriteDefinition Definition, int X, int Y);
}
