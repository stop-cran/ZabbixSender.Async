using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// A basic class for sending datato Zabbix, which allows custom setup for TcpClient and Formatter.
    /// </summary>
    public class SenderSkeleton : ISender
    {
        private readonly Func<CancellationToken, Task<TcpClient>> tcpClientFactory;
        private readonly Func<IFormatter> formatterFactory;

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.SenderSkeleton class.
        /// </summary>
        /// <param name="tcpClientFactory">An async factory delegate for TcpClient.</param>
        /// <param name="formatterFactory">A factory delegate for the data formatter.</param>
        public SenderSkeleton(
            Func<CancellationToken, Task<TcpClient>> tcpClientFactory,
            Func<IFormatter> formatterFactory)
        {
            this.tcpClientFactory = tcpClientFactory;
            this.formatterFactory = formatterFactory;
        }

        public Task<SenderResponse> Send(params SendData[] data) =>
            Send(data, CancellationToken.None);

        public Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken = default) =>
            Send(new[]
            {
                new SendData
                {
                    Host = host,
                    Key = key,
                    Value = value
                }
            }, cancellationToken);

        public async Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default)
        {
            using (var tcpClient = await tcpClientFactory(cancellationToken))
            using (var networkStream = tcpClient.GetStream())
            {
                var formatter = formatterFactory();

                await formatter.WriteRequestAsync(networkStream, data, cancellationToken);
                await networkStream.FlushAsync();
                await networkStream.WaitForDataAvailable(tcpClient.ReceiveTimeout, cancellationToken);

                return await formatter.ReadResponseAsync(networkStream, cancellationToken);
            }
        }
    }
}
