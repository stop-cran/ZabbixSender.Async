using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZabbixSender.Async
{
    /// <summary>
    /// The counters from <see cref="SenderResponse.Info"/>.
    /// </summary>
    public class SenderResponseInfo
    {
        private readonly string info;

        /// <summary>
        /// Parses the counters from a <see cref="SenderResponse.Info"/> string.
        /// </summary>
        /// <param name="info">The info string, for example "processed: 1; failed: 0; total: 1; seconds spent: 0.000055".</param>
        /// <exception cref="ProtocolException"><paramref name="info"/> has an unexpected format.</exception>
        public SenderResponseInfo(string info)
        {
            this.info = info;

            var match = Regex.Match(info ?? string.Empty, @"processed: (\d+); failed: (\d+); total: (\d+); seconds spent: (\d+\.\d+)");

            if (!match.Success)
                throw new ProtocolException($"Info field has an unexpected format: \"{info}\"");

            try
            {
                Processed = Convert.ToInt32(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Failed = Convert.ToInt32(match.Groups[2].Value, CultureInfo.InvariantCulture);
                Total = Convert.ToInt32(match.Groups[3].Value, CultureInfo.InvariantCulture);
                TimeSpent = TimeSpan.FromSeconds(Convert.ToDouble(match.Groups[4].Value, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new ProtocolException($"Info field has an unexpected format: \"{info}\"", ex);
            }
        }

        /// <summary>
        /// The number of values the server accepted. An accepted value is still discarded if it doesn't match
        /// the item's type of information.
        /// </summary>
        public int Processed { get; }

        /// <summary>
        /// The number of values the server rejected, for example because the host or item doesn't exist or is
        /// disabled, the item doesn't accept trapped values, or the sender's address isn't in the item's allowed hosts.
        /// </summary>
        public int Failed { get; }

        /// <summary>
        /// The number of values in the request.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// The time the server spent processing the request.
        /// </summary>
        public TimeSpan TimeSpent { get; }

        /// <summary>
        /// Returns the original info string.
        /// </summary>
        public override string ToString() => info;
    }
}