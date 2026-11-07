namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the warehouse, its areas drawn from its level's template and composed by the layout solver.
/// </summary>
internal sealed class WarehouseGrammar : BuildingGrammar
{
    public WarehouseGrammar(BuildingRequest request, Random rng) : base(request, rng)
    {
    }

    public override void Build() => BuildWarehouse(Request.Level);
}
