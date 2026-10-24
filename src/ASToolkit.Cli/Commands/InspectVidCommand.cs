using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
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
            AnsiConsole.MarkupLine($"[red]No file {Markup.Escape(settings.File)}.[/]");
            return 1;
        }

        var vid = new VidResource(File.ReadAllBytes(path));
        AnsiConsole.MarkupLine($"flags {vid.Flags}");
        AnsiConsole.MarkupLine($"format {vid.Format}, mode {vid.Mode}, blend {vid.Blend}, speed {vid.Speed}");
        AnsiConsole.MarkupLine($"size {vid.Width} x {vid.Height}, {vid.FrameCount} frames");
        if (settings.Frame < 0 || settings.Frame >= Math.Max(1, (int)vid.FrameCount))
        {
            AnsiConsole.MarkupLine($"[red]No frame {settings.Frame}.[/]");
            return 1;
        }

        if (vid is { HasDepth: true, HasPalette: true } && vid.Flags.HasFlag(VidFlags.Runs8Bit))
        {
            var histogram = new SortedDictionary<int, int>();
            foreach (var run in vid.GetRunFrame(settings.Frame))
            {
                for (int index = 0; index < run.Count; index++)
                {
                    int depth = (run.GetDepth(index) >> DepthShift) - DepthBias;
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
        AnsiConsole.MarkupLine($"[bold]{points.Count} height points of frame {settings.Frame}[/]");
        foreach (var group in points.GroupBy(point => point.Height).OrderBy(group => group.Key))
        {
            AnsiConsole.MarkupLine($"  height {group.Key,5}: {group.Count()} cells");
        }

        if (settings.Points)
        {
            foreach ((float x, float y, short height) in points.OrderBy(point => point.Y).ThenBy(point => point.X))
            {
                AnsiConsole.MarkupLine($"  {x,6} {y,6} {height,5}");
            }
        }

        return 0;
    }
}
