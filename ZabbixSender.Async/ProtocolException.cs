using System;

namespace ZabbixSender.Async
{
    /// <summary>
    /// Represents an error working with Zabbix sender protocol.
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
