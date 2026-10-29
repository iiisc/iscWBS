using System.Text.Json;
using System.Text.Json.Serialization;

namespace iscWBS.Core.Data;

/// <summary>
/// Source-generated JSON metadata: the Native AOT release has no reflection-based serializer,
/// so a type must be listed here before it is stored in settings.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(bool?))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
