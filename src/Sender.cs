using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Sends values to Zabbix trapper items over the Zabbix sender protocol, like the zabbix_sender utility.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <c>Send</c> call opens a new TCP connection to <see cref="ZabbixServer"/>:<see cref="Port"/>, sends one
    /// request with all the given values and reads the response. There is no connection pooling, so batch values
    /// into one call for throughput.
    /// </para>
    /// <para>
    /// The class is immutable and thread-safe. Create one instance per Zabbix server and share it, for example as a
    /// singleton <see cref="ISender"/> in a dependency injection container.
    /// </para>
    /// <para>
    /// Target items must be of type Zabbix trapper, or HTTP agent with trapping enabled. Values the server rejects
    /// don't cause exceptions: they are counted in <see cref="SenderResponseInfo.Failed"/>.
    /// </para>
    /// </remarks>
    public class Sender : SenderSkeleton, ISender
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Sender"/> class. No connection is opened until a value is sent.
        /// </summary>
        /// <param name="zabbixServer">The host name or IP address of the Zabbix server or proxy.</param>
        /// <param name="port">The trapper port of the Zabbix server or proxy.</param>
        /// <param name="timeout">
        /// The limit in milliseconds for connecting, and separately for waiting for the response.
        /// 0 or <see cref="Timeout.Infinite"/> disables the limit. Writing the request is limited only by the
        /// cancellation token.
        /// </param>
        /// <param name="bufferSize">The socket send and receive buffer size and the stream copy buffer size, in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is less than -1.</exception>
        public Sender(string zabbixServer, int port = 10051, int timeout = 500, int bufferSize = 1024)
            : base(CreateTcpClient(zabbixServer, port, timeout, bufferSize),
                  () => new Formatter(bufferSize))
        {
            ZabbixServer = zabbixServer;
            Port = port;
        }

        /// <summary>
        /// The host name or IP address of the Zabbix server or proxy.
        /// </summary>
        public string ZabbixServer { get; }

        /// <summary>
        /// The trapper port of the Zabbix server or proxy.
        /// </summary>
        public int Port { get; }

        private static Func<CancellationToken, Task<TcpClient>> CreateTcpClient(string zabbixServer, int port, int timeout, int bufferSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, Timeout.Infinite);

            return async cancellationToken =>
            {
                var tcpClient = new TcpClient
                {
                    SendTimeout = timeout,
                    ReceiveTimeout = timeout,
                    SendBufferSize = bufferSize,
                    ReceiveBufferSize = bufferSize
                };

                try
                {
                    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    if (timeout > 0)
                        timeoutSource.CancelAfter(timeout);

                    try
                    {
                        await tcpClient.ConnectAsync(zabbixServer, port, timeoutSource.Token);
                    }
                    catch (OperationCanceledException ex) when (
                        timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw CreateTimeoutException(
                            $"Could not connect to Zabbix server {zabbixServer}:{port} within {timeout} ms", ex);
                    }

                    return tcpClient;
                }
                catch
                {
                    tcpClient.Dispose();
                    throw;
                }
            };
        }
    }
}