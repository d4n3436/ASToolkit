using System.Drawing;
using System.Numerics;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a direction in 256ths of a turn, clockwise from up the screen, such as the direction a placed object faces or
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
    /// Initializes a new instance of the <see cref="Angle"/> structure from an offset in map coordinates.
    /// </summary>
    /// <param name="offset">The offset.</param>
    [EngineRoutine(0x00439b20)]
    public Angle(Vector2 offset)
        : this(FromOffset(offset).Value)
    {
    }

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

    /// <summary>
    /// Gets the unit vector of the direction in map coordinates.
    /// </summary>
    public Vector2 Vector => Vectors[Value];

    /// <summary>
    /// Gets the sine of the angle from the 256-step sine table.
    /// </summary>
    [EngineRoutine(0x00439b50)]
    public float Sine => SineTable[Value];

    /// <summary>
    /// Gets the cosine of the angle from the 256-step cosine table.
    /// </summary>
    [EngineRoutine(0x00439b60)]
    public float Cosine => CosineTable[Value];

    /// <summary>
    /// Gets the sine of the angle scaled by the map's vertical projection, for offsets along the map's y axis.
    /// </summary>
    public float ProjectedSine => ProjectedSineTable[Value];

    /// <summary>
    /// Gets the cosine of the angle scaled by the map's vertical projection, for offsets along the map's y axis.
    /// </summary>
    public float ProjectedCosine => ProjectedCosineTable[Value];

    /// <summary>
    /// Gets the opposite direction.
    /// </summary>
    public Angle Opposite => this + HalfTurn;

    private static ReadOnlySpan<int> AimSteps => [64, 37, 19, 10, 5, 2, 1];

    /// <summary>
    /// Creates a direction from its value in 256ths of a turn.
    /// </summary>
    /// <param name="value">The direction in 256ths of a turn.</param>
    /// <returns>The direction.</returns>
    public static implicit operator Angle(byte value) => new(value);

    /// <summary>
    /// Turns a direction clockwise by a number of steps and wraps around past a full turn.
    /// </summary>
    /// <param name="angle">The direction.</param>
    /// <param name="turn">The steps to turn by.</param>
    /// <returns>The turned direction.</returns>
    public static Angle operator +(Angle angle, int turn) => new(unchecked((byte)(angle.Value + turn)));

    /// <summary>
    /// Turns a direction counterclockwise by a number of steps and wraps around past a full turn.
    /// </summary>
    /// <param name="angle">The direction.</param>
    /// <param name="turn">The steps to turn by.</param>
    /// <returns>The turned direction.</returns>
    public static Angle operator -(Angle angle, int turn) => new(unchecked((byte)(angle.Value - turn)));

    /// <summary>
    /// Returns the direction of an offset in map coordinates.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The direction of the offset.</returns>
    [EngineRoutine(0x00406080)]
    public static Angle FromOffset(Vector2 offset)
    {
        if (offset.Y.IsExactlyZero())
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
    /// Returns the direction of an offset by an integer approximation.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The direction of the offset.</returns>
    public static Angle Aim(Vector2 offset) => Aim(offset, out _);

    /// <summary>
    /// Returns the direction of an offset by an integer approximation, with its length.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <param name="distance">The approximate length of the offset.</param>
    /// <returns>The direction of the offset.</returns>
    [EngineRoutine(0x00406000)]
    public static Angle Aim(Vector2 offset, out int distance)
    {
        var point = new Point((int)offset.X, (int)offset.Y);
        int direction = HalfTurn;

        if (point.X < 0)
        {
            point = new Point(-point.X, -point.Y);
            direction = HalfTurn * 3;
        }

        for (int step = 0; step < AimSteps.Length; step++)
        {
            var delta = new Size(point.Y < 0 ? -(point.Y >> step) : point.Y >> step, point.Y < 0 ? point.X >> step : -(point.X >> step));
            direction += point.Y < 0 ? -AimSteps[step] : AimSteps[step];
            point += delta;
        }

        distance = point.X * AimGain >> AimGainShift;

        return new Angle(unchecked((byte)(direction >> 1)));
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();

    /// <inheritdoc/>
    public int CompareTo(Angle other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Returns the index of the direction frame the engine shows for this direction, out of a number of direction frames.
    /// </summary>
    /// <param name="directions">The number of direction frames.</param>
    /// <returns>The index of the direction frame.</returns>
    public int GetDirectionIndex(int directions) => directions == 0 ? 0 : GetDirectionIndex(directions, HalfTurn / directions);

    /// <summary>
    /// Returns the index of the direction frame for this direction, out of a number of direction frames, with a given offset added to the direction first.
    /// </summary>
    /// <param name="directions">The number of direction frames.</param>
    /// <param name="offset">The offset added to the direction before it is divided into frames.</param>
    /// <returns>The index of the direction frame.</returns>
    [EngineRoutine(0x0043a1c0)]
    public int GetDirectionIndex(int directions, int offset) => ((offset + Value) & 0xff) * directions >> 8;

    /// <summary>
    /// Returns the direction that the direction frame showing this direction faces, out of a number of direction frames.
    /// </summary>
    /// <param name="directions">The number of direction frames.</param>
    /// <returns>The direction of the direction frame.</returns>
    public Angle GetFrameDirection(int directions) => new(unchecked((byte)(GetDirectionIndex(directions) * FullTurn / directions)));

    /// <summary>
    /// Returns the steps of the shorter turn to another direction.
    /// </summary>
    /// <param name="other">The other direction.</param>
    /// <returns>The number of steps of the shorter turn.</returns>
    public int MeasureTurnTo(Angle other) => Math.Min((byte)(Value - other.Value), (byte)(other.Value - Value));

    /// <summary>
    /// Returns the direction after turning toward a goal for a time, at the rate of a full turn per period.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="period">The milliseconds of a full turn; 0 turns at once and 999999 never turns.</param>
    /// <param name="elapsed">The milliseconds to turn for.</param>
    /// <param name="remaining">The turn left to the goal.</param>
    /// <returns>The direction after the turn.</returns>
    public Angle TurnToward(Angle goal, float period, int elapsed, out Angle remaining)
    {
        if (this == goal)
        {
            remaining = default;

            return this;
        }

        if (period.IsExactlyZero())
        {
            remaining = 1;

            return goal;
        }

        int step = period.IsExactly(NeverTurns) ? 0 : (int)(elapsed * (FullTurn / period) + 0.5f);

        return StepToward(goal, step, out remaining);
    }

    /// <summary>
    /// Returns the direction after turning toward a goal by a number of steps.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="step">The steps to turn by.</param>
    /// <param name="remaining">The turn left to the goal.</param>
    /// <returns>The direction after the turn.</returns>
    public Angle StepToward(Angle goal, int step, out Angle remaining)
    {
        int difference = MeasureTurnTo(goal);

        if (step == 0)
        {
            remaining = (byte)difference;

            return this;
        }

        if (difference <= step)
        {
            remaining = default;

            return goal;
        }

        var angle = this + ((byte)(goal.Value - Value) <= HalfTurn ? step : -step);
        remaining = (byte)angle.MeasureTurnTo(goal);

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
