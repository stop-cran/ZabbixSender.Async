using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace ZabbixSender.Async.Tests
{
    /// <summary>
    /// End-to-end tests against a running Zabbix server and frontend (see compose.yaml in the repository root).
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class IntegrationTests
    {
        private static readonly string ApiUrl =
            Environment.GetEnvironmentVariable("ZABBIX_API_URL") ?? "http://localhost:8080/api_jsonrpc.php";

        private static readonly string ZabbixServer =
            Environment.GetEnvironmentVariable("ZABBIX_SERVER") ?? "127.0.0.1";

        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private ZabbixApiClient api;
        private string hostGroupId;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            api = new ZabbixApiClient(ApiUrl);

            var version = await Retry(async () =>
            {
                var result = await api.Call<string>("apiinfo.version", Array.Empty<object>(), default);

                await api.Login("Admin", "zabbix", default);

                return result;
            }, _ => true, ReadyTimeout);

            await TestContext.Out.WriteLineAsync("Connected to Zabbix API version " + version);

            var hostGroup = await api.Call<HostGroupResponse>("hostgroup.create",
                new { name = "ZabbixSender.Async tests " + Guid.NewGuid().ToString("N") }, default);

            hostGroupId = hostGroup.Groupids.ShouldHaveSingleItem();
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            if (hostGroupId != null)
                await api.Call<HostGroupResponse>("hostgroup.delete", new[] { hostGroupId }, default);
        }

        [Test]
        [TestCase("Monitored host 1", "item_key1")]
        public async Task ShouldPushData(string host, string itemKey)
        {
            host += " " + Guid.NewGuid().ToString("N");

            var hostCreateRes = await api.Call<HostResponse>("host.create", new
            {
                host,
                groups = new[] { new { groupid = hostGroupId } }
            }, default);

            var hostId = hostCreateRes.Hostids.ShouldHaveSingleItem();
            await TestContext.Out.WriteLineAsync("Created monitored host with id " + hostId);

            try
            {
                var itemCreateRes = await api.Call<ItemResponse>("item.create", new
                {
                    name = "test item",
                    key_ = itemKey,
                    hostid = hostId,
                    type = 2, // Zabbix trapper
                    value_type = 3 // numeric unsigned
                }, default);

                var itemId = itemCreateRes.Itemids.ShouldHaveSingleItem();
                await TestContext.Out.WriteLineAsync("Created item with id " + itemId);

                var sender = new Sender(ZabbixServer);

                // The server picks up new hosts and items on the next configuration cache update.
                var res = await Retry(
                    () => sender.Send(host, itemKey, "123", default),
                    r => r.ParseInfo().Processed == 1,
                    SyncTimeout);

                res.IsSuccess.ShouldBeTrue();
                res.ParseInfo().ShouldSatisfyAllConditions(
                    info => info.Processed.ShouldBe(1),
                    info => info.Failed.ShouldBe(0),
                    info => info.Total.ShouldBe(1));

                var clock = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());

                var batchRes = await sender.Send(new[]
                {
                    new SendData { Host = host, Key = itemKey, Value = "124", Clock = clock },
                    new SendData { Host = host, Key = itemKey, Value = "125", Clock = clock.AddSeconds(1) }
                });

                batchRes.IsSuccess.ShouldBeTrue();
                batchRes.ParseInfo().Processed.ShouldBe(2);

                var history = await Retry(
                    () => api.Call<HistoryRecord[]>("history.get", new
                    {
                        itemids = new[] { itemId },
                        history = 3,
                        output = "extend",
                        sortfield = "clock",
                        sortorder = "ASC"
                    }, default),
                    records => records.Length >= 3,
                    SyncTimeout);

                history.Select(r => (r.Value, long.Parse(r.Clock)))
                    .Take(2)
                    .ShouldBe(new[]
                    {
                        ("124", clock.ToUnixTimeSeconds()),
                        ("125", clock.ToUnixTimeSeconds() + 1)
                    });
                history.Select(r => r.Value).ShouldContain("123");
            }
            finally
            {
                await api.Call<HostResponse>("host.delete", new[] { hostId }, default);
            }
        }

        private static async Task<T> Retry<T>(Func<Task<T>> action, Func<T, bool> isDone, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            string lastError = null;

            while (true)
            {
                try
                {
                    var result = await action();

                    if (isDone(result) || DateTime.UtcNow >= deadline)
                        return result;
                }
                catch (Exception ex) when (DateTime.UtcNow < deadline)
                {
                    if (ex.Message != lastError)
                        await TestContext.Out.WriteLineAsync("Retrying after an error: " + ex.Message);

                    lastError = ex.Message;
                }

                await Task.Delay(PollInterval);
            }
        }
    }
}