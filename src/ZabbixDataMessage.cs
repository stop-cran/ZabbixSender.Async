using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZabbixSender.Async;

/// <summary>
/// A Zabbix sender protocol request message.
/// </summary>
/// <param name="Request">The request type, "sender data" for the sender protocol.</param>
/// <param name="Data">Data items to send.</param>
/// <param name="Clock">The request timestamp.</param>
public record ZabbixDataMessage(string Request, IEnumerable<SendData> Data, DateTimeOffset Clock)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{{ Request = {Request}, Data = {Data}, Clock = {Clock} }}";
    }

    /// <summary>
    /// The request timestamp, serialized as Unix time in seconds.
    /// </summary>
    [JsonConverter(typeof(JsonUnixDateTime))]
    public DateTimeOffset Clock { get; init; } = Clock;
}