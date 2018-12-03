using System;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Represents a data frame to be sent to Zabbix.
    /// </summary>
    public class SendData
    {
        /// <summary>
        /// Host name to monitor. Should be configured in Zabbix.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// A key of the item to send.
        /// Should belong to the specified host and have "Zabbix sender" type.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// An item value.
        /// Should be formatted in a way to respect the configured "type of information" of the item.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// A timestamp for the provided value. Leave null for ongoing values.
        /// </summary>
        public DateTime? Clock { get; set; }
    }
}
