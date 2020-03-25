using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZabbixSender.Async
{
    /// <summary>
    /// A structured view of Zabbix server response.
    /// </summary>
    public class SenderResponseInfo
    {
        private readonly string info;

        /// <summary>
        /// Create a structured view of Zabbix server response by given string.
        /// </summary>
        /// <param name="info">An original info string.</param>
        public SenderResponseInfo(string info)
        {
            this.info = info;

            var match = Regex.Match(info, @"processed: (\d+); failed: (\d+); total: (\d+); seconds spent: (\d+\.\d+)");

            if (!match.Success)
                throw new ProtocolException($"Info field has an unexpected format: \"{info}\"");

            try
            {
                Processed = Convert.ToInt32(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Failed = Convert.ToInt32(match.Groups[2].Value, CultureInfo.InvariantCulture);
                Total = Convert.ToInt32(match.Groups[3].Value, CultureInfo.InvariantCulture);
                TimeSpent = TimeSpan.FromSeconds(Convert.ToDouble(match.Groups[4].Value, CultureInfo.InvariantCulture));
            }
            catch (FormatException ex)
            {
                throw new ProtocolException($"Info field has an unexpected format: \"{info}\"", ex);
            }
        }

        /// <summary>
        /// Amount of successfully processed trapper items.
        /// </summary>
        public int Processed { get; }
        /// <summary>
        /// Amount of trapper items which have failed to process.
        /// </summary>
        public int Failed { get; }
        /// <summary>
        /// Total amount of trapper items processed.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Time spent to process the request.
        /// </summary>
        public TimeSpan TimeSpent { get; }

        /// <summary>
        /// Shows the original info string.
        /// </summary>
        public override string ToString() => info;
    }
}