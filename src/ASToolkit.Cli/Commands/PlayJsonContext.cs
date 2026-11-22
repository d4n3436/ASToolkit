using System.Text.Json.Serialization;

namespace ASToolkit.Cli.Commands;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<PlayReport>))]
internal sealed partial class PlayJsonContext : JsonSerializerContext;
