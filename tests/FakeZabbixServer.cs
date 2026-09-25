using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async.Tests
{
    /// <summary>
    /// A loopback TCP server that speaks just enough of the Zabbix sender protocol to test <see cref="Sender"/>
    /// without a real Zabbix server.
    /// </summary>
    internal sealed class FakeZabbixServer : IAsyncDisposable
    {
        public const string SuccessResponse =
            "{\"response\":\"success\",\"info\":\"processed: 1; failed: 0; total: 1; seconds spent: 0.000055\"}";

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Func<FakeZabbixServer, NetworkStream, CancellationToken, Task> handler;
        private readonly CancellationTokenSource stopping = new();
        private readonly ConcurrentBag<Task> connections = new();
        private readonly Task acceptLoop;
        private int connectionCount;

        public FakeZabbixServer(Func<FakeZabbixServer, NetworkStream, CancellationToken, Task> handler)
        {
            this.handler = handler;
            listener.Start();
            acceptLoop = AcceptLoop();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public int ConnectionCount => Volatile.Read(ref connectionCount);

        public ConcurrentQueue<JsonDocument> Requests { get; } = new();

        public ConcurrentQueue<Exception> Errors { get; } = new();

        /// <summary>Replies to every request with the given JSON payload.</summary>
        public static FakeZabbixServer Replying(string json = SuccessResponse) =>
            new(async (server, stream, ct) =>
            {
                await server.ReadRequest(stream, ct);
                await stream.WriteAsync(Packet(json), ct);
            });

        /// <summary>Reads the request and then keeps the connection open without replying.</summary>
        public static FakeZabbixServer Silent() =>
            new(async (server, stream, ct) =>
            {
                await server.ReadRequest(stream, ct);
                await Task.Delay(Timeout.Infinite, ct);
            });

        /// <summary>Replies with raw bytes, e.g. to imitate a non-Zabbix service listening on the port.</summary>
        public static FakeZabbixServer ReplyingRaw(byte[] response) =>
            new(async (server, stream, ct) =>
            {
                await server.ReadRequest(stream, ct);
                await stream.WriteAsync(response, ct);
            });

        public static byte[] Packet(string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var packet = new byte[13 + payload.Length];

            "ZBXD"u8.CopyTo(packet);
            packet[4] = 0x01;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5), payload.Length);
            payload.CopyTo(packet, 13);

            return packet;
        }

        public async Task<JsonDocument> ReadRequest(Stream stream, CancellationToken cancellationToken)
        {
            var header = new byte[13];
            await stream.ReadExactlyAsync(header, cancellationToken);

            if (!header.AsSpan(0, 4).SequenceEqual("ZBXD"u8) || header[4] != 0x01)
                throw new InvalidDataException("Unexpected request header: " + Convert.ToHexString(header));

            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5))];
            await stream.ReadExactlyAsync(payload, cancellationToken);

            var request = JsonDocument.Parse(payload);
            Requests.Enqueue(request);

            return request;
        }

        public async ValueTask DisposeAsync()
        {
            stopping.Cancel();
            listener.Stop();

            await acceptLoop;
            await Task.WhenAll(connections);

            foreach (var request in Requests)
                request.Dispose();

            stopping.Dispose();
        }

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(stopping.Token);
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                    return;
                }

                Interlocked.Increment(ref connectionCount);
                connections.Add(Handle(client));
            }
        }

        private async Task Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await handler(this, client.GetStream(), stopping.Token);
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    Errors.Enqueue(ex);
                }
            }
        }
    }
}
