using System;

namespace ZabbixSender.Async
{
    /// <summary>
    /// The exception thrown when the other side doesn't follow the Zabbix sender protocol: the port doesn't belong to
    /// a Zabbix trapper, the connection closed before a complete response arrived, the response is malformed, or
    /// <see cref="SenderResponse.Info"/> has an unexpected format. It is also thrown when a request or response
    /// exceeds the protocol's 1 GB packet size limit.
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        /// <summary>
        /// Initializes a new instance of ZabbixSender.Async.ProtocolException with given error message.
        /// </summary>
        /// <param name="message">An error message.</param>
        public ProtocolException(string message) :
            base($"Protocol error - {message}.")
        { }

        /// <summary>
        /// Initializes a new instance of ZabbixSender.Async.ProtocolException with given error message and inner exception.
        /// </summary>
        /// <param name="message">An error message.</param>
        /// <param name="innerException">An inner exception.</param>
        public ProtocolException(string message, Exception innerException) :
            base($"Protocol error - {message}.", innerException)
        { }
    }
}
