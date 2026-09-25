using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZabbixSender.Async.Tests
{
    /// <summary>
    /// A loopback listener that never accepts and whose backlog is full, so a new connect neither completes nor
    /// fails quickly: Linux drops the SYN and retransmits it, Windows retries the refused connection for about
    /// 2 seconds. Lets connect timeouts and cancellation be tested on every platform.
    /// </summary>
    internal sealed class UnacceptedListener : IDisposable
    {
        private readonly Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly List<Socket> queued = new();

        private UnacceptedListener()
        {
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
        }

        public int Port => ((IPEndPoint)listener.LocalEndPoint).Port;

        public static async Task<UnacceptedListener> Create()
        {
            var result = new UnacceptedListener();

            try
            {
                for (var i = 0; i < 100; i++)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    result.queued.Add(socket);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

                    try
                    {
                        await socket.ConnectAsync(IPAddress.Loopback, result.Port, timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return result;
                    }
                }

                throw new InconclusiveException("The listen backlog did not fill up after 100 connections.");
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var socket in queued)
                socket.Dispose();

            listener.Dispose();
        }
    }
}
