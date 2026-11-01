namespace ASToolkit.Core.Levels;

/// <summary>
/// Specifies which side of a point its nearest wall stands on, in lattice directions.
/// </summary>
public enum WallSide
{
    /// <summary>A wall on the lower u side.</summary>
    LowerU,

    /// <summary>A wall on the upper u side.</summary>
    UpperU,

    /// <summary>A wall on the lower v side.</summary>
    LowerV,

    /// <summary>A wall on the upper v side.</summary>
    UpperV
}
