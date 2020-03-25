namespace ZabbixSender.Async
{
    /// <summary>
    /// Represents a response from Zabbix server on the data sent.
    /// </summary>
    public class SenderResponse
    {
        /// <summary>
        /// "success" in case of the request has succeeded.
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// Supplementary information. Can be parsed by ParseInfo() mwthod.
        /// </summary>
        public string Info { get; set; }

        /// <summary>
        /// Whether the request has succeeded.
        /// </summary>
        public bool IsSuccess => Response == "success";

        /// <summary>
        /// Supplementary information in a structured view.
        /// </summary>
        public SenderResponseInfo ParseInfo() =>
            new SenderResponseInfo(Info);
    }
}
