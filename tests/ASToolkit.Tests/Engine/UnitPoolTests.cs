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
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.Monster1)!;
        var other = definitions.Find(KnownObject.RedBloodSpray)!;
        var neighbor = new Unit(4, other, Vector3.Zero, Army.Monsters, 1);
        var used = new Unit(3, other, new Vector3(5, 6, 7), Army.Monsters, 40)
        {
            Placement = new MergedMapSprite(1, KnownObject.Crate, Vector3.One, 0, 0, []),
            Speed = 13,
            TurnTicks = 13,
            TurnCarry = 13,
            Command = UnitCommand.OneAttack,
            IsMoving = true,
            HasReachedX = true,
            HasReachedY = true,
            Bucket = 13,
            Slot = 13,
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
            IsBlocked = true,
            IsClimbing = true,
            Direction = new Angle(77)
        };
        used.Orders.Push(new UnitOrder((int)ActionCode.Attack, 0, 0, 0, neighbor));
        used.ChangeAnimation(SpriteAnimation.Death);
        used.MarkReferenced();

        used.Initialize(-1, definition, new Vector3(1, 2, 3), 0, definition.MaxHp, new Angle(9));
        var fresh = new Unit(-1, definition, new Vector3(1, 2, 3), 0, definition.MaxHp) { Direction = new Angle(9) };

        Assert.Equal(Describe(fresh), Describe(used));
    }

    private static string Describe(Unit unit) =>
        string.Join(" ", unit.Number, unit.Definition.Id, unit.Army, unit.Placement is null, unit.Position, unit.Speed, unit.TurnTicks, unit.TurnCarry,
            unit.Command, unit.IsMoving, unit.HasReachedX, unit.HasReachedY, unit.Hp, unit.Bucket, unit.Slot, unit.Orders.Count,
            unit.DiedAt, unit.Animation, unit.FirstFrame, unit.Frame, unit.LastFrame, unit.LastTickTime, unit.CreatedAt,
            unit.Lifetime, unit.Timer, unit.VerticalSpeed, unit.MaxSpeed, unit.Goal is null, unit.GoalPoint, unit.Shooter is null,
            unit.Owner is null, unit.OwnerOffset, unit.Depth, unit.IsBlocked, unit.IsClimbing, unit.IsReferenced, unit.Direction);
}
