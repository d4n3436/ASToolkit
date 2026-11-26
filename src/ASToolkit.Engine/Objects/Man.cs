using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the player's body sprite.
/// </summary>
public sealed class Man : Unit
{
    private const int MaxWeaponSlot = 9;
    private const int ArmorRounding = 50;
    private const int PlayerIdLimit = 20;
    private const uint ReloadTimerLimit = 5000;
    private const float RescueEffectHeight = 22;
    private const int WornArmorShareCount = 3;
    private const float AimRise = 70;
    private const float AimRiseCap = 50;
    private const int DepthShift = 3;
    private const float DepthBias = 128;
    private const float UnitAimLift = 80;
    private const float GroundAimLift = 19;
    private const int WeaponIdLimit = 21;

    private readonly int[] _slotAmmo = new int[MaxWeaponSlot + 1];

    /// <summary>
    /// Initializes a new instance of the <see cref="Man"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x004545e0)]
    public Man(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        InsertUniqueItem(WeaponItemBase + Pistols);
    }

    /// <summary>
    /// Gets the ammunition the player's body keeps for the slots it does not hold.
    /// </summary>
    public Span<int> SlotAmmo => _slotAmmo;

    private static ReadOnlySpan<int> WornArmorShares => [50, 70, 90];

    [EngineRoutine(0x00454660)]
    internal override void Destruct() => base.Destruct();

    [EngineRoutine(0x00454670)]
    internal override void MoveTact()
    {
        int elapsed = Map.FrameTime;
        var next = MoveTactCalculateCoordinates(elapsed);

        if (Position.AsVector2() != next.AsVector2() && next is { X: >= 0, Y: >= 0 } && next.X < Map.Size.Width && next.Y < Map.Size.Height)
        {
            if (CanPlaceWithCrushAndGlide(ref next) is null)
            {
                ChangeCoor(next);
            }
        }

        var goal = Goal;

        if (goal is null || Command != Command.Move)
            return;

        var direction = DirectionTo(goal) + (Speed < 0 ? Angle.HalfTurn : 0);
        RotateTact(RotateForGlide(direction), elapsed);

        if (this is { HasReachedX: true, HasReachedY: true } || IsTouching(goal.Position.AsVector2(), goal.Vid.Half))
        {
            Stop();
        }
    }

    [EngineRoutine(0x00454800)]
    internal override int Action(ActionCode act, int var1, int var2, int var3)
    {
        switch (act)
        {
            case ActionCode.CoorAttack:
                var point = new Vector2(var1, var2);
                var weapon = Vid.LinkVid;
                var torso = Link;

                if (torso is null || torso.Vid != weapon || weapon.FightChild == 0 || weapon.WeaponIndex == 0 || (uint)torso.Timer > ReloadTimerLimit ||
                    torso.Animation == SpriteAnimation.Fight && torso.Frame <= torso.LastFrame)
                    return 0;

                var screen = Map.Session.Graphics.Screen;
                var player = Position;
                var pixel = point - Map.Shift;
                float y = point.Y;
                float height;

                if (screen is not null && pixel.X >= Map.Session.Graphics.Viewport.Left && pixel.X < Map.Session.Graphics.Viewport.Right && pixel.Y >= Map.Session.Graphics.Viewport.Top && pixel.Y < Map.Session.Graphics.Viewport.Bottom)
                {
                    height = (screen.Depth[(int)pixel.Y * screen.DepthWidth + (int)pixel.X] >> DepthShift) - DepthBias;

                    if (height > player.Z + AimRise)
                    {
                        height = player.Z + AimRiseCap;
                    }
                }
                else if (weapon.Weapon.Targets == OldSpriteType.Unit)
                {
                    height = Map.GetGroundZ(point with { Y = point.Y + UnitAimLift }) + UnitAimLift;
                }
                else
                {
                    height = Map.GetGroundZ(point) + GroundAimLift;
                    y -= GroundAimLift;
                }

                var goal = Map.ConstructGoal(new Vector3(point.X, y + height, height));
                torso.SetCommand(Command.OneAttack, goal);
                return 0;
            case ActionCode.ChangeVid:
                if ((int)Vid.Id < PlayerIdLimit)
                {
                    SlotAmmo[WeaponSlot] = base.Action(ActionCode.GetAmmo, 0, 0, 0);
                }

                base.Action(ActionCode.ChangeVid, var1, var2, var3);

                if ((int)Vid.Id > PlayerIdLimit)
                {
                    base.Action(ActionCode.AddAmmo, Vid.GetMaxAmmo() - base.Action(ActionCode.GetAmmo, 0, 0, 0), 0, 0);

                    return 0;
                }

                var armorItem = GetItemNumber((int)KnownObject.GreenArmor) >= 0 ? KnownObject.GreenArmor
                    : GetItemNumber((int)KnownObject.YellowArmor) >= 0 ? KnownObject.YellowArmor
                    : GetItemNumber((int)KnownObject.RedArmor) >= 0 ? KnownObject.RedArmor
                    : 0;

                if (armorItem != 0)
                {
                    var position = new Vector3(Position.X, Position.Y, Position.Z + Vid.LinkOffset.Z);
                    var armor = Map.CreateSprite(Map.FindVid(KnownObject.WornGreenArmor + (armorItem - KnownObject.GreenArmor))!, position, Direction);
                    Link!.AddLink(armor);
                }

                base.Action(ActionCode.AddAmmo, SlotAmmo[WeaponSlot] - base.Action(ActionCode.GetAmmo, 0, 0, 0), 0, 0);
                return 0;
            case ActionCode.AddItem:
                if ((KnownObject)var1 is KnownObject.Dynamite or KnownObject.ExtraLife)
                {
                    InsertItem(var1);

                    return 0;
                }

                if (!InsertUniqueItem(var1))
                    return 0;

                int slot = var1 - WeaponItemBase;

                if (slot is < 0 or >= WeaponSlots)
                    return 0;

                if (base.Action(ActionCode.GetAmmo, 0, 0, 0) == 0 || WeaponSlot < slot || slot == 0)
                {
                    ChangeWeapon(slot);
                }

                return 0;
            case ActionCode.Damage:
                if (var1 <= 0)
                    return SpriteAction(ActionCode.Damage, var1, var2, var3);

                if (Vid.Id != KnownObject.MannedCannon)
                {
                    foreach (var link in EnumerateLinks())
                    {
                        if (link.Vid.Id is KnownObject.InvulnerabilityEffect or KnownObject.RescueMedkitEffect)
                            return 0;
                    }

                    foreach (var link in EnumerateLinks())
                    {
                        int armor = link.Vid.Id - KnownObject.WornGreenArmor;

                        if (armor is < 0 or >= WornArmorShareCount)
                            continue;

                        int share = WornArmorShares[armor];
                        Map.Action(link, ActionCode.Damage, (share * var1 + ArmorRounding) / Map.Percent, var2, var3);
                        var1 -= share * var1 / Map.Percent;
                    }
                }

                if (var1 < Hp || Map.Action(this, ActionCode.HaveItem, (int)KnownObject.Medkit) == 0)
                    return SpriteAction(ActionCode.Damage, var1, var2, var3);

                Map.Action(this, ActionCode.DeleteItem, (int)KnownObject.Medkit);
                ChangeHp(Vid.GetArmyMaxHp((int)Army));
                Map.CreateSprite(Map.FindVid(KnownObject.RescueMedkitEffect)!, Position + Vector3.UnitZ * RescueEffectHeight, default, this);

                return 0;
            case ActionCode.GetAmmo:
                if (var1 != 0 && var1 != WeaponSlot)
                    return ReadSlot(var1);

                return base.Action(act, 0, 0, 0);
            case ActionCode.AddAmmo:
                if (var2 > MaxWeaponSlot)
                    return 0;

                if (var2 != 0 && var2 != WeaponSlot)
                {
                    int added = ReadSlot(var2) + var1;
                    WriteSlot(var2, added);

                    return added;
                }

                return base.Action(act, var1, 0, 0);
            case ActionCode.NextCommand:
                if (Animation >= SpriteAnimation.Death)
                    return 0;

                if (HasGoal || Link is { HasGoal: true })
                {
                    AttackResult = AttackTact((int)Math.Max((uint)Map.FrameTime, (uint)Vid.GetVisualFrameDuration(Animation)));
                }

                ChangeAnimation(!Speed.IsExactlyZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);

                return 0;
        }

        return base.Action(act, var1, var2, var3);
    }

    [EngineRoutine(0x00455070)]
    internal bool ChangeWeapon(int slot)
    {
        var held = Vid.LinkVid;

        if (held is null || (int)held.Id >= WeaponIdLimit)
            return false;

        if (slot == WeaponSlots)
        {
            slot = 0;
        }

        if (!Items.Contains(WeaponItemBase + slot) || Link is null || Link.Vid != held)
            return false;

        SlotAmmo[WeaponSlot] = GetAmmo();
        Map.Action(Link, ActionCode.ChangeVid, (int)(KnownObject.SecretWeaponTorso + slot));
        Vid.LinkVid = Map.FindVid(KnownObject.SecretWeaponTorso + slot) ?? Map.EmptyVid;
        AddAmmo(SlotAmmo[slot] - GetAmmo());

        return true;
    }

    private int ReadSlot(int slot) => slot switch
    {
        >= 0 and <= MaxWeaponSlot => _slotAmmo[slot],
        -1 => (int)Behavior,
        -2 => TurnTicks,
        -3 => _playerSlot,
        -4 => Ammo,
        -5 => _itemList,
        -6 => _itemCount,
        -7 => ShouldRefillAmmo ? 1 : 0,
        -8 => _unused,
        _ => throw new NotSupportedException($"The ammo slot {slot} lies outside the fields of the unit.")
    };

    private void WriteSlot(int slot, int value)
    {
        switch (slot)
        {
            case >= 0 and <= MaxWeaponSlot:
                _slotAmmo[slot] = value;
                break;
            case -1:
                Behavior = (WeaponBehavior)value;
                break;
            case -2:
                TurnTicks = value;
                break;
            case -3:
                _playerSlot = value;
                break;
            case -4:
                Ammo = value;
                break;
            case -5:
                _itemList = value;
                break;
            case -6:
                _itemCount = value;
                break;
            case -7 when value is 0 or 1:
                ShouldRefillAmmo = value != 0;
                break;
            case -7:
                throw new NotSupportedException($"The refill flag can't hold the value {value}.");
            case -8:
                _unused = value;
                break;
            default:
                throw new NotSupportedException($"The ammo slot {slot} lies outside the fields of the unit.");
        }
    }
}
