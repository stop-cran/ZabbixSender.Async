using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Writes requests and reads responses in the Zabbix sender protocol wire format. <see cref="Formatter"/> is the
    /// default implementation; implement this interface and pass it to <see cref="SenderSkeleton"/> only to change
    /// the wire format.
    /// </summary>
    public interface IFormatter
    {
        /// <summary>
        /// Read Zabbix server response from given stream.
        /// </summary>
        /// <param name="stream">A stream to read from.</param>
        SenderResponse ReadResponse(Stream stream);

        /// <summary>
        /// Read Zabbix server response from given stream.
        /// </summary>
        /// <param name="stream">A stream to read from.</param>
        /// <param name="cancellationToken">CancellationToken for the read operation.</param>
        /// <returns>The parsed response.</returns>
        Task<SenderResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Write provided data items to the request stream.
        /// </summary>
        /// <param name="stream">Request stream.</param>
        /// <param name="data">Data items to write.</param>
        void WriteRequest(Stream stream, IEnumerable<SendData> data);

        /// <summary>
        /// Write provided data items to the request stream.
        /// </summary>
        /// <param name="stream">A stream to write to.</param>
        /// <param name="data">Data items to write.</param>
        /// <param name="cancellationToken">A CancellationToken for the write operation.</param>
        Task WriteRequestAsync(Stream stream, IEnumerable<SendData> data, CancellationToken cancellationToken = default);
    }
}