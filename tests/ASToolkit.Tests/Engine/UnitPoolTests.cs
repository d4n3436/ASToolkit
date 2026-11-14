using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class UnitPoolTests
{
    [Fact]
    public void Initialize_UsedUnit_LeavesNothingOfItsPreviousLife()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var kind = kinds.Find(KnownObject.Monster1)!;
        var other = kinds.Find(KnownObject.RedBloodSpray)!;
        var neighbor = new Unit(4, other, Vector3.Zero, 1, 1);
        var used = new Unit(3, other, new Vector3(5, 6, 7), 1, 40)
        {
            Placement = new MergedMapSprite(1, KnownObject.Crate, Vector3.One, 0, 0, []),
            Speed = 13,
            TurnTicks = 13,
            TurnCarry = 13,
            Command = UnitCommand.OneAttack,
            Moving = true,
            ReachedX = true,
            ReachedY = true,
            DropItem = KnownObject.Money,
            Bucket = 13,
            Slot = 13,
            Orders = [new UnitOrder(ActionCode.Move, Vector2.One, neighbor)],
            OrderStep = 13,
            DiedAt = 13,
            Frame = 13,
            LastTickTime = 13,
            CreatedAt = 13,
            Lifetime = 13,
            Timer = 13,
            VerticalSpeed = 13,
            MaxSpeed = 13,
            Goal = neighbor,
            GoalPoint = new Vector3(12),
            Shooter = neighbor,
            Owner = neighbor,
            OwnerOffset = new Vector3(11),
            Depth = 13,
            Blocked = true,
            Climbing = true,
            Facing = new Facing(77)
        };
        used.SetState(SpriteAnimation.Death);
        used.MarkReferenced();

        used.Initialize(-1, kind, new Vector3(1, 2, 3), 0, kind.MaxHp, new Facing(9));
        var fresh = new Unit(-1, kind, new Vector3(1, 2, 3), 0, kind.MaxHp) { Facing = new Facing(9) };

        Assert.Equal(Describe(fresh), Describe(used));
    }

    private static string Describe(Unit unit) =>
        string.Join(" ", unit.Number, unit.Kind.Id, unit.Army, unit.Placement is null, unit.Position, unit.Speed, unit.TurnTicks, unit.TurnCarry,
            unit.Command, unit.Moving, unit.ReachedX, unit.ReachedY, unit.Hp, unit.DropItem, unit.Bucket, unit.Slot, unit.Orders.Length,
            unit.OrderStep, unit.DiedAt, unit.Animation, unit.FirstFrame, unit.Frame, unit.LastFrame, unit.LastTickTime, unit.CreatedAt,
            unit.Lifetime, unit.Timer, unit.VerticalSpeed, unit.MaxSpeed, unit.Goal is null, unit.GoalPoint, unit.Shooter is null,
            unit.Owner is null, unit.OwnerOffset, unit.Depth, unit.Blocked, unit.Climbing, unit.IsReferenced, unit.Facing);
}
