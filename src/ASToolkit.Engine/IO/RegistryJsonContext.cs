using System.Text.Json.Serialization;

namespace ASToolkit.Engine.IO;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
internal sealed partial class RegistryJsonContext : JsonSerializerContext;
