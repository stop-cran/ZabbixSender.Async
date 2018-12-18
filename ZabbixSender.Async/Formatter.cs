using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// An auxiliary class for Zabbix sender protocol 4.0 request and response formatting.
    /// See full specs at https://zabbix.org/wiki/Docs/protocols/zabbix_sender/4.0.
    /// </summary>
    public class Formatter : IFormatter
    {
        private static readonly byte[] ZabbixHeader = Encoding.ASCII.GetBytes("ZBXD\x01");

        private readonly int bufferSize;

        private readonly JsonSerializer serializer;

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class with custom JsonSerializerSettings.
        /// </summary>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(int bufferSize = 1024) :
            this(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Converters =
                {
                    new UnixDateTimeConverter()
                }
            }, bufferSize)
        { }

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class.
        /// </summary>
        /// <param name="settings">Custom Json serialization settings.</param>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(JsonSerializerSettings settings, int bufferSize = 1024)
        {
            serializer = JsonSerializer.Create(settings);
            this.bufferSize = bufferSize;
        }

        public void WriteRequest(Stream stream, IEnumerable<SendData> data)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new StreamWriter(ms, Encoding.ASCII, bufferSize, true))
                using (var jsonWriter = new JsonTextWriter(writer))
                    serializer.Serialize(
                        jsonWriter,
                        new
                        {
                            Request = "sender data",
                            Data = data,
                            Clock = DateTime.Now
                        });

                var lengthBytes = BitConverter.GetBytes(ms.Length);

                stream.Write(ZabbixHeader, 0, ZabbixHeader.Length);
                stream.Write(BitConverter.GetBytes(ms.Length), 0, lengthBytes.Length);

                ms.Seek(0, SeekOrigin.Begin);
                ms.CopyTo(stream, bufferSize);
            }
        }

        public async Task WriteRequestAsync(Stream stream, IEnumerable<SendData> data,
            CancellationToken cancellationToken = default)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new StreamWriter(ms, Encoding.ASCII, bufferSize, true))
                using (var jsonWriter = new JsonTextWriter(writer))
                    serializer.Serialize(
                        jsonWriter,
                        new
                        {
                            Request = "sender data",
                            Data = data,
                            Clock = DateTime.Now
                        });

                var lengthBytes = BitConverter.GetBytes(ms.Length);

                await stream.WriteAsync(ZabbixHeader, 0, ZabbixHeader.Length,
                    cancellationToken);
                await stream.WriteAsync(BitConverter.GetBytes(ms.Length), 0,
                    lengthBytes.Length, cancellationToken);

                ms.Seek(0, SeekOrigin.Begin);
                await ms.CopyToAsync(stream, bufferSize, cancellationToken);
            }
        }

        public SenderResponse ReadResponse(Stream stream)
        {
            try
            {
                var buffer = new byte[13];

                stream.Read(buffer, 0, buffer.Length); // skip the length

                if (ZabbixHeader.Zip(buffer, (x, y) => x != y).Any(b => b))
                    throw new ProtocolException("the response has an incorrect header");

                using (var reader = new StreamReader(stream, Encoding.ASCII))
                using (var jsonReader = new JsonTextReader(reader))
                    return serializer.Deserialize<SenderResponse>(jsonReader);
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("invalid response format", ex);
            }
        }

        public async Task<SenderResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                var buffer = new byte[13];

                await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken); // skip the length

                if (ZabbixHeader.Zip(buffer, (x, y) => x != y).Any(b => b))
                    throw new ProtocolException("the response has an incorrect header");

                using (var reader = new StreamReader(stream, Encoding.ASCII))
                using (var jsonReader = new JsonTextReader(reader))
                    return serializer.Deserialize<SenderResponse>(jsonReader);
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("invalid response format", ex);
            }
        }
    }
}
