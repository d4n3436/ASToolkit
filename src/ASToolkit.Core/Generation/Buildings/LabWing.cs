namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Specifies the second wing of a lab.
/// </summary>
public enum LabWing
{
    /// <summary>A small suite of offices behind the gate.</summary>
    Suite,

    /// <summary>A research wing of labs, passages and a depot around a court.</summary>
    Research,

    /// <summary>Halls of big rooms walked in sequence.</summary>
    BigRoomHalls,

    /// <summary>A containment gallery of cells behind a service corridor.</summary>
    Gallery,

    /// <summary>Echelon bays stepping down from their hall.</summary>
    Echelon,

    /// <summary>An airlock clean room around a sealed core.</summary>
    Airlock,

    /// <summary>A depot wing of two sections across a court.</summary>
    DepotSections,

    /// <summary>A depot wing along a loading dock.</summary>
    Dock,

    /// <summary>A depot wing of two rows across a locked service corridor.</summary>
    ServiceCorridor
}
