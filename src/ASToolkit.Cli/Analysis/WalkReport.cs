namespace ASToolkit.Cli.Analysis;

internal sealed record WalkReport(string Map, int WalkedPixels, int UnreachedSteps, string[] Steps, int Markers);
