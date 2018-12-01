using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    public class Sender
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly byte[] ZabbixHeader = new byte[] { 0x5a, 0x42, 0x58, 0x44, 0x01 };

        /// <summary>
        /// Use this class to send data to Zabbix. Be sure, that all used host names and items are configured.
        /// All the items should have type "Zabbix trapper" to support Zabbix sender data flow.
        /// </summary>
        /// <param name="zabbixServer">Host name or IP address of Zabbix server or proxy.</param>
        /// <param name="port">Zabbix server port.</param>
        /// <param name="timeout">Send data request timeout in milliseconds.</param>
        /// <param name="bufferSize">Stream writer buffer size.</param>
        public Sender(string zabbixServer, int port = 10051, int timeout = 500, int bufferSize = 1024)
        {
            ZabbixServer = zabbixServer;
            Port = port;
            Timeout = timeout;
            BufferSize = bufferSize;
        }

        /// <summary>
        /// Host name or IP address of Zabbix server or proxy.
        /// </summary>
        public string ZabbixServer { get; }

        /// <summary>
        /// Zabbix server port
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Send data request timeout in milliseconds.
        /// </summary>
        public int Timeout { get; }

        /// <summary>
        /// Stream writer buffer size.
        /// </summary>
        public int BufferSize { get; }

        public Task<SenderResponse> Send(string host, string key, string value) =>
            Send(host, key, value, CancellationToken.None);

        public Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken) =>
            Send(new[]
            {
                new SendData
                {
                    Host = host,
                    Key = key,
                    Value = value
                }
            }, cancellationToken);

        public Task<SenderResponse> Send(params SendData[] data) =>
            Send(data, CancellationToken.None);

        public Task<SenderResponse> Send(IEnumerable<SendData> data) =>
            Send(data, CancellationToken.None);

        public async Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken)
        {
            using (var tcpClient = new TcpClient())
            {
                await tcpClient.ConnectAsync(ZabbixServer, Port);

                using (var networkStream = tcpClient.GetStream())
                {
                    var serializer = JsonSerializer.Create(new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });

                    using (var ms = new MemoryStream())
                    {
                        using (var writer = new StreamWriter(ms, Encoding.ASCII, BufferSize, true))
                        using (var jsonWriter = new JsonTextWriter(writer))
                            serializer.Serialize(
                                jsonWriter,
                                new
                                {
                                    request = "sender data",
                                    data,
                                    clock = GetCurrentUnixTime()
                                });

                        await networkStream.WriteAsync(ZabbixHeader, 0, 5, cancellationToken);
                        await networkStream.WriteAsync(BitConverter.GetBytes(ms.Length), 0, 8, cancellationToken);

                        ms.Seek(0, SeekOrigin.Begin);
                        await ms.CopyToAsync(networkStream, BufferSize, cancellationToken);
                    }

                    networkStream.Flush();

                    for (int retry = 0; !networkStream.DataAvailable; retry += 50)
                    {
                        await Task.Delay(50, cancellationToken);

                        if (retry >= Timeout)
                            throw new TaskCanceledException();
                    }

                    var response = new byte[BufferSize];
                    var count = await networkStream.ReadAsync(response, 0, response.Length, cancellationToken);
                    var begin = Array.IndexOf(response, (byte)'{');

                    using (var ms = new MemoryStream(response, begin, count - begin))
                    {
                        using (var reader = new StreamReader(ms, Encoding.ASCII))
                        using (var jsonReader = new JsonTextReader(reader))
                            return serializer.Deserialize<SenderResponse>(jsonReader);
                    }
                }
            }
        }

        private static long GetCurrentUnixTime() =>
            (long)(DateTime.UtcNow - UnixEpoch).TotalSeconds;
    }
}
