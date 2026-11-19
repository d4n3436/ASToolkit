using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class SpritePoolTests
{
    [Fact]
    public void Initialize_UsedSprite_LeavesNothingOfItsPreviousLife()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.Monster1)!;
        var other = definitions.Find(KnownObject.RedBloodSpray)!;
        var neighbor = new Sprite(4, other, Vector3.Zero, Army.Monsters, 1);
        var used = new Sprite(3, other, new Vector3(5, 6, 7), Army.Monsters, 40)
        {
            Placement = new MergedMapSprite(1, KnownObject.Crate, Vector3.One, 0, 0, []),
            Speed = 13,
            TurnTicks = 13,
            TurnCarry = 13,
            Command = Command.OneAttack,
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
            Shooter = neighbor,
            Owner = neighbor,
            LinkerBase = neighbor,
            LinkerOffset = new Vector3(11),
            LinkerDirection = new Angle(11),
            Depth = 13,
            IsBlocked = true,
            IsClimbing = true,
            Direction = new Angle(77)
        };
        used.ActionStack.Push(new Act((int)ActionCode.Attack, 0, 0, 0, neighbor));
        used.ChangeAnimation(SpriteAnimation.Death);
        used.MarkReferenced();

        used.Initialize(-1, definition, new Vector3(1, 2, 3), 0, definition.MaxHp, new Angle(9));
        var fresh = new Sprite(-1, definition, new Vector3(1, 2, 3), 0, definition.MaxHp) { Direction = new Angle(9) };

        Assert.Equal(Describe(fresh), Describe(used));
    }

    private static string Describe(Sprite sprite) =>
        string.Join(" ", sprite.Number, sprite.Definition.Id, sprite.Army, sprite.Placement is null, sprite.Position, sprite.Speed, sprite.TurnTicks, sprite.TurnCarry,
            sprite.Command, sprite.IsMoving, sprite.HasReachedX, sprite.HasReachedY, sprite.Hp, sprite.Bucket, sprite.Slot, sprite.ActionStack.Count,
            sprite.DiedAt, sprite.Animation, sprite.FirstFrame, sprite.Frame, sprite.LastFrame, sprite.LastTickTime, sprite.CreatedAt,
            sprite.Lifetime, sprite.Timer, sprite.VerticalSpeed, sprite.MaxSpeed, sprite.Goal is null, sprite.Shooter is null,
            sprite.Owner is null, sprite.LinkerBase is null, sprite.LinkerOffset, sprite.LinkerDirection, sprite.Collector is null, sprite.Depth, sprite.IsBlocked, sprite.IsClimbing, sprite.IsReferenced, sprite.Direction);
}
