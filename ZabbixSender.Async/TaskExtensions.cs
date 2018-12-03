using System.Threading;
using System.Threading.Tasks;

namespace ZabbixSender.Async
{
    internal static class TaskExtensions
    {
        public static async Task WithTimeout(this Task task, int millisecondsDelay,
            CancellationToken cancellationToken)
        {
            if (await Task.WhenAny(task, Task.Delay(millisecondsDelay, cancellationToken)) != task)
                throw new TaskCanceledException();
        }
    }
}
