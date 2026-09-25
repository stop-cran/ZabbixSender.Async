using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZabbixSender.Async;

/// <summary>
/// Converts <see cref="DateTimeOffset"/> values to and from Unix time in seconds.
/// </summary>
public class JsonUnixDateTime : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var timestamp = reader.GetInt64();
        return DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }


    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset v,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(v.ToUnixTimeSeconds());
}