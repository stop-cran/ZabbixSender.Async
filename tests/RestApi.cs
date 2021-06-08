using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace ZabbixSender.Async.Tests
{
    public interface IZabbixApi
    {
        [Post("")]
        Task<IApiResponse<ZabbixResponse<TResponse>>> Rpc<TRequest, TResponse>([Body] ZabbixRequest<TRequest> body,
            CancellationToken cancellationToken);
    }

    public static class ZabbixApiExtensions
    {
        public static ZabbixTask<T> Request<T>(this IZabbixApi api, T request, string method, string auth) =>
            new(api, new()
            {
                Params = request,
                Method = method,
                Auth = auth,
                Id = 1
            });
    }

    public class ZabbixTask<T>
    {
        private readonly IZabbixApi api;
        private readonly ZabbixRequest<T> request;

        public ZabbixTask(IZabbixApi api, ZabbixRequest<T> request)
        {
            this.api = api;
            this.request = request;
        }

        public async Task<IApiResponse<ZabbixResponse<TResponse>>> As<TResponse>(
            CancellationToken cancellationToken) =>
            await api.Rpc<T, TResponse>(request, cancellationToken);
    }

    public class ZabbixRequest<T>
    {
        public string Jsonrpc { get; set; } = "2.0";
        public string Method { get; set; }
        public T Params { get; set; }
        public int Id { get; set; }
        public string Auth { get; set; }
    }

    public class ZabbixResponse<T>
    {
        public T Result { get; set; }
    }

    public class HostResponse
    {
        public string[] Hostids { get; set; }
    }

    public class ItemResponse
    {
        public string[] Itemids { get; set; }
    }
}