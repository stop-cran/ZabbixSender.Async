using System;

namespace ZabbixSender.Async
{
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) :
            base($"Protocol error - {message}.")
        { }

        public ProtocolException(string message, Exception innerException) :
            base($"Protocol error - {message}.", innerException)
        { }
    }
}
