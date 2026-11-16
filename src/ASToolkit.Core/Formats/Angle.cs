using System.Numerics;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a direction in 256ths of a turn, clockwise from up the screen, such as the facing of a placed object or
/// the direction of the wind.
/// </summary>
/// <param name="Value">The direction in 256ths of a turn.</param>
public readonly partial record struct Angle(byte Value) : IComparable<Angle>
{
    /// <summary>
    /// Represents the number of steps in a full turn.
    /// </summary>
    public const int FullTurn = 256;

    /// <summary>
    /// Represents the number of steps in half a turn.
    /// </summary>
    public const int HalfTurn = FullTurn / 2;

    /// <summary>
    /// Represents the number of steps in a quarter turn.
    /// </summary>
    public const int QuarterTurn = FullTurn / 4;

    private const float AngleUnitsPerRadian = 40.74366760253906f;
    private const int AimGain = 0x9b74e;
    private const int AimGainShift = 20;
    private const int NeverTurns = 999999;
    private static readonly Vector2[] Vectors = CreateVectors();

    /// <summary>
    /// Gets the direction up the screen.
    /// </summary>
    public static Angle North => default;

    /// <summary>
    /// Gets the direction down the screen.
    /// </summary>
    public static Angle South => new(HalfTurn);

    /// <summary>
    /// Gets the direction to the right of the screen.
    /// </summary>
    public static Angle East => new(QuarterTurn);

    /// <summary>
    /// Gets the direction to the left of the screen.
    /// </summary>
    public static Angle West => new(QuarterTurn * 3);

    private static ReadOnlySpan<int> AimSteps => [64, 37, 19, 10, 5, 2, 1];

    /// <summary>
    /// Gets the unit vector of the direction in map coordinates.
    /// </summary>
    public Vector2 Vector => Vectors[Value];

    /// <summary>
    /// Gets the sine of the angle from the engine's table.
    /// </summary>
    public float Sine => SineTable[Value];

    /// <summary>
    /// Gets the cosine of the angle from the engine's table.
    /// </summary>
    public float Cosine => CosineTable[Value];

    /// <summary>
    /// Gets the opposite direction.
    /// </summary>
    public Angle Opposite => this + HalfTurn;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();

    /// <inheritdoc/>
    public int CompareTo(Angle other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Creates a direction from its value in 256ths of a turn.
    /// </summary>
    /// <param name="value">The direction in 256ths of a turn.</param>
    public static implicit operator Angle(byte value) => new(value);

    /// <summary>
    /// Returns the direction turned clockwise by a number of steps, wrapping around.
    /// </summary>
    /// <param name="angle">The direction.</param>
    /// <param name="turn">The steps to turn by.</param>
    public static Angle operator +(Angle angle, int turn) => new(unchecked((byte)(angle.Value + turn)));

    /// <summary>
    /// Returns the direction turned counterclockwise by a number of steps, wrapping around.
    /// </summary>
    /// <param name="angle">The direction.</param>
    /// <param name="turn">The steps to turn by.</param>
    public static Angle operator -(Angle angle, int turn) => new(unchecked((byte)(angle.Value - turn)));

    /// <summary>
    /// Creates a direction from the low byte of a stored value.
    /// </summary>
    /// <param name="value">The stored value.</param>
    public static Angle FromStored(long value) => new(unchecked((byte)value));

    /// <summary>
    /// Returns the direction of an offset in map coordinates.
    /// </summary>
    /// <param name="offset">The offset.</param>
    [EngineRoutine(0x00406080)]
    public static Angle FromOffset(Vector2 offset)
    {
        if (offset.Y.IsZero())
        {
            return offset.X > 0 ? East : offset.X < 0 ? West : default;
        }

        float angle = (float)Math.Atan(-(offset.X / offset.Y)) * AngleUnitsPerRadian;

        if (offset.Y > 0)
        {
            angle += HalfTurn;
        }
        else if (offset.X < 0)
        {
            angle += FullTurn;
        }

        return new Angle(unchecked((byte)(int)angle));
    }

    /// <summary>
    /// Returns the direction of an offset by the engine's integer approximation.
    /// </summary>
    /// <param name="offset">The offset.</param>
    public static Angle Aim(Vector2 offset) => Aim(offset, out _);

    /// <summary>
    /// Returns the direction of an offset by the engine's integer approximation, with its length.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <param name="distance">The approximate length of the offset.</param>
    [EngineRoutine(0x00406000)]
    public static Angle Aim(Vector2 offset, out int distance)
    {
        int x = (int)offset.X;
        int y = (int)offset.Y;
        int direction = HalfTurn;

        if (x < 0)
        {
            y = -y;
            x = -x;
            direction = HalfTurn * 3;
        }

        for (int step = 0; step < AimSteps.Length; step++)
        {
            int dx = y < 0 ? -(y >> step) : y >> step;
            int dy = y < 0 ? x >> step : -(x >> step);
            direction += y < 0 ? -AimSteps[step] : AimSteps[step];
            y += dy;
            x += dx;
        }

        distance = x * AimGain >> AimGainShift;

        return new Angle(unchecked((byte)(direction >> 1)));
    }

    /// <summary>
    /// Returns the index of the direction frame the engine shows for this direction, out of a number of direction frames.
    /// </summary>
    /// <param name="directions">The number of direction frames.</param>
    [EngineRoutine(0x0043a1c0)]
    public int GetDirectionIndex(int directions) => directions == 0 ? 0 : ((Value + HalfTurn / directions) & 0xff) * directions >> 8;

    /// <summary>
    /// Returns the direction the engine's direction frame for this direction faces, out of a number of direction frames.
    /// </summary>
    /// <param name="directions">The number of direction frames.</param>
    public Angle GetFrameDirection(int directions) => new(unchecked((byte)(GetDirectionIndex(directions) * FullTurn / directions)));

    /// <summary>
    /// Returns the steps of the shorter turn to another direction.
    /// </summary>
    /// <param name="other">The other direction.</param>
    public int MeasureTurnTo(Angle other) => Math.Min((byte)(Value - other.Value), (byte)(other.Value - Value));

    /// <summary>
    /// Returns the direction after turning toward a goal for a time, at the rate of a full turn per period.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="period">The milliseconds of a full turn; 0 turns at once and 999999 never turns.</param>
    /// <param name="elapsed">The milliseconds to turn for.</param>
    /// <param name="remaining">The steps left to the goal.</param>
    public Angle TurnToward(Angle goal, float period, int elapsed, out int remaining)
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

        int step = period.IsNear(NeverTurns) ? 0 : (int)(elapsed * (FullTurn / period) + 0.5f);

        return StepToward(goal, step, out remaining);
    }

    /// <summary>
    /// Returns the direction after turning toward a goal by a number of steps.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="step">The steps to turn by.</param>
    /// <param name="remaining">The steps left to the goal.</param>
    public Angle StepToward(Angle goal, int step, out int remaining)
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

        var angle = this + ((byte)(goal.Value - Value) <= HalfTurn ? step : -step);
        remaining = angle.MeasureTurnTo(goal);

        return angle;
    }

    private static Vector2[] CreateVectors()
    {
        var vectors = new Vector2[FullTurn];
        for (int value = 0; value < FullTurn; value++)
        {
            vectors[value] = new Vector2(SineTable[value], -CosineTable[value]);
        }

        return vectors;
    }
}
