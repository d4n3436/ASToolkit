namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the warehouse, whose areas are drawn from the template of its level and composed by the layout solver.
/// </summary>
internal sealed class WarehouseGrammar : BuildingGrammar
{
    public WarehouseGrammar(BuildingRequest request, Random rng) : base(request, rng)
    {
    }

    public override void Build() => BuildWarehouse(Request.Level);
}
