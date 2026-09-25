using NUnit.Framework;
using Shouldly;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async.Tests
{
    [TestFixture]
    public class FormatterTests
    {
        private const string SuccessResponse =
            "{\"response\":\"success\",\"info\":\"processed: 1; failed: 0; total: 1; seconds spent: 0.000055\"}";

        [Test]
        public void RequestShouldStartWithZabbixHeader()
        {
            using (var stream = new MemoryStream())
            {
                new Formatter().WriteRequest(stream, new[] { new SendData() });
                Encoding.ASCII.GetString(stream.ToArray()).ShouldStartWith("ZBXD");
            }
        }

        [Test]
        public async Task RequestShouldStartWithZabbixHeaderAsync()
        {
            using (var stream = new MemoryStream())
            {
                await new Formatter().WriteRequestAsync(stream, new[] { new SendData() });
                Encoding.ASCII.GetString(stream.ToArray()).ShouldStartWith("ZBXD");
            }
        }

        [Test]
        public async Task RequestHeaderShouldContainLittleEndianDataLength([Values] bool useAsync)
        {
            var request = await Write(useAsync, new SendData { Host = "host1", Key = "key1", Value = "1" });

            request.AsSpan(0, 5).ToArray().ShouldBe(new byte[] { 0x5A, 0x42, 0x58, 0x44, 0x01 });
            BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(5, 4)).ShouldBe(request.Length - 13);
            BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(9, 4)).ShouldBe(0);
        }

        [Test]
        public void RequestShouldEndWithJson()
        {
            using (var stream = new MemoryStream())
            {
                new Formatter().WriteRequest(stream, new[]
                {
                    new SendData
                    {
                         Host = "host1",
                         Key = "key1"
                    },
                    new SendData
                    {
                         Host = "host2",
                         Key = "key2",
                         Value = "value2",
                         Clock = new System.DateTime(2018, 12, 18)
                    },
                });

                var result = Encoding.ASCII.GetString(stream.ToArray());

                using var json = JsonDocument.Parse(result.Substring(result.IndexOf('{')));

                json.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
            }
        }

        [Test]
        public async Task RequestShouldEndWithJsonAsync()
        {
            using (var stream = new MemoryStream())
            {
                await new Formatter().WriteRequestAsync(stream, new[]
                {
                    new SendData
                    {
                         Host = "host1",
                         Key = "key1"
                    },
                    new SendData
                    {
                         Host = "host2",
                         Key = "key2",
                         Value = "value2",
                         Clock = new System.DateTime(2018, 12, 18)
                    },
                });

                var result = Encoding.ASCII.GetString(stream.ToArray());

                using var json = JsonDocument.Parse(result.Substring(result.IndexOf('{')));

                json.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
            }
        }

        [Test]
        public async Task RequestShouldFollowSenderProtocol([Values] bool useAsync)
        {
            var clock = new DateTimeOffset(2018, 12, 18, 0, 0, 0, TimeSpan.Zero);
            var request = await Write(useAsync,
                new SendData { Host = "host1", Key = "key1", Value = "1" },
                new SendData { Host = "host2", Key = "key2", Value = "value2", Clock = clock });

            using var json = JsonDocument.Parse(request.AsMemory(13));
            var root = json.RootElement;

            root.GetProperty("request").GetString().ShouldBe("sender data");
            root.GetProperty("clock").ValueKind.ShouldBe(JsonValueKind.Number);

            var data = root.GetProperty("data").EnumerateArray().ToArray();

            data.Length.ShouldBe(2);
            data[0].GetProperty("host").GetString().ShouldBe("host1");
            data[0].GetProperty("key").GetString().ShouldBe("key1");
            data[0].GetProperty("value").GetString().ShouldBe("1");
            data[0].TryGetProperty("clock", out _).ShouldBeFalse();
            data[1].GetProperty("clock").GetInt64().ShouldBe(clock.ToUnixTimeSeconds());
        }

        [Test]
        public void ShouldCheckResponseHeader()
        {
            using (var stream = new MemoryStream())
                Should.Throw<ProtocolException>(() => new Formatter().ReadResponse(stream));
        }

        [Test]
        public async Task ShouldCheckResponseHeaderAsync()
        {
            using (var stream = new MemoryStream())
                await Should.ThrowAsync<ProtocolException>(() => new Formatter().ReadResponseAsync(stream));
        }

        [Test]
        public async Task ShouldRejectIncorrectProtocol([Values] bool useAsync)
        {
            var packet = Packet(0x01, SuccessResponse);

            packet[3] = (byte)'E';

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));
        }

        [Test]
        public async Task ShouldRejectIncorrectFlags([Values(0x00, 0x02, 0x08, 0x81)] byte flags,
            [Values] bool useAsync)
        {
            await Should.ThrowAsync<ProtocolException>(
                () => Read(useAsync, new MemoryStream(Packet(flags, SuccessResponse))));
        }

        [Test]
        public void ShouldRethrowJsonError()
        {
            using (var stream = new MemoryStream(Packet(0x01, "[ \"invalid\" ]")))
                Should.Throw<ProtocolException>(() => new Formatter().ReadResponse(stream));
        }

        [Test]
        public async Task ShouldRethrowJsonErrorAsync()
        {
            using (var stream = new MemoryStream(Packet(0x01, "[ \"invalid\" ]")))
                await Should.ThrowAsync<ProtocolException>(() => new Formatter().ReadResponseAsync(stream));
        }

        [Test]
        public async Task ShouldReadResponse([Values] bool useAsync)
        {
            var response = await Read(useAsync, new MemoryStream(Packet(0x01, SuccessResponse)));

            ShouldBeSuccess(response);
        }

        [Test]
        public async Task ShouldReadFailedResponse([Values] bool useAsync)
        {
            var response = await Read(useAsync, new MemoryStream(Packet(0x01,
                "{\"response\":\"failed\",\"info\":\"processed: 0; failed: 1; total: 1; seconds spent: 0.000030\"}")));

            response.IsSuccess.ShouldBeFalse();
            response.ParseInfo().Failed.ShouldBe(1);
        }

        [Test]
        public async Task ShouldReadResponseDeliveredInChunks([Values] bool useAsync)
        {
            var response = await Read(useAsync, new OneByteAtATimeStream(Packet(0x01, SuccessResponse)));

            ShouldBeSuccess(response);
        }

        [Test]
        public async Task ShouldReadNoMoreThanDataLength([Values] bool useAsync)
        {
            var packet = Packet(0x01, SuccessResponse);
            var stream = new MemoryStream(packet.Concat(Encoding.ASCII.GetBytes("trailing garbage")).ToArray());

            var response = await Read(useAsync, stream);

            ShouldBeSuccess(response);
            stream.Position.ShouldBe(packet.Length);
        }

        [Test]
        public async Task ShouldReadCompressedResponse([Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(SuccessResponse);
            var compressed = Compress(plain);

            var response = await Read(useAsync, new MemoryStream(Packet(0x03, compressed, reserved: plain.Length)));

            ShouldBeSuccess(response);
        }

        [Test]
        public async Task ShouldReadLargePacketResponse([Values(0x05, 0x07)] byte flags, [Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(SuccessResponse);
            var packet = (flags & 0x02) == 0
                ? Packet(flags, plain)
                : Packet(flags, Compress(plain), reserved: plain.Length);

            var response = await Read(useAsync, new MemoryStream(packet));

            ShouldBeSuccess(response);
        }

        [Test]
        public async Task ShouldRejectTruncatedResponse([Values] bool useAsync)
        {
            var packet = Packet(0x01, SuccessResponse);

            await Should.ThrowAsync<ProtocolException>(
                () => Read(useAsync, new MemoryStream(packet, 0, packet.Length - 1)));
        }

        [Test]
        public async Task ShouldRejectTruncatedHeader([Values] bool useAsync)
        {
            await Should.ThrowAsync<ProtocolException>(
                () => Read(useAsync, new MemoryStream(Packet(0x01, SuccessResponse), 0, 10)));
        }

        [Test]
        public async Task ShouldRejectResponseOverPacketSizeLimit([Values(0x01, 0x05)] byte flags,
            [Values] bool useAsync)
        {
            var packet = Packet(flags, SuccessResponse, dataLength: (1L << 30) + 1);

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));
        }

        [Test]
        public async Task ShouldRejectCompressedResponseOverPacketSizeLimit([Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(SuccessResponse);
            var packet = Packet(0x03, Compress(plain), reserved: (1L << 30) + 1);

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));
        }

        [Test]
        public async Task ShouldRejectInvalidCompressedData([Values] bool useAsync)
        {
            var packet = Packet(0x03, Encoding.ASCII.GetBytes("not zlib data"), reserved: 100);

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));
        }

        [Test]
        public async Task ShouldRejectMismatchedUncompressedLength([Values(-1, 1)] int delta, [Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(SuccessResponse);
            var packet = Packet(0x03, Compress(plain), reserved: plain.Length + delta);

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));
        }

        [Test]
        public async Task ShouldReadResponseLargerThanInitialBuffer([Values] bool compressed, [Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(new string(' ', 10_000) + SuccessResponse);
            var packet = compressed
                ? Packet(0x03, Compress(plain), reserved: plain.Length)
                : Packet(0x01, plain);

            ShouldBeSuccess(await Read(useAsync, new OneByteAtATimeStream(packet)));
        }

        [Test]
        public async Task ShouldNotAllocateDeclaredLengthBeforeDataArrives([Values] bool compressed,
            [Values] bool useAsync)
        {
            var plain = Encoding.UTF8.GetBytes(SuccessResponse);
            var packet = compressed
                ? Packet(0x03, Compress(plain), reserved: 1L << 30)
                : Packet(0x01, plain, dataLength: 1L << 30);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

            await Should.ThrowAsync<ProtocolException>(() => Read(useAsync, new MemoryStream(packet)));

            (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore).ShouldBeLessThan(64L << 20);
        }

        private static void ShouldBeSuccess(SenderResponse response)
        {
            response.IsSuccess.ShouldBeTrue();

            var info = response.ParseInfo();

            info.Processed.ShouldBe(1);
            info.Failed.ShouldBe(0);
            info.Total.ShouldBe(1);
            info.TimeSpent.ShouldBe(TimeSpan.FromSeconds(0.000055));
        }

        private static async Task<byte[]> Write(bool useAsync, params SendData[] data)
        {
            using var stream = new MemoryStream();

            if (useAsync)
                await new Formatter().WriteRequestAsync(stream, data);
            else
                new Formatter().WriteRequest(stream, data);

            return stream.ToArray();
        }

        private static async Task<SenderResponse> Read(bool useAsync, Stream stream) =>
            useAsync
                ? await new Formatter().ReadResponseAsync(stream)
                : new Formatter().ReadResponse(stream);

        private static byte[] Packet(byte flags, string payload, long? dataLength = null) =>
            Packet(flags, Encoding.UTF8.GetBytes(payload), dataLength);

        private static byte[] Packet(byte flags, byte[] payload, long? dataLength = null, long reserved = 0)
        {
            var fieldSize = (flags & 0x04) == 0 ? sizeof(int) : sizeof(long);
            var header = new byte[5 + 2 * fieldSize];

            Encoding.ASCII.GetBytes("ZBXD").CopyTo(header, 0);
            header[4] = flags;

            if (fieldSize == sizeof(int))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), (uint)(dataLength ?? payload.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(9), (uint)reserved);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(5), (ulong)(dataLength ?? payload.Length));
                BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(13), (ulong)reserved);
            }

            return header.Concat(payload).ToArray();
        }

        private static byte[] Compress(byte[] data)
        {
            using var result = new MemoryStream();

            using (var zlib = new ZLibStream(result, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data);

            return result.ToArray();
        }

        private sealed class OneByteAtATimeStream : MemoryStream
        {
            public OneByteAtATimeStream(byte[] data) : base(data) { }

            public override int Read(byte[] buffer, int offset, int count) =>
                base.Read(buffer, offset, Math.Min(count, 1));

            public override int Read(Span<byte> buffer) =>
                base.Read(buffer.Slice(0, Math.Min(buffer.Length, 1)));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
                CancellationToken cancellationToken) =>
                base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, 1)), cancellationToken);
        }
    }
}