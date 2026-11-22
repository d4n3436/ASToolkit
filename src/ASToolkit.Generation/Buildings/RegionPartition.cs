using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal readonly record struct RegionPartition(List<HashSet<Cell>> Regions, HashSet<Cell> Strip);
