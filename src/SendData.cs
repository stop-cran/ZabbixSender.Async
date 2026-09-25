using System;
using System.Net;
using System.Text.Json.Serialization;

namespace ZabbixSender.Async
{
    /// <summary>
    /// A single value for a Zabbix item.
    /// </summary>
    public class SendData
    {
        /// <summary>
        /// The technical name of the host in Zabbix ("Host name" in the frontend), not its visible name.
        /// The host must exist and be monitored.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// The key of an item on that host. The item must be of type "Zabbix trapper",
        /// or "HTTP agent" with trapping enabled.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The value as text, formatted to match the item's type of information. Format numbers with
        /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/>, for example "3.14". A value of the wrong type
        /// is still counted as processed, but the server discards it and marks the item as not supported.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// When the value was collected. It is sent as Unix time in whole seconds; fractions of a second are dropped.
        /// Leave it null to let the server use the time it receives the value.
        /// </summary>
        [JsonConverter(typeof(JsonUnixDateTime))]
        public DateTimeOffset? Clock { get; set; }
    }
}
