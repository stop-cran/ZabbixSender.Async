namespace ZabbixSender.Async
{
    /// <summary>
    /// The Zabbix server's reply to a request.
    /// </summary>
    public class SenderResponse
    {
        /// <summary>
        /// "success" if the server processed the request, or "failed" if it rejected the request as a whole.
        /// A "success" response can still contain rejected values: see <see cref="ParseInfo"/>.
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// The processing summary, for example "processed: 1; failed: 0; total: 1; seconds spent: 0.000055".
        /// Use <see cref="ParseInfo"/> to get the counters.
        /// </summary>
        public string Info { get; set; }

        /// <summary>
        /// Whether <see cref="Response"/> is "success". It stays true when the server rejects some or all values:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="ParseInfo"/>.
        /// </summary>
        public bool IsSuccess => Response == "success";

        /// <summary>
        /// Parses <see cref="Info"/> into counters.
        /// </summary>
        /// <exception cref="ProtocolException"><see cref="Info"/> has an unexpected format, for example in a
        /// "failed" response.</exception>
        public SenderResponseInfo ParseInfo() =>
            new SenderResponseInfo(Info);
    }
}
