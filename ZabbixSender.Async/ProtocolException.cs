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

        public ProtocolException(string message, byte[] response) :
            base($"Protocol error - {message}. See the whole response in Data property by Response key.")
        {
            Data.Add("Response", response);
        }

        public ProtocolException(string message, Exception innerException, byte[] response) :
            base($"Protocol error - {message}. See the whole response in Data property by Response key.", innerException)
        {
            Data.Add("Response", response);
        }
    }
}
