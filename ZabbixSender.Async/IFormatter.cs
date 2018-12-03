using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    public interface IFormatter
    {
        SenderResponse ReadResponse(Stream stream);
        Task<SenderResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken);
        void WriteRequest(Stream stream, IEnumerable<SendData> data);
        Task WriteRequestAsync(Stream stream, IEnumerable<SendData> data, CancellationToken cancellationToken);
    }
}