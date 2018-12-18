using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    public interface ISender
    {
        Task<SenderResponse> Send(params SendData[] data);

        Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default);

        Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken = default);
    }
}
