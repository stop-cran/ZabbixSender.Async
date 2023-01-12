using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// An auxiliary class for Zabbix sender protocol 4.4 request and response formatting.
    /// See full specs at https://zabbix.org/wiki/Docs/protocols/zabbix_sender/4.4.
    /// </summary>
    public class Formatter : IFormatter
    {
        // See docs: https://www.zabbix.com/documentation/4.4/manual/appendix/protocols/header_datalen
        private static readonly byte[] ZabbixHeader = Encoding.ASCII.GetBytes("ZBXD\x01");

        private readonly int bufferSize;
        private readonly JsonSerializerOptions _settings;

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class with custom JsonSerializerSettings.
        /// </summary>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(int bufferSize = 1024) :
            this(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IgnoreNullValues = true
            }, bufferSize)
        { }

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class.
        /// </summary>
        /// <param name="settings">Custom Json serialization settings.</param>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(JsonSerializerOptions settings, int bufferSize = 1024)
        {
            this.bufferSize = bufferSize;
            this._settings = settings;
        }

        /// <summary>
        /// Write provided data items to the request stream.
        /// </summary>
        /// <param name="stream">Request stream.</param>
        /// <param name="data">Data items to write.</param>
        public void WriteRequest(Stream stream, IEnumerable<SendData> data)
        {
            using (var ms = new MemoryStream())
            {
                JsonSerializer.SerializeAsync(
                        ms, new ZabbixDataMessage("sender data", data, DateTimeOffset.UtcNow), _settings).Wait();

                var lengthBytes = BitConverter.GetBytes(ms.Length);

                stream.Write(ZabbixHeader, 0, ZabbixHeader.Length);
                stream.Write(BitConverter.GetBytes(ms.Length), 0, lengthBytes.Length);

                ms.Seek(0, SeekOrigin.Begin);
                ms.CopyTo(stream, bufferSize);
            }
        }

        /// <summary>
        /// Write provided data items to the request stream.
        /// </summary>
        /// <param name="stream">A stream to write to.</param>
        /// <param name="data">Data items to write.</param>
        /// <param name="cancellationToken">A CancellationToken for the write operation.</param>
        public async Task WriteRequestAsync(Stream stream, IEnumerable<SendData> data,
            CancellationToken cancellationToken = default)
        {
            using (var ms = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(
                    ms, new ZabbixDataMessage("sender data", data, DateTimeOffset.UtcNow), _settings);

                var lengthBytes = BitConverter.GetBytes(ms.Length);

                await stream.WriteAsync(ZabbixHeader, 0, ZabbixHeader.Length,
                    cancellationToken);
                await stream.WriteAsync(BitConverter.GetBytes(ms.Length), 0,
                    lengthBytes.Length, cancellationToken);

                ms.Seek(0, SeekOrigin.Begin);
                await ms.CopyToAsync(stream, bufferSize, cancellationToken);
            }
        }

        /// <summary>
        /// Read Zabbix server response from given stream.
        /// </summary>
        /// <param name="stream">A stream to read from.</param>
        public SenderResponse ReadResponse(Stream stream)
        {
            try
            {
                var buffer = new byte[13];

                stream.Read(buffer, 0, buffer.Length); // skip the length

                if (ZabbixHeader.Zip(buffer, (x, y) => x != y).Any(b => b))
                    throw new ProtocolException("the response has an incorrect header");

                return JsonSerializer.DeserializeAsync<SenderResponse>(stream).Result;
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("invalid response format", ex);
            }
        }

        /// <summary>
        /// Read Zabbix server response from given stream.
        /// </summary>
        /// <param name="stream">A stream to read from.</param>
        /// <param name="cancellationToken">CancellationToken for the read operation.</param>
        /// <returns></returns>
        public async Task<SenderResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                var buffer = new byte[13];

                await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken); // skip the length

                if (ZabbixHeader.Zip(buffer, (x, y) => x != y).Any(b => b))
                    throw new ProtocolException("the response has an incorrect header");
                
                return await JsonSerializer.DeserializeAsync<SenderResponse>(stream);
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("invalid response format", ex);
            }
        }
    }
}
