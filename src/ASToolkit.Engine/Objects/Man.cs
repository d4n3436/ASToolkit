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
    private const int WornArmorItemBase = 204;
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

    [EngineRoutine(0x00454800)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        switch (act)
        {
            case ActionCode.CoorAttack:
                CoorAttackCase(new Vector2(var1, var2));
                return 0;
            case ActionCode.ChangeVid:
                ChangeVidCase(var1, var2, var3, target);
                return 0;
            case ActionCode.AddItem:
                AddItemCase(var1);
                return 0;
            case ActionCode.Damage:
                return ActionDamage(var1, var2, var3, target);
            case ActionCode.GetAmmo:
                if (var1 != 0 && var1 != WeaponSlot)
                    return var1 >= 0 && var1 < SlotAmmo.Length ? SlotAmmo[var1] : 0;

                return base.Action(act, 0, 0, 0, null);
            case ActionCode.AddAmmo:
                // TODO: Port AS1's write before the slot array for a negative slot; the port returns 0
                if (var2 < 0 || var2 >= SlotAmmo.Length)
                    return 0;

                if (var2 != 0 && var2 != WeaponSlot)
                    return SlotAmmo[var2] += var1;

                return base.Action(act, var1, 0, 0, null);
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

        return base.Action(act, var1, var2, var3, target);
    }

    [EngineRoutine(0x00454670)]
    internal override void MoveTact()
    {
        int elapsed = Map.FrameTime;
        var next = MoveTactCalculateCoordinates(elapsed);

        if (Position.AsVector2() != next.AsVector2() && IsInsideMap(next.AsVector2()))
        {
            if (CanPlaceWithCrush(ref next) is null)
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
        Map.Action(Link, ActionCode.ChangeVid, slot + WeaponIdBase);
        Vid.LinkVid = Map.FindVid((KnownObject)(slot + WeaponIdBase)) ?? Map.EmptyVid;
        AddAmmo(SlotAmmo[slot] - GetAmmo());

        return true;
    }

    private void CoorAttackCase(Vector2 point)
    {
        var weapon = Vid.LinkVid;
        var torso = Link;

        if (torso is null || torso.Vid != weapon || weapon.FightChild == 0 || weapon.WeaponIndex == 0 || (uint)torso.Timer > ReloadTimerLimit ||
            torso.Animation == SpriteAnimation.Fight && torso.Frame <= torso.LastFrame)
            return;

        var goal = Map.ConstructGoal(GetCursorPoint(point, weapon));
        torso.SetCommand(Command.OneAttack, goal);
    }

    private void ChangeVidCase(int var1, int var2, int var3, Sprite? target)
    {
        if ((int)Vid.Id < PlayerIdLimit)
        {
            SlotAmmo[WeaponSlot] = base.Action(ActionCode.GetAmmo, 0, 0, 0, null);
        }

        base.Action(ActionCode.ChangeVid, var1, var2, var3, target);

        if ((int)Vid.Id > PlayerIdLimit)
        {
            var armed = Vid.LinkVid;
            int capacity = armed is not null && armed.FightChild != 0 && armed.WeaponIndex != 0 ? armed.Weapon.Ammo : Vid.Weapon.Ammo;
            base.Action(ActionCode.AddAmmo, capacity - base.Action(ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);

            return;
        }

        int armorItem = GetItemNumber(WornArmorItemBase) >= 0 ? WornArmorItemBase
            : GetItemNumber(WornArmorItemBase + 1) >= 0 ? WornArmorItemBase + 1
            : GetItemNumber(WornArmorItemBase + 2) >= 0 ? WornArmorItemBase + 2
            : 0;

        if (armorItem != 0)
        {
            var position = new Vector3(Position.X, Position.Y, Position.Z + Vid.LinkOffset.Z);
            var armor = Map.CreateSprite(Map.FindVid((KnownObject)(armorItem - WornArmorItemBase + (int)KnownObject.WornGreenArmor))!, position, Direction);
            Link!.AddLink(armor);
        }

        base.Action(ActionCode.AddAmmo, SlotAmmo[WeaponSlot] - base.Action(ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);
    }

    private void AddItemCase(int item)
    {
        if ((KnownObject)item is KnownObject.Dynamite or KnownObject.ExtraLife)
        {
            InsertItem(item);

            return;
        }

        if (!InsertUniqueItem(item))
            return;

        int slot = item - WeaponItemBase;

        if (slot is < 0 or >= WeaponSlots)
            return;

        if (base.Action(ActionCode.GetAmmo, 0, 0, 0, null) == 0 || WeaponSlot < slot || slot == 0)
        {
            ChangeWeapon(slot);
        }
    }

    private int ActionDamage(int damage, int var2, int var3, Sprite? target)
    {
        if (damage > 0)
        {
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
                    Map.Action(link, ActionCode.Damage, (share * damage + ArmorRounding) / Map.Percent, var2, var3);
                    damage -= share * damage / Map.Percent;
                }
            }

            if (damage >= Hp && Map.Action(this, ActionCode.HaveItem, (int)KnownObject.Medkit) != 0)
            {
                Map.Action(this, ActionCode.DeleteItem, (int)KnownObject.Medkit);
                ChangeHp(Vid.GetArmyMaxHp((int)Army));
                Map.CreateSprite(Map.FindVid(KnownObject.RescueMedkitEffect)!, Position + Vector3.UnitZ * RescueEffectHeight, default, this);

                return 0;
            }
        }

        return SpriteAction(ActionCode.Damage, damage, var2, var3, target);
    }

    private bool IsInsideMap(Vector2 point) => point is { X: >= 0, Y: >= 0 } && point.X < Map.Size.Width && point.Y < Map.Size.Height;

    private Vector3 GetCursorPoint(Vector2 cursor, Vid weapon)
    {
        var screen = Map.Session.Graphics.Screen;
        var player = Position;
        var pixel = cursor - Map.Shift;
        float y = cursor.Y;
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
            height = Map.GetGroundZ(cursor with { Y = cursor.Y + UnitAimLift }) + UnitAimLift;
        }
        else
        {
            height = Map.GetGroundZ(cursor) + GroundAimLift;
            y -= GroundAimLift;
        }

        return new Vector3(cursor.X, y + height, height);
    }
}
