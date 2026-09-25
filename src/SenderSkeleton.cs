using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Sends data to Zabbix over connections and formatters produced by caller-supplied factories.
    /// Use it instead of <see cref="Sender"/> to customize the <see cref="TcpClient"/>
    /// (for example, to bind a local address or tune socket options) or the <see cref="IFormatter"/>.
    /// </summary>
    /// <remarks>
    /// Each <c>Send</c> call gets a new connection from the factory, writes one request, reads one response and disposes
    /// the connection. The instance keeps no other state, so it is safe for concurrent use if the factories are.
    /// Waiting for the response is limited by <see cref="TcpClient.ReceiveTimeout"/> of the connection; 0 means no limit.
    /// </remarks>
    public class SenderSkeleton
    {
        private readonly Func<CancellationToken, Task<TcpClient>> tcpClientFactory;
        private readonly Func<IFormatter> formatterFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SenderSkeleton"/> class.
        /// </summary>
        /// <param name="tcpClientFactory">Creates a new connected <see cref="TcpClient"/> for each request.
        /// The sender disposes it after reading the response.</param>
        /// <param name="formatterFactory">Creates the formatter used for each request.</param>
        public SenderSkeleton(
            Func<CancellationToken, Task<TcpClient>> tcpClientFactory,
            Func<IFormatter> formatterFactory)
        {
            this.tcpClientFactory = tcpClientFactory;
            this.formatterFactory = formatterFactory;
        }

        /// <summary>
        /// Sends one or more values in a single request.
        /// </summary>
        /// <param name="data">The values to send.</param>
        /// <returns>
        /// The server response. The call succeeds even if the server rejects some or all values:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="SenderResponse.ParseInfo"/>.
        /// </returns>
        /// <exception cref="SocketException">The connection failed, for example it was refused or the host name
        /// could not be resolved.
        /// Windows retries a refused connection for about 2 seconds, so with a shorter timeout it ends as a timeout
        /// instead.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>. Tell a timeout from cancellation
        /// by that, not by the exception type.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        public Task<SenderResponse> Send(params SendData[] data) =>
            Send(data, CancellationToken.None);

        /// <summary>
        /// Sends a single value.
        /// </summary>
        /// <param name="host">The technical name of the host in Zabbix, not its visible name.</param>
        /// <param name="key">The key of a trapper item on that host
        /// (see https://www.zabbix.com/documentation/7.4/en/manual/config/items/item/key).</param>
        /// <param name="value">The value, formatted to match the item's type of information.</param>
        /// <param name="cancellationToken">Cancels connecting, sending and waiting for the response.</param>
        /// <returns>
        /// The server response. The call succeeds even if the server rejects the value:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="SenderResponse.ParseInfo"/>.
        /// </returns>
        /// <exception cref="SocketException">The connection failed, for example it was refused or the host name
        /// could not be resolved.
        /// Windows retries a refused connection for about 2 seconds, so with a shorter timeout it ends as a timeout
        /// instead.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>. Tell a timeout from cancellation
        /// by that, not by the exception type.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. It may be a
        /// <see cref="TaskCanceledException"/>, but unlike a timeout its <see cref="Exception.InnerException"/> is not a
        /// <see cref="TimeoutException"/>.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        public Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken = default) =>
            Send(new[]
            {
                new SendData
                {
                    Host = host,
                    Key = key,
                    Value = value
                }
            }, cancellationToken);

        /// <summary>
        /// Sends one or more values in a single request.
        /// </summary>
        /// <param name="data">The values to send.</param>
        /// <param name="cancellationToken">Cancels connecting, sending and waiting for the response.</param>
        /// <returns>
        /// The server response. The call succeeds even if the server rejects some or all values:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="SenderResponse.ParseInfo"/>.
        /// </returns>
        /// <exception cref="SocketException">The connection failed, for example it was refused or the host name
        /// could not be resolved.
        /// Windows retries a refused connection for about 2 seconds, so with a shorter timeout it ends as a timeout
        /// instead.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>. Tell a timeout from cancellation
        /// by that, not by the exception type.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. It may be a
        /// <see cref="TaskCanceledException"/>, but unlike a timeout its <see cref="Exception.InnerException"/> is not a
        /// <see cref="TimeoutException"/>.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        public async Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default)
        {
            using var tcpClient = await tcpClientFactory(cancellationToken);
            using var networkStream = tcpClient.GetStream();
            var formatter = formatterFactory();

            await formatter.WriteRequestAsync(networkStream, data, cancellationToken);
            await networkStream.FlushAsync(cancellationToken);

            var timeout = tcpClient.ReceiveTimeout;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (timeout > 0)
                timeoutSource.CancelAfter(timeout);

            try
            {
                return await formatter.ReadResponseAsync(networkStream, timeoutSource.Token);
            }
            catch (OperationCanceledException ex) when (
                timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw CreateTimeoutException(
                    $"Zabbix server {tcpClient.Client.RemoteEndPoint} has not responded within {timeout} ms", ex);
            }
            catch (OperationCanceledException ex) when (
                cancellationToken.IsCancellationRequested && ex.CancellationToken != cancellationToken)
            {
                throw WithCallerToken(ex, cancellationToken);
            }
        }

        internal static TaskCanceledException CreateTimeoutException(string message, Exception innerException) =>
            new(message, new TimeoutException(message, innerException));

        // Operations get a linked token, so report the caller's own token in the exception, as HttpClient does.
        internal static OperationCanceledException WithCallerToken(
            OperationCanceledException exception, CancellationToken cancellationToken) =>
            exception is TaskCanceledException
                ? new TaskCanceledException(exception.Message, exception, cancellationToken)
                : new OperationCanceledException(exception.Message, exception, cancellationToken);
    }
}