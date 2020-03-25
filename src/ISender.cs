using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Zabbix data sender.
    /// </summary>
    public interface ISender
    {
        /// <summary>
        /// Send an array of data items.
        /// </summary>
        /// <param name="data">Data items to send.</param>
        /// <returns>Zabbix Trapper response.</returns>
        Task<SenderResponse> Send(params SendData[] data);

        /// <summary>
        /// Send a single data item for specified host.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="key">An item key (see https://www.zabbix.com/documentation/4.4/manual/config/items/item/key).</param>
        /// <param name="value">An item value</param>
        /// <param name="cancellationToken">A CancellationToken for an overall request processing.</param>
        Task<SenderResponse> Send(string host, string key, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send multiple data items.
        /// </summary>
        /// <param name="data">Data items to send</param>
        /// <param name="cancellationToken">A CancellationToken for an overall request processing.</param>
        Task<SenderResponse> Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default);
    }
}
