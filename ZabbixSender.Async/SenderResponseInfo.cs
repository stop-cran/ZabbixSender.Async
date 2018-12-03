using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZabbixSender.Async
{
    public class SenderResponseInfo
    {
        private readonly string info;

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
                SecondsSpent = Convert.ToDouble(match.Groups[4].Value, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new ProtocolException($"Info field has an unexpected format: \"{info}\"", ex);
            }
        }

        public int Processed { get; }
        public int Failed { get; }
        public int Total { get; }
        public double SecondsSpent { get; }

        public override string ToString() => info;
    }
}