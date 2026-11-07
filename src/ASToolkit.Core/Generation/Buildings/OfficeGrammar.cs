namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the office block, a level graph of the dark run and the rooms the breaker opens laid by the layout solver into one building.
/// </summary>
internal sealed class OfficeGrammar : BuildingGrammar
{
    public OfficeGrammar(BuildingRequest request, Random rng) : base(request, rng)
    {
    }

    public override void Build() => BuildOfficeBlock();

    public override void PlaceSideRooms() => PlaceOfficeClosets();
}
