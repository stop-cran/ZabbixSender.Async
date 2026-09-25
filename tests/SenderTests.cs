using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace ZabbixSender.Async.Tests
{
    /// <summary>
    /// Tests <see cref="Sender"/> end to end over real loopback TCP connections, against <see cref="FakeZabbixServer"/>.
    /// </summary>
    [TestFixture]
    public class SenderTests
    {
        [Test]
        public async Task ShouldSendValueAndReturnServerResponse()
        {
            await using var server = FakeZabbixServer.Replying();
            var sender = new Sender("127.0.0.1", server.Port);

            var response = await sender.Send("host1", "key1", "value1");

            response.IsSuccess.ShouldBeTrue();
            response.Info.ShouldBe("processed: 1; failed: 0; total: 1; seconds spent: 0.000055");
            server.Errors.ShouldBeEmpty();

            var request = server.Requests.ShouldHaveSingleItem().RootElement;
            request.GetProperty("request").GetString().ShouldBe("sender data");

            var item = request.GetProperty("data").EnumerateArray().ShouldHaveSingleItem();
            item.GetProperty("host").GetString().ShouldBe("host1");
            item.GetProperty("key").GetString().ShouldBe("key1");
            item.GetProperty("value").GetString().ShouldBe("value1");
        }

        [Test]
        public async Task ShouldSendBatchInOneRequest()
        {
            await using var server = FakeZabbixServer.Replying(
                "{\"response\":\"success\",\"info\":\"processed: 2; failed: 1; total: 3; seconds spent: 0.000100\"}");
            var sender = new Sender("127.0.0.1", server.Port);
            var clock = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

            var response = await sender.Send(
                new SendData { Host = "h", Key = "k1", Value = "1" },
                new SendData { Host = "h", Key = "k2", Value = "2", Clock = clock },
                new SendData { Host = "h", Key = "k3", Value = "3" });

            response.IsSuccess.ShouldBeTrue();
            response.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(2),
                info => info.Failed.ShouldBe(1),
                info => info.Total.ShouldBe(3));

            server.ConnectionCount.ShouldBe(1);

            var data = server.Requests.ShouldHaveSingleItem().RootElement.GetProperty("data").EnumerateArray().ToArray();
            data.Select(d => d.GetProperty("key").GetString()).ShouldBe(new[] { "k1", "k2", "k3" });
            data[1].GetProperty("clock").GetInt64().ShouldBe(clock.ToUnixTimeSeconds());
        }

        [Test]
        public async Task ShouldReturnFailedResponseWithoutThrowing()
        {
            await using var server = FakeZabbixServer.Replying(
                "{\"response\":\"failed\",\"info\":\"cannot parse request\"}");
            var sender = new Sender("127.0.0.1", server.Port);

            var response = await sender.Send("h", "k", "v");

            response.IsSuccess.ShouldBeFalse();
            response.Response.ShouldBe("failed");
            response.Info.ShouldBe("cannot parse request");
            Should.Throw<ProtocolException>(() => response.ParseInfo());
        }

        [Test]
        public async Task ShouldOpenNewConnectionForEachSendAndAllowConcurrentUse()
        {
            const int count = 50;
            await using var server = FakeZabbixServer.Replying();
            var sender = new Sender("127.0.0.1", server.Port, timeout: 10_000);

            var responses = await Task.WhenAll(Enumerable.Range(0, count)
                .Select(i => sender.Send("host", "key", i.ToString())));

            responses.ShouldAllBe(r => r.IsSuccess);
            server.Errors.ShouldBeEmpty();
            server.ConnectionCount.ShouldBe(count);
            server.Requests
                .Select(r => r.RootElement.GetProperty("data")[0].GetProperty("value").GetString())
                .OrderBy(int.Parse)
                .ShouldBe(Enumerable.Range(0, count).Select(i => i.ToString()));
        }

        [Test]
        public async Task ShouldThrowSocketExceptionWhenNothingListens()
        {
            // Windows retries a refused connection for about 2 seconds, so the timeout must be longer than that
            // to observe the SocketException rather than a connect timeout.
            var sender = new Sender("127.0.0.1", GetUnusedPort(), timeout: 10_000);

            var ex = await Should.ThrowAsync<SocketException>(() => sender.Send("h", "k", "v"));

            ex.SocketErrorCode.ShouldBe(SocketError.ConnectionRefused);
        }

        [Test]
        public async Task ShouldThrowTaskCanceledExceptionWithTimeoutWhenServerDoesNotReply()
        {
            await using var server = FakeZabbixServer.Silent();
            var sender = new Sender("127.0.0.1", server.Port, timeout: 200);
            var stopwatch = Stopwatch.StartNew();

            var ex = (await CatchAsync(() => sender.Send("h", "k", "v"))).ShouldBeOfType<TaskCanceledException>();

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            ex.InnerException.ShouldBeOfType<TimeoutException>();
            ex.Message.ShouldContain("has not responded within 200 ms");
            server.Requests.Count.ShouldBe(1);
        }

        [Test]
        public async Task ShouldThrowTaskCanceledExceptionWithTimeoutWhenReplyStalls()
        {
            await using var server = new FakeZabbixServer(async (s, stream, ct) =>
            {
                await s.ReadRequest(stream, ct);
                await stream.WriteAsync(FakeZabbixServer.Packet(FakeZabbixServer.SuccessResponse).AsMemory(0, 20), ct);
                await Task.Delay(Timeout.Infinite, ct);
            });
            var sender = new Sender("127.0.0.1", server.Port, timeout: 200);
            var stopwatch = Stopwatch.StartNew();

            var ex = (await CatchAsync(() => sender.Send("h", "k", "v"))).ShouldBeOfType<TaskCanceledException>();

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            ex.InnerException.ShouldBeOfType<TimeoutException>();
            ex.Message.ShouldContain("has not responded within 200 ms");
        }

        [Test]
        public async Task ShouldThrowTaskCanceledExceptionWithTimeoutWhenConnectTakesTooLong()
        {
            using var listener = await UnacceptedListener.Create();
            var sender = new Sender("127.0.0.1", listener.Port, timeout: 200);
            var stopwatch = Stopwatch.StartNew();

            var ex = (await CatchAsync(() => sender.Send("h", "k", "v"))).ShouldBeOfType<TaskCanceledException>();

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            ex.InnerException.ShouldBeOfType<TimeoutException>();
            ex.Message.ShouldContain($"Could not connect to Zabbix server 127.0.0.1:{listener.Port} within 200 ms");
        }

        [Test]
        public async Task ShouldThrowProtocolExceptionWhenServerClosesConnectionWithoutReply()
        {
            await using var server = new FakeZabbixServer((s, stream, ct) => s.ReadRequest(stream, ct));
            var sender = new Sender("127.0.0.1", server.Port, timeout: 10_000);
            var stopwatch = Stopwatch.StartNew();

            await Should.ThrowAsync<ProtocolException>(() => sender.Send("h", "k", "v"));

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }

        [Test]
        public async Task ShouldThrowIOExceptionWhenServerResetsConnection()
        {
            await using var server = new FakeZabbixServer(async (s, stream, ct) =>
            {
                await s.ReadRequest(stream, ct);
                stream.Socket.LingerState = new LingerOption(true, 0);
                stream.Socket.Close();
            });
            var sender = new Sender("127.0.0.1", server.Port, timeout: 10_000);

            var ex = (await CatchAsync(() => sender.Send("h", "k", "v"))).ShouldBeOfType<IOException>();

            ex.InnerException.ShouldBeOfType<SocketException>().SocketErrorCode.ShouldBe(SocketError.ConnectionReset);
        }

        [TestCase(30_000)]
        [TestCase(0)]
        public async Task ShouldThrowOperationCanceledExceptionWhenCancelled(int timeout)
        {
            await using var server = FakeZabbixServer.Silent();
            var sender = new Sender("127.0.0.1", server.Port, timeout: timeout);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var stopwatch = Stopwatch.StartNew();

            var ex = await CatchAsync(() => sender.Send("h", "k", "v", cts.Token));

            ex.ShouldBeAssignableTo<OperationCanceledException>().CancellationToken.ShouldBe(cts.Token);
            ex.InnerException.ShouldNotBeOfType<TimeoutException>();
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Test]
        public async Task ShouldThrowOperationCanceledExceptionWhenCancelledWhileConnecting()
        {
            using var listener = await UnacceptedListener.Create();
            var sender = new Sender("127.0.0.1", listener.Port, timeout: 30_000);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var stopwatch = Stopwatch.StartNew();

            var ex = await CatchAsync(() => sender.Send("h", "k", "v", cts.Token));

            ex.ShouldBeAssignableTo<OperationCanceledException>().CancellationToken.ShouldBe(cts.Token);
            ex.InnerException.ShouldNotBeOfType<TimeoutException>();
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }

        [Test]
        public async Task ShouldThrowOperationCanceledExceptionWhenCancelledWhileWriting()
        {
            await using var server = new FakeZabbixServer((_, _, ct) => Task.Delay(Timeout.Infinite, ct));
            var sender = new Sender("127.0.0.1", server.Port, timeout: 30_000);
            // Much more than the socket buffers hold, so the write stalls while the server reads nothing. Windows
            // loopback may buffer it all, and then the cancellation arrives while waiting for the reply instead.
            var data = Enumerable.Range(0, 200)
                .Select(i => new SendData { Host = "h", Key = "k", Value = new string('x', 100_000) })
                .ToArray();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var stopwatch = Stopwatch.StartNew();

            var ex = await CatchAsync(() => sender.Send(data, cts.Token));

            ex.ShouldBeAssignableTo<OperationCanceledException>().CancellationToken.ShouldBe(cts.Token);
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [TestCase(0)]
        [TestCase(Timeout.Infinite)]
        public async Task ShouldTreatZeroOrInfiniteTimeoutAsNoLimit(int timeout)
        {
            await using var server = FakeZabbixServer.Replying();
            var sender = new Sender("127.0.0.1", server.Port, timeout: timeout);

            var response = await sender.Send("h", "k", "v");

            response.IsSuccess.ShouldBeTrue();
        }

        [Test]
        public void ShouldRejectNegativeTimeout()
        {
            Should.Throw<ArgumentOutOfRangeException>(() => new Sender("127.0.0.1", timeout: -2));
        }

        [Test]
        public async Task ShouldNotAddLatencyToEachSend()
        {
            await using var server = FakeZabbixServer.Replying();
            var sender = new Sender("127.0.0.1", server.Port, timeout: 10_000);
            await sender.Send("h", "k", "warm-up");
            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < 10; i++)
                await sender.Send("h", "k", i.ToString());

            // Before 1.4.0 the response was polled every 50 ms, so 10 sends took at least 500 ms.
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(400));
        }

        [Test]
        public async Task ShouldThrowProtocolExceptionWhenPeerIsNotZabbix()
        {
            await using var server = FakeZabbixServer.ReplyingRaw(
                Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"));
            var sender = new Sender("127.0.0.1", server.Port);

            await Should.ThrowAsync<ProtocolException>(() => sender.Send("h", "k", "v"));
        }

        [Test]
        public async Task ShouldThrowProtocolExceptionWhenResponseIsTruncated()
        {
            var packet = FakeZabbixServer.Packet(FakeZabbixServer.SuccessResponse);
            await using var server = FakeZabbixServer.ReplyingRaw(packet.AsSpan(0, packet.Length - 5).ToArray());
            var sender = new Sender("127.0.0.1", server.Port);

            await Should.ThrowAsync<ProtocolException>(() => sender.Send("h", "k", "v"));
        }

        [Test]
        public void ShouldExposeConnectionSettings()
        {
            ISender sender = new Sender("zabbix.example.com", 10052);

            sender.ZabbixServer.ShouldBe("zabbix.example.com");
            ((Sender)sender).Port.ShouldBe(10052);
        }

        // Shouldly replaces the exception of a canceled task with a new TaskCanceledException, losing
        // InnerException and Message, so await directly as callers do.
        private static async Task<Exception> CatchAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                return ex;
            }

            throw new AssertionException("No exception was thrown.");
        }

        private static int GetUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
