using ASToolkit.Core.Reference;
using SixLabors.ImageSharp.Formats.Png;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal abstract class LevelCommand<TSettings> : Command<TSettings>
    where TSettings : CommandSettings
{
    protected static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];


    private const int MaxLevelsAtOnce = 3;

    protected static ParallelOptions CreateParallelOptions(CancellationToken cancellationToken, int maxLevelsAtOnce = MaxLevelsAtOnce) =>
        new() { MaxDegreeOfParallelism = maxLevelsAtOnce, CancellationToken = cancellationToken };

    protected static string[] GetMaps(string[] maps, string? levels)
    {
        if (maps.Length > 0)
        {
            return maps;
        }

        var campaign = ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName);
        return levels is null
            ? campaign.Concat(AddOnLevels).ToArray()
            : campaign.Where(map => File.Exists(Path.Combine(levels, map))).ToArray();
    }
}
