using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class ObjectProgram
{
    private readonly Random _random;
    private readonly GameWorld _world;
    private readonly StackAction[] _actions;
    private readonly UnitOrder[] _orders;
    private int _nextTick;
    private int _waitUntil;
    private int _frame;

    public ObjectProgram(ObjectDefinition kind, Vector3 position, Facing facing, StackAction[] actions, Random random,
        GameWorld world,
        MergedMapSprite? placement = null)
    {
        Kind = kind;
        Position = position;
        Facing = facing;
        _actions = actions;
        _orders = ReadOrders(actions);
        Cursor = actions.Length;
        Placement = placement;
        _random = random;
        _world = world;
        _nextTick = kind.GetVisualFrameDuration(SpriteAnimation.Stand);
    }

    public ObjectDefinition Kind { get; }

    public Vector3 Position { get; private set; }

    public Facing Facing { get; private set; }

    public ReadOnlySpan<StackAction> Actions => _actions;

    public MergedMapSprite? Placement { get; }

    public int Cursor { get; private set; }

    public bool Finished { get; private set; }

    public StackAction? PendingBuild { get; private set; }

    public void Stop() => Finished = true;

    public UnitOrder[] Orders => _orders;

    // Reads the move points before the first stop, last first
    private static UnitOrder[] ReadOrders(StackAction[] actions)
    {
        var orders = new List<UnitOrder>();
        foreach (var action in actions)
        {
            var code = action.Code;

            if (code == ActionCode.StopStack)
                break;

            if (code == ActionCode.Move)
            {
                orders.Add(new UnitOrder(code, new Vector2(action.Var1, action.Var2), null));
            }
        }

        orders.Reverse();

        return orders.ToArray();
    }

    [EngineRoutine(0x00440110)]
    public void Tick(Vector2 player, int time)
    {
        if (Finished || time < _nextTick)
        {
            return;
        }

        int interval = Kind.GetVisualFrameDuration(SpriteAnimation.Stand);
        _nextTick = time - time % interval + interval;

        if (++_frame < Math.Max(1, Kind.Object.FrameCounts[(int)SpriteAnimation.Stand]) && !Kind.Object.Flags.HasFlag(ObjectFlags.ActsEveryFrame))
            return;

        _frame = 0;

        if (time < _waitUntil)
        {
            return;
        }

        if (PendingBuild is not null)
        {
            if (_world.BuildFromProgram(this, PendingBuild.Value))
            {
                PendingBuild = null;
            }

            return;
        }

        if (Cursor == 0)
        {
            Finished = true;

            return;
        }

        var action = Actions[--Cursor];
        switch (action.Code)
        {
            case ActionCode.FlagmanTrigger:
                if (player.MeasureDistance(new Vector2(action.Var1, action.Var2)) >= action.Var3)
                {
                    Cursor++;
                }
                break;
            case ActionCode.Pause:
                _waitUntil = time + action.Var1 + _random.Next(action.Var2 + 1);
                break;
            case ActionCode.Turn:
                Facing = Facing.TurnToward(new Facing(unchecked((byte)action.Var1)), Kind.Object.TurnPeriod, interval, out _);
                break;
            case ActionCode.BuildUnit:
                if (Kind.Object.SpriteClass == SpriteClass.Building)
                {
                    PendingBuild = action;
                    _waitUntil = time + (_world.Kinds.Find((KnownObject)action.Var1)?.BuildTime ?? 0);
                }
                else
                {
                    _world.BuildFromProgram(this, action, testPlacement: false);
                }
                break;
            case ActionCode.DestroyUnit:
                _world.DestroySprite((KnownObject)action.Var1, new Vector2(action.Var2, action.Var3));
                break;
            case ActionCode.GotoStack:
                Cursor = Math.Clamp(action.Var1 + 1, 0, Actions.Length);
                break;
            case ActionCode.ChangeCoor:
                Position = new Vector3(action.Var1, action.Var2, action.Var3);
                break;
            case ActionCode.StopStack:
                Cursor++;
                Finished = true;
                break;
            case ActionCode.ClearStack:
                Cursor = 0;
                Finished = true;
                break;
            default:
                if ((int)action.Code <= (int)SpriteAnimation.Death2)
                {
                    var animation = (SpriteAnimation)action.Code;
                    _world.AnimateFromProgram(this, animation);
                    Finished = animation >= SpriteAnimation.Death;
                }

                break;
        }
    }
}
