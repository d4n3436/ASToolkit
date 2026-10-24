namespace ASToolkit.Cli.Analysis;

internal sealed record WalkReport(string Map, string? Image, int WalkedPixels, int UnreachedSteps, string[] Steps, int Markers);
