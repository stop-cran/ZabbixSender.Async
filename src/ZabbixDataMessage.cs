using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZabbixSender.Async;

public record ZabbixDataMessage(string Request, IEnumerable<SendData> Data, DateTimeOffset Clock)
{
    public override string ToString()
    {
        return $"{{ Request = {Request}, Data = {Data}, Clock = {Clock} }}";
    }

    [JsonConverter(typeof(JsonUnixDateTime))]
    public DateTimeOffset Clock { get; init; } = Clock;
}