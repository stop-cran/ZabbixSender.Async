namespace ZabbixSender.Async
{
    public class SenderResponse
    {
        public string Response { get; set; }
        public string Info { get; set; }
        public bool IsSuccess => Response == "success";
    }
}