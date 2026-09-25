using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Sends values to Zabbix trapper items. <see cref="Sender"/> implements it; depend on this interface
    /// to register the sender in a dependency injection container or to replace it with a fake in tests.
    /// </summary>
    public interface ISender
    {
        /// <summary>
        /// The host name or IP address of the Zabbix server or proxy.
        /// </summary>
        public String ZabbixServer { get; }

        /// <summary>
        /// Sends one or more values in a single request.
        /// </summary>
        /// <param name="data">The values to send.</param>
        /// <returns>
        /// The server response. The call succeeds even if the server rejects some or all values:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="SenderResponse.ParseInfo"/>.
        /// </returns>
        /// <exception cref="System.Net.Sockets.SocketException">The connection failed, for example it was refused
        /// or the host name could not be resolved.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        Task<SenderResponse> Send(params SendData[] data);

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
        /// <exception cref="System.Net.Sockets.SocketException">The connection failed, for example it was refused
        /// or the host name could not be resolved.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends one or more values in a single request.
        /// </summary>
        /// <param name="data">The values to send.</param>
        /// <param name="cancellationToken">Cancels connecting, sending and waiting for the response.</param>
        /// <returns>
        /// The server response. The call succeeds even if the server rejects some or all values:
        /// check <see cref="SenderResponseInfo.Failed"/> from <see cref="SenderResponse.ParseInfo"/>.
        /// </returns>
        /// <exception cref="System.Net.Sockets.SocketException">The connection failed, for example it was refused
        /// or the host name could not be resolved.</exception>
        /// <exception cref="System.IO.IOException">The connection was reset or broken while sending the request or
        /// receiving the response. <see cref="Exception.InnerException"/> is usually a <see cref="System.Net.Sockets.SocketException"/>.</exception>
        /// <exception cref="TaskCanceledException">Connecting or waiting for the response took longer than the timeout.
        /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="ProtocolException">The reply is not a valid Zabbix sender protocol response, for example
        /// the port does not belong to a Zabbix trapper, or the connection closed before a complete response arrived.
        /// </exception>
        Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default);
    }
}
