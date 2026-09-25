using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace ZabbixSender.Async.Tests
{
    public interface IZabbixApi
    {
        [Post("")]
        Task<ZabbixResponse<TResponse>> Rpc<TRequest, TResponse>([Body] ZabbixRequest<TRequest> body,
            [Header("Authorization")] string authorization, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A minimal Zabbix 7.x JSON-RPC API client.
    /// Since Zabbix 7.2 the session token is passed in the Authorization header rather than the "auth" property.
    /// </summary>
    public class ZabbixApiClient
    {
        private readonly IZabbixApi api;
        private string authorization;
        private int id;

        public ZabbixApiClient(string url)
        {
            api = RestService.For<IZabbixApi>(url);
        }

        public async Task Login(string username, string password, CancellationToken cancellationToken)
        {
            var token = await Call<string>("user.login", new { username, password }, cancellationToken);

            authorization = "Bearer " + token;
        }

        public async Task<T> Call<T>(string method, object parameters, CancellationToken cancellationToken)
        {
            var response = await api.Rpc<object, T>(new ZabbixRequest<object>
            {
                Method = method,
                Params = parameters,
                Id = Interlocked.Increment(ref id)
            }, method is "user.login" or "apiinfo.version" ? null : authorization, cancellationToken);

            if (response.Error != null)
                throw new InvalidOperationException(
                    $"Zabbix API call {method} has failed: {response.Error.Message} {response.Error.Data}");

            return response.Result;
        }
    }

    public class ZabbixRequest<T>
    {
        public string Jsonrpc { get; set; } = "2.0";
        public string Method { get; set; }
        public T Params { get; set; }
        public int Id { get; set; }
    }

    public class ZabbixResponse<T>
    {
        public T Result { get; set; }
        public ZabbixError Error { get; set; }
    }

    public class ZabbixError
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
    }

    public class HostGroupResponse
    {
        public string[] Groupids { get; set; }
    }

    public class HostResponse
    {
        public string[] Hostids { get; set; }
    }

    public class ItemResponse
    {
        public string[] Itemids { get; set; }
    }

    public class HistoryRecord
    {
        public string Itemid { get; set; }
        public string Clock { get; set; }
        public string Value { get; set; }
    }
}