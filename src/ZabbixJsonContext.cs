using System.Text.Json.Serialization;

namespace ZabbixSender.Async;

// Source-generated metadata, so serialization works in trimmed and Native AOT applications.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ZabbixDataMessage))]
[JsonSerializable(typeof(SenderResponse))]
internal partial class ZabbixJsonContext : JsonSerializerContext;