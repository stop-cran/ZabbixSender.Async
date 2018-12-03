namespace ZabbixSender.Async
{
    /// <summary>
    /// Represents a response from Zabbix on the data sent.
    /// </summary>
    public class SenderResponse
    {
        public string Response { get; set; }
        public string Info { get; set; }
        public bool IsSuccess => Response == "success";

        public SenderResponseInfo ParseInfo() =>
            new SenderResponseInfo(Info);
    }
}
