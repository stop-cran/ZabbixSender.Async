using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// A class to send data to Zabbix. Be sure, that all used host names and items are configured.
    /// All the items should have type "Zabbix trapper" to support Zabbix sender data flow.
    /// </summary>
    public class Sender : SenderSkeleton
    {
        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Sender class.
        /// </summary>
        /// <param name="zabbixServer">Host name or IP address of Zabbix server or proxy.</param>
        /// <param name="port">Zabbix server port.</param>
        /// <param name="timeout">Connect, send and receive timeout in milliseconds.</param>
        /// <param name="bufferSize">Buffer size for stream reading and writing.</param>
        public Sender(string zabbixServer, int port = 10051, int timeout = 500, int bufferSize = 1024)
            : base(CreateTcpClient(zabbixServer, port, timeout, bufferSize),
                  () => new Formatter(bufferSize))
        {
            ZabbixServer = zabbixServer;
            Port = port;
        }

        /// <summary>
        /// Host name or IP address of Zabbix server or proxy.
        /// </summary>
        public string ZabbixServer { get; }

        /// <summary>
        /// Zabbix server port
        /// </summary>
        public int Port { get; }

        private static Func<CancellationToken, Task<TcpClient>> CreateTcpClient(string zabbixServer, int port, int timeout, int bufferSize)
        {
            var tcpClient = new TcpClient
            {
                SendTimeout = timeout,
                ReceiveTimeout = timeout,
                SendBufferSize = bufferSize,
                ReceiveBufferSize = bufferSize
            };

            return async cancellationToken =>
            {
                await tcpClient.ConnectAsync(zabbixServer, port)
                    .WithTimeout(timeout, cancellationToken);

                return tcpClient;
            };
        }
    }
}
