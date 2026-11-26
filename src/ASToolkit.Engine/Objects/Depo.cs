using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a depot sprite, which builds the units its player pays for, one queue entry at a time.
/// </summary>
public sealed class Depo : Unit
{
    private const int QueueSize = 20;
    private const int DefaultQueueCapacity = 10;
    private const int AttackReportMask = -(1 << 14);
    private const int BuiltSound = 105;

    private readonly ushort[] _queue = new ushort[QueueSize];
    private readonly int[] _buildTimes = new int[QueueSize];
    private readonly int[] _paused = new int[QueueSize];
    private readonly int _capacity;
    private int _batch;
    private int _slot;
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="Depo"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044c040)]
    public Depo(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        _capacity = DefaultQueueCapacity;
    }

    private static ReadOnlySpan<int> ItemOrder => [5, 10, 20, 25, 80, 85, 45, 30, 35, 82, 97, 90, 75, 62];

    [EngineRoutine(0x0044c100)]
    internal override void MoveTact()
    {
        if ((uint)(Map.CurrentTime & AttackReportMask) > (uint)Map.Session.PreviousCurrentTime)
        {
            IsAttackReported = false;
        }

        AddHpPerSecond(Map.Constant.DepoHpPerSecond);
    }

    [EngineRoutine(0x0044c130)]
    internal override int Action(ActionCode act, int var1, int var2, int var3)
    {
        switch (act)
        {
            case ActionCode.BuildUnit:
                if (!Map.ValidateVid(var1))
                    return 0;

                AddUnitToQueue(var1);
                BuildNextUnit();

                return 0;
            case ActionCode.BackupCommand when Command == Command.Build:
                ActionStack.Insert(new Act((int)ActionCode.BuildUnit, _queue[0], 0, 0));
                SetCommand(Command.None);

                return 0;
            case ActionCode.StopStack:
                ActionStack.Insert(new Act((int)act, var1, var2, var3));
                Action(ActionCode.NextCommand, 0, 0, 0);

                return 0;
            case ActionCode.Damage:
                var attacker = Map.FindSprite(var2);
                if (var1 > 0 && !IsAttackReported && attacker is not null && attacker.Army != Army)
                {
                    Map.ScriptRun(Map.GetEventFunction(MapEvent.DepoAttacked), this, var2);
                    IsAttackReported = true;
                }

                break;
            case ActionCode.SetArmy:
                var previousArmy = Army;
                Map.Player((int)previousArmy).Forget(this);
                ChangeArmy(var1);
                if (Army != previousArmy)
                {
                    Map.ScriptRun(Map.GetEventFunction(MapEvent.BuildingCapture), this);
                }

                return 0;
            case ActionCode.NextCommand:
                if (Animation >= SpriteAnimation.Death)
                    return 0;

                if (Animation == SpriteAnimation.Wound)
                {
                    ChangeAnimation(SpriteAnimation.Stand);
                }

                if (_slot == 0 || _paused[_slot - 1] != 0)
                {
                    if (Animation != SpriteAnimation.Stand)
                    {
                        ChangeAnimation(SpriteAnimation.Stand);
                    }

                    if (Command != Command.None)
                    {
                        SetCommand(Command.None);
                    }

                    Map.ScriptRun(Map.GetEventFunction(MapEvent.DepoFree), this);

                    return 0;
                }

                if (Timer == 0)
                {
                    ActionBuildUnit(var1, var2);
                    return 0;
                }

                if (Animation != SpriteAnimation.StopMove)
                {
                    ChangeAnimation(SpriteAnimation.StopMove);
                }

                return 0;
        }

        return base.Action(act, var1, var2, var3);
    }

    [EngineRoutine(0x0044c130)]
    internal override int Action(ActionCode act, ref Resource stream, int version)
    {
        switch (act)
        {
            case ActionCode.Save:
                if (!ActionStackHaveCommand(ActionCode.StopStack))
                {
                    ActionStack.Insert(new Act((int)ActionCode.StopStack, 0, 0, 0));
                }

                break;
            case ActionCode.Restore or ActionCode.RestoreOld:
                base.Action(act, ref stream, version);
                if (ExtraData is null)
                    return 0;

                var items = ExtraData.Items;
                int sorted = 0;
                foreach (int item in ItemOrder)
                {
                    for (int i = sorted; i < items.Count; i++)
                    {
                        if (items[i] != item)
                            continue;

                        int moved = items[sorted];
                        items[sorted] = item;
                        items[i] = moved;
                        sorted++;
                    }
                }

                return 0;
        }

        return base.Action(act, ref stream, version);
    }

    [EngineRoutine(0x0044c5f0)]
    private void AddUnitToQueue(int id)
    {
        if (_count >= _capacity)
            return;

        var player = Map.Player((int)Army);
        if (Map.Vid(id).GetBuildTime() > player.Money)
            return;

        player.Money -= Map.Vid(id).GetBuildTime();
        _queue[_count] = (ushort)id;
        _buildTimes[_count] = Map.Vid(id).GetBuildTime() * Map.Constant.BuildTimeFactor;
        _paused[_count] = 0;
        _count++;
    }

    [EngineRoutine(0x0044c700)]
    private void BuildNextUnit()
    {
        if (_slot != 0)
            return;

        _slot = 1;
        if (_paused[0] != 0)
        {
            while (_slot <= _capacity)
            {
                _slot++;
                if (_paused[_slot - 1] == 0)
                    break;
            }
        }

        if (_slot > _count)
        {
            _slot = 0;
            SetCommand(Command.None);
            ChangeAnimation(SpriteAnimation.Stand);

            return;
        }

        SetCommand(Command.Build);
        int id = _queue[_slot - 1];
        int time = _buildTimes[_slot - 1];
        Timer = time != 0 ? time : Map.Vid(id).GetBuildTime() * Map.Constant.BuildTimeFactor;
    }

    [EngineRoutine(0x0044c7e0)]
    private int ActionBuildUnit(int _, int _1)
    {
        while (true)
        {
            var blocker = Map.Sprites.CanPlace(Map.Vid(_queue[_slot - 1]), Position);
            if (blocker is null || blocker == Map.Mouse)
                break;

            Map.DeleteSprite(blocker);
        }

        var built = Map.CreateSprite(Map.Vid(_queue[_slot - 1]), Position, Direction, this);
        if (built is null)
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, _queue[_slot - 1], "Depo can't create unit"u8);

            return 0;
        }

        if (built.Vid.Class == SpriteClass.Engine)
        {
            ((Engine)built).DepoBatch = _batch;
        }

        _count--;
        for (int i = _slot - 1; i < _count; i++)
        {
            _queue[i] = _queue[i + 1];
            _buildTimes[i] = _buildTimes[i + 1];
            _paused[i] = _paused[i + 1];
        }

        _slot = 0;
        built.IsBuiltByDepo = true;
        Map.ScriptRun(Map.GetEventFunction(MapEvent.UnitCreated), built);
        built.PlaySfx(BuiltSound);
        if (_count == 0 && (ActionStack.Count == 0 || ActionStack[^1].Value == (int)ActionCode.StopStack))
        {
            if (built.Vid.Class == SpriteClass.Engine)
            {
                ((Engine)built).IsDepoBatchEnd = true;
            }

            _batch++;

            return 0;
        }

        BuildNextUnit();

        return 0;
    }
}
