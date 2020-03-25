using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    internal static class NetworkStreamExtensions
    {
        public static async Task WaitForDataAvailable(this NetworkStream stream, int millisecondsDelay,
            CancellationToken cancellationToken)
        {
            const int millisecondsDelayStep = 50;

            for (int retry = 0; !stream.DataAvailable; retry += millisecondsDelayStep)
            {
                await Task.Delay(millisecondsDelayStep, cancellationToken);

                if (retry >= millisecondsDelay || millisecondsDelay == 0)
                    throw new TaskCanceledException();
            }
        }
    }
}
