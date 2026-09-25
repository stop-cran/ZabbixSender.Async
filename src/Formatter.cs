using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// The default <see cref="IFormatter"/>: formats requests and parses responses of the Zabbix sender protocol.
    /// Requests are sent uncompressed as camelCase JSON. Responses may use zlib compression and the large packet
    /// header. Packets are limited to 1 GB.
    /// See https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/zabbix_sender
    /// and https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/header_datalen.
    /// </summary>
    public class Formatter : IFormatter
    {
        private const byte ZabbixProtocolFlag = 0x01;
        private const byte CompressionFlag = 0x02;
        private const byte LargePacketFlag = 0x04;
        private const int ProtocolLength = 4;
        private const int FlagsLength = 1;
        private const int HeaderLength = ProtocolLength + FlagsLength + 2 * sizeof(int);
        private const int LargePacketHeaderLength = ProtocolLength + FlagsLength + 2 * sizeof(long);
        private const int MaxPacketLength = 1 << 30;
        // Buffers start at this size and grow only as data actually arrives, so a header that
        // declares a huge length cannot force a huge allocation up front.
        private const int InitialPayloadBufferLength = 4096;

        private static ReadOnlySpan<byte> Protocol => "ZBXD"u8;

        private readonly int bufferSize;
        private readonly JsonSerializerOptions _settings;

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class with the default JSON settings:
        /// camelCase property names, null properties omitted. They use source-generated serialization metadata,
        /// so they work in trimmed and Native AOT applications.
        /// </summary>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(int bufferSize = 1024) :
            this(ZabbixJsonContext.Default.Options, bufferSize)
        { }

        /// <summary>
        /// Initializes a new instance of the ZabbixSender.Async.Formatter class.
        /// </summary>
        /// <param name="settings">Custom JSON serialization settings. They replace the defaults, so keep
        /// <c>PropertyNamingPolicy = JsonNamingPolicy.CamelCase</c>: without it, Zabbix closes the connection and
        /// <see cref="ISender.Send(IEnumerable{SendData}, CancellationToken)"/> throws <see cref="ProtocolException"/>.
        /// Also keep <c>DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull</c>: without it,
        /// <c>"clock": null</c> is written for values without <see cref="SendData.Clock"/>, and Zabbix does not store
        /// them: they count as failed, or, when every value in the request has it, are not counted at all. The
        /// settings are copied. The built-in source-generated metadata for
        /// <see cref="ZabbixDataMessage"/> and <see cref="SenderResponse"/> is added after any
        /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> they have, so they work in trimmed and Native AOT
        /// applications too; a resolver of their own takes precedence. <see langword="null"/> means the default
        /// settings.</param>
        /// <param name="bufferSize">Stream buffer size.</param>
        public Formatter(JsonSerializerOptions settings, int bufferSize = 1024)
        {
            this.bufferSize = bufferSize;
            this._settings = WithZabbixMetadata(settings);
        }

        // JsonSerializerOptions.GetTypeInfo, unlike JsonSerializer.Serialize(value, options), does not fall back to
        // reflection when the options have no resolver; so options that worked with 1.3 would throw without this.
        private static JsonSerializerOptions WithZabbixMetadata(JsonSerializerOptions settings)
        {
            if (settings is null || ReferenceEquals(settings, ZabbixJsonContext.Default.Options))
                return ZabbixJsonContext.Default.Options;

            var options = new JsonSerializerOptions(settings);

            options.TypeInfoResolver = options.TypeInfoResolver is null
                ? ZabbixJsonContext.Default
                : JsonTypeInfoResolver.Combine(options.TypeInfoResolver, ZabbixJsonContext.Default);

            return options;
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
                JsonSerializer.Serialize(
                    ms, new ZabbixDataMessage("sender data", data, DateTimeOffset.UtcNow), GetTypeInfo<ZabbixDataMessage>());

                stream.Write(CreateHeader(ms.Length));

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
                    ms, new ZabbixDataMessage("sender data", data, DateTimeOffset.UtcNow), GetTypeInfo<ZabbixDataMessage>(),
                    cancellationToken);

                await stream.WriteAsync(CreateHeader(ms.Length), cancellationToken);

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
                var header = new byte[LargePacketHeaderLength];

                stream.ReadExactly(header, 0, HeaderLength);

                var headerLength = GetHeaderLength(header);

                if (headerLength > HeaderLength)
                    stream.ReadExactly(header, HeaderLength, headerLength - HeaderLength);

                var packet = ParseHeader(header);
                var payload = ReadExactlyGrowing(stream, packet.DataLength);

                return Deserialize(payload, packet);
            }
            catch (EndOfStreamException ex)
            {
                throw new ProtocolException("the response is truncated", ex);
            }
        }

        /// <summary>
        /// Read Zabbix server response from given stream.
        /// </summary>
        /// <param name="stream">A stream to read from.</param>
        /// <param name="cancellationToken">CancellationToken for the read operation.</param>
        /// <returns>The parsed response.</returns>
        /// <exception cref="ProtocolException">The response is truncated or malformed.</exception>
        public async Task<SenderResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                var header = new byte[LargePacketHeaderLength];

                await stream.ReadExactlyAsync(header, 0, HeaderLength, cancellationToken);

                var headerLength = GetHeaderLength(header);

                if (headerLength > HeaderLength)
                    await stream.ReadExactlyAsync(header, HeaderLength, headerLength - HeaderLength,
                        cancellationToken);

                var packet = ParseHeader(header);
                var payload = await ReadExactlyGrowingAsync(stream, packet.DataLength, cancellationToken);

                return Deserialize(payload, packet);
            }
            catch (EndOfStreamException ex)
            {
                throw new ProtocolException("the response is truncated", ex);
            }
        }

        private static byte[] CreateHeader(long dataLength)
        {
            if (dataLength > MaxPacketLength)
                throw new ProtocolException("the request exceeds the 1 GB packet size limit");

            var header = new byte[HeaderLength];
            var span = header.AsSpan();

            Protocol.CopyTo(span);
            span[ProtocolLength] = ZabbixProtocolFlag;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(ProtocolLength + FlagsLength), (int)dataLength);
            // The reserved field is left zeroed since the request is not compressed.

            return header;
        }

        private static int GetHeaderLength(ReadOnlySpan<byte> header)
        {
            var flags = header[ProtocolLength];

            if (!header.StartsWith(Protocol) ||
                (flags & ZabbixProtocolFlag) == 0 ||
                (flags & ~(ZabbixProtocolFlag | CompressionFlag | LargePacketFlag)) != 0)
                throw new ProtocolException("the response has an incorrect header");

            return (flags & LargePacketFlag) == 0 ? HeaderLength : LargePacketHeaderLength;
        }

        private static PacketInfo ParseHeader(ReadOnlySpan<byte> header)
        {
            var flags = header[ProtocolLength];
            var lengths = header.Slice(ProtocolLength + FlagsLength);
            var isCompressed = (flags & CompressionFlag) != 0;
            ulong dataLength, reserved;

            if ((flags & LargePacketFlag) == 0)
            {
                dataLength = BinaryPrimitives.ReadUInt32LittleEndian(lengths);
                reserved = BinaryPrimitives.ReadUInt32LittleEndian(lengths.Slice(sizeof(int)));
            }
            else
            {
                dataLength = BinaryPrimitives.ReadUInt64LittleEndian(lengths);
                reserved = BinaryPrimitives.ReadUInt64LittleEndian(lengths.Slice(sizeof(long)));
            }

            if (dataLength > MaxPacketLength || (isCompressed && reserved > MaxPacketLength))
                throw new ProtocolException("the response exceeds the 1 GB packet size limit");

            return new PacketInfo((int)dataLength, isCompressed ? (int)reserved : null);
        }

        private SenderResponse Deserialize(byte[] payload, PacketInfo packet)
        {
            try
            {
                return JsonSerializer.Deserialize(
                    packet.UncompressedLength is { } uncompressedLength
                        ? Decompress(payload, uncompressedLength)
                        : payload,
                    GetTypeInfo<SenderResponse>()) ?? throw new ProtocolException("invalid response format");
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("invalid response format", ex);
            }
        }

        private JsonTypeInfo<T> GetTypeInfo<T>() =>
            (JsonTypeInfo<T>)_settings.GetTypeInfo(typeof(T));

        private static byte[] Decompress(byte[] payload, int uncompressedLength)
        {
            try
            {
                using (var zlib = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress))
                {
                    var result = ReadExactlyGrowing(zlib, uncompressedLength);

                    if (zlib.ReadByte() != -1)
                        throw new ProtocolException("the uncompressed response is longer than specified in the header");

                    return result;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                throw new ProtocolException("the response has invalid compressed data", ex);
            }
        }

        private static byte[] ReadExactlyGrowing(Stream stream, int length)
        {
            var buffer = new byte[Math.Min(length, InitialPayloadBufferLength)];
            var filled = 0;

            while (true)
            {
                stream.ReadExactly(buffer, filled, buffer.Length - filled);
                filled = buffer.Length;

                if (filled == length)
                    return buffer;

                Array.Resize(ref buffer, (int)Math.Min(length, 2L * buffer.Length));
            }
        }

        private static async Task<byte[]> ReadExactlyGrowingAsync(Stream stream, int length,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[Math.Min(length, InitialPayloadBufferLength)];
            var filled = 0;

            while (true)
            {
                await stream.ReadExactlyAsync(buffer, filled, buffer.Length - filled, cancellationToken);
                filled = buffer.Length;

                if (filled == length)
                    return buffer;

                Array.Resize(ref buffer, (int)Math.Min(length, 2L * buffer.Length));
            }
        }

        private readonly record struct PacketInfo(int DataLength, int? UncompressedLength);
    }
}