using ASToolkit.Engine;
using ASToolkit.Formats;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectVidCommand : Command<InspectVidSettings>
{
    private const int DepthShift = 3;
    private const int DepthBias = 0x80;

    public override int Execute(CommandContext context, InspectVidSettings settings, CancellationToken cancellationToken)
    {
        string? path = File.Exists(settings.File) ? settings.File : GamePath.Resolve(settings.Game, settings.File);
        if (path is null)
        {
            AnsiConsole.MarkupLine($"[red]Could not find file '{Markup.Escape(settings.File)}'.[/]");
            return 1;
        }

        var vid = new VidResource(File.ReadAllBytes(path));
        AnsiConsole.MarkupLine($"type {vid.Type}");
        AnsiConsole.MarkupLine($"format {vid.Format}, mode {vid.MeshType}, blend {vid.BlendProcedure}, speed {vid.Speed}");
        AnsiConsole.MarkupLine($"size {vid.Size.Width} x {vid.Size.Height}, {vid.FrameCount} frames");
        if (settings.Frame < 0 || settings.Frame >= Math.Max(1, (int)vid.FrameCount))
        {
            AnsiConsole.MarkupLine($"[red]Could not find frame {settings.Frame}.[/]");
            return 1;
        }

        if (vid is { HasZBuffer: true, HasPalette: true } && vid.Type.HasFlag(VidType.Runs8Bit))
        {
            var histogram = new SortedDictionary<int, int>();
            foreach (var run in vid.GetRunFrame(settings.Frame))
            {
                for (int i = 0; i < run.Count; i++)
                {
                    int depth = (run.GetDepth(i) >> DepthShift) - DepthBias;
                    histogram[depth] = histogram.GetValueOrDefault(depth) + 1;
                }
            }

            AnsiConsole.MarkupLine($"[bold]depth histogram of frame {settings.Frame}[/]");
            foreach ((int depth, int count) in histogram)
            {
                AnsiConsole.MarkupLine($"  {depth,5} {count,7}");
            }
        }

        var points = SpritePartReader.ReadHeightPoints(vid, settings.Frame);
        AnsiConsole.MarkupLine($"[bold]{points.Length} height points of frame {settings.Frame}[/]");
        foreach (var group in points.GroupBy(point => point.Z).OrderBy(group => group.Key))
        {
            AnsiConsole.MarkupLine($"  height {group.Key,5}: {group.Count()} cells");
        }

        if (settings.Points)
        {
            foreach (var point in points.OrderBy(point => point.Y).ThenBy(point => point.X))
            {
                AnsiConsole.MarkupLine($"  {point.X,6} {point.Y,6} {point.Z,5}");
            }
        }

        return 0;
    }
}
