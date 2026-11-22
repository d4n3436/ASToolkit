namespace ASToolkit.Generation.Buildings;

/// <summary>
/// Provides the office block, one mass split into rooms with the dark run drawn through them to the breaker.
/// </summary>
internal sealed class OfficeGrammar : BuildingGrammar
{
    public OfficeGrammar(BuildingRequest request, Random rng) : base(request, rng)
    {
    }

    public override void Build() => BuildOfficeBlock();

    public override void PlaceSideRooms() => PlaceOfficeClosets();
}
