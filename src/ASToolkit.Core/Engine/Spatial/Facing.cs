using System.Numerics;

namespace ASToolkit.Core.Engine;

internal readonly partial record struct Facing(byte Value)
{
    public const int FullTurn = 256;
    public const int HalfTurn = FullTurn / 2;
    public const int QuarterTurn = FullTurn / 4;
    private const double AngleUnitsPerRadian = 40.74366760253906;
    private const int AimGain = 0x9b74e;
    private const int AimGainShift = 20;
    private static readonly Vector2[] Directions = CreateDirections();

    public static Facing East => new(QuarterTurn);

    public static Facing West => new(QuarterTurn * 3);

    private static ReadOnlySpan<int> AimSteps => [64, 37, 19, 10, 5, 2, 1];

    public Vector2 Direction => Directions[Value];

    public Facing Opposite => this + HalfTurn;

    public override string ToString() => Value.ToString();

    public static Facing operator +(Facing facing, int turn) => new(unchecked((byte)(facing.Value + turn)));

    public static Facing operator -(Facing facing, int turn) => new(unchecked((byte)(facing.Value - turn)));

    public static Facing FromOffset(Vector2 offset)
    {
        if (offset.Y.IsZero())
        {
            return offset.X > 0 ? East : offset.X < 0 ? West : default;
        }

        double angle = Math.Atan(-(double)offset.X / offset.Y) * AngleUnitsPerRadian;

        if (offset.Y > 0)
        {
            angle += HalfTurn;
        }
        else if (offset.X < 0)
        {
            angle += FullTurn;
        }

        return new Facing(unchecked((byte)(int)angle));
    }

    public static Facing Aim(Vector2 offset) => Aim(offset, out _);

    [EngineRoutine(0x00406000)]
    public static Facing Aim(Vector2 offset, out int distance)
    {
        int x = (int)offset.X;
        int y = (int)offset.Y;
        int facing = HalfTurn;

        if (x < 0)
        {
            y = -y;
            x = -x;
            facing = HalfTurn * 3;
        }

        for (int step = 0; step < AimSteps.Length; step++)
        {
            int dx = y < 0 ? -(y >> step) : y >> step;
            int dy = y < 0 ? x >> step : -(x >> step);
            facing += y < 0 ? -AimSteps[step] : AimSteps[step];
            y += dy;
            x += dx;
        }

        distance = x * AimGain >> AimGainShift;

        return new Facing(unchecked((byte)(facing >> 1)));
    }

    public int MeasureTurnTo(Facing other) => Math.Min((byte)(Value - other.Value), (byte)(other.Value - Value));

    public Facing TurnToward(Facing goal, float period, int elapsed, out int remaining)
    {
        if (this == goal)
        {
            remaining = 0;

            return this;
        }

        if (period.IsZero())
        {
            remaining = 1;

            return goal;
        }

        int step = period.IsNear(ObjectDefinition.Unlimited) ? 0 : (int)(elapsed * (FullTurn / period) + 0.5f);

        return StepToward(goal, step, out remaining);
    }

    public Facing StepToward(Facing goal, int step, out int remaining)
    {
        int difference = MeasureTurnTo(goal);

        if (step == 0)
        {
            remaining = difference;

            return this;
        }

        if (difference <= step)
        {
            remaining = 0;

            return goal;
        }

        var facing = this + ((byte)(goal.Value - Value) <= HalfTurn ? step : -step);
        remaining = facing.MeasureTurnTo(goal);

        return facing;
    }

    private static Vector2[] CreateDirections()
    {
        var directions = new Vector2[FullTurn];
        for (int facing = 0; facing < FullTurn; facing++)
        {
            directions[facing] = new Vector2(SineTable[facing], -CosineTable[facing]);
        }

        return directions;
    }
}
