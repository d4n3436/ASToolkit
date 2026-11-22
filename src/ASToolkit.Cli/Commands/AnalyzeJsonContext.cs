using System.Text.Json.Serialization;
using ASToolkit.Cli.Analysis;

namespace ASToolkit.Cli.Commands;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RouteReport[]))]
[JsonSerializable(typeof(WalkReport[]))]
internal sealed partial class AnalyzeJsonContext : JsonSerializerContext;
