using System;
using System.Collections.Generic;
using System.Linq;
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
        private const int TrapperItem = 2;
        private const int HttpAgentItem = 19;
        private const int TextValue = 4;
        private const int NumericUnsignedValue = 3;

        private static readonly string ApiUrl =
            Environment.GetEnvironmentVariable("ZABBIX_API_URL") ?? "http://localhost:8080/api_jsonrpc.php";

        private static readonly string ZabbixServer =
            Environment.GetEnvironmentVariable("ZABBIX_SERVER") ?? "127.0.0.1";

        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly List<string> hostIds = new();
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

        [TearDown]
        public async Task DeleteHosts()
        {
            if (hostIds.Count > 0)
                await api.Call<HostResponse>("host.delete", hostIds.ToArray(), default);

            hostIds.Clear();
        }

        [Test]
        public async Task ShouldPushData()
        {
            var (host, hostId) = await CreateHost("Monitored host");
            var itemId = await CreateItem(hostId, "item_key1", NumericUnsignedValue);
            var sender = new Sender(ZabbixServer);

            var res = await SendWhenReady(sender, host, "item_key1", "123");

            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(1),
                info => info.Failed.ShouldBe(0),
                info => info.Total.ShouldBe(1));

            var clock = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());

            var batchRes = await sender.Send(new[]
            {
                new SendData { Host = host, Key = "item_key1", Value = "124", Clock = clock },
                new SendData { Host = host, Key = "item_key1", Value = "125", Clock = clock.AddSeconds(1) }
            });

            batchRes.IsSuccess.ShouldBeTrue();
            batchRes.ParseInfo().Processed.ShouldBe(2);

            var history = await WaitForHistory(itemId, NumericUnsignedValue, 3);

            history.Select(r => (r.Value, long.Parse(r.Clock)))
                .Take(2)
                .ShouldBe(new[]
                {
                    ("124", clock.ToUnixTimeSeconds()),
                    ("125", clock.ToUnixTimeSeconds() + 1)
                });
            history.Select(r => r.Value).ShouldContain("123");
        }

        [Test]
        public async Task ShouldCountUnknownHostsAndItemsAsFailedWithoutThrowing()
        {
            var (host, hostId) = await CreateHost("Rejected values host");
            await CreateItem(hostId, "known", NumericUnsignedValue);
            var sender = new Sender(ZabbixServer);

            await SendWhenReady(sender, host, "known", "1");

            var res = await sender.Send(
                new SendData { Host = host, Key = "known", Value = "2" },
                new SendData { Host = host, Key = "unknown.key", Value = "3" },
                new SendData { Host = "Unknown host " + Guid.NewGuid().ToString("N"), Key = "known", Value = "4" });

            await TestContext.Out.WriteLineAsync($"Mixed batch: {res.Response} / {res.Info}");
            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(1),
                info => info.Failed.ShouldBe(2),
                info => info.Total.ShouldBe(3));

            var allFailed = await sender.Send(host, "unknown.key", "5");

            await TestContext.Out.WriteLineAsync($"All failed: {allFailed.Response} / {allFailed.Info}");
            allFailed.IsSuccess.ShouldBeTrue();
            allFailed.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(0),
                info => info.Failed.ShouldBe(1));
        }

        [Test]
        public async Task ShouldCountValuesRejectedByHostOrItemSettingsAsFailed()
        {
            // Everything is created before the "ok" item, so it is in the configuration cache once "ok" is.
            var (disabledHost, disabledHostId) = await CreateHost("Disabled host",
                new Dictionary<string, object> { ["status"] = 1 });
            await CreateItem(disabledHostId, "item", NumericUnsignedValue);
            var (pskHost, pskHostId) = await CreateHost("PSK only host", new Dictionary<string, object>
            {
                ["tls_accept"] = 2,
                ["tls_psk_identity"] = "psk-" + Guid.NewGuid().ToString("N"),
                ["tls_psk"] = "1f87b595725ac58dd977beef14b97461a7c1045b9a1c963065002c5473194952"
            });
            await CreateItem(pskHostId, "item", NumericUnsignedValue);
            var (host, hostId) = await CreateHost("Item settings host");
            await CreateItem(hostId, "disabled", NumericUnsignedValue, new Dictionary<string, object> { ["status"] = 1 });
            await CreateItem(hostId, "restricted", NumericUnsignedValue, new Dictionary<string, object>
            {
                ["trapper_hosts"] = "192.0.2.1"
            });
            await CreateItem(hostId, "http.no.trap", NumericUnsignedValue, new Dictionary<string, object>
            {
                ["type"] = HttpAgentItem,
                ["allow_traps"] = 0,
                ["url"] = "http://127.0.0.1:9/",
                ["delay"] = "1h"
            });
            await CreateItem(hostId, "ok", NumericUnsignedValue);
            var sender = new Sender(ZabbixServer);

            await SendWhenReady(sender, host, "ok", "1");

            var res = await sender.Send(
                new SendData { Host = host, Key = "ok", Value = "2" },
                new SendData { Host = host, Key = "disabled", Value = "3" },
                new SendData { Host = host, Key = "restricted", Value = "4" },
                new SendData { Host = host, Key = "http.no.trap", Value = "5" },
                new SendData { Host = disabledHost, Key = "item", Value = "6" },
                new SendData { Host = pskHost, Key = "item", Value = "7" });

            await TestContext.Out.WriteLineAsync($"Rejected by settings: {res.Response} / {res.Info}");
            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(1),
                info => info.Failed.ShouldBe(5),
                info => info.Total.ShouldBe(6));
        }

        [Test]
        public async Task ShouldCountValueOfWrongTypeAsProcessedButNotStoreIt()
        {
            var (host, hostId) = await CreateHost("Wrong type host");
            var itemId = await CreateItem(hostId, "numeric", NumericUnsignedValue);
            var sender = new Sender(ZabbixServer);

            await SendWhenReady(sender, host, "numeric", "1");

            var res = await sender.Send(host, "numeric", "not a number");

            await TestContext.Out.WriteLineAsync($"Wrong type: {res.Response} / {res.Info}");
            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(1),
                info => info.Failed.ShouldBe(0));

            // The value is rejected later, during preprocessing: the item becomes "not supported".
            var item = await Retry(
                async () => (await api.Call<ItemRecord[]>("item.get", new
                {
                    itemids = new[] { itemId },
                    output = new[] { "state", "error" }
                }, default)).ShouldHaveSingleItem(),
                i => i.State == "1",
                SyncTimeout);

            await TestContext.Out.WriteLineAsync($"Item state: {item.State} / {item.Error}");
            item.State.ShouldBe("1");
            item.Error.ShouldContain("not suitable for value type");
            (await WaitForHistory(itemId, NumericUnsignedValue, 1)).Select(r => r.Value).ShouldBe(new[] { "1" });
        }

        [Test]
        public async Task ShouldPushUnicodeText()
        {
            const string value = "Привет, мир! 你好 🌍 \"quoted\" back\\slash\ttab\nsecond line";
            var (host, hostId) = await CreateHost("Text host");
            var itemId = await CreateItem(hostId, "text", TextValue);
            var sender = new Sender(ZabbixServer);

            var res = await SendWhenReady(sender, host, "text", value);

            res.ParseInfo().Processed.ShouldBe(1);
            (await WaitForHistory(itemId, TextValue, 1)).ShouldHaveSingleItem().Value.ShouldBe(value);
        }

        [TestCase(10_000)]
        public async Task ShouldPushLargeBatchInOneRequest(int count)
        {
            var (host, hostId) = await CreateHost("Large batch host");
            var itemId = await CreateItem(hostId, "batch", NumericUnsignedValue);
            var sender = new Sender(ZabbixServer, timeout: 10_000);

            await SendWhenReady(sender, host, "batch", "0");

            var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
            var res = await sender.Send(Enumerable.Range(1, count).Select(i => new SendData
            {
                Host = host,
                Key = "batch",
                Value = i.ToString(),
                Clock = start.AddSeconds(i)
            }));

            await TestContext.Out.WriteLineAsync($"Large batch: {res.Response} / {res.Info}");
            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().ShouldSatisfyAllConditions(
                info => info.Processed.ShouldBe(count),
                info => info.Failed.ShouldBe(0),
                info => info.Total.ShouldBe(count));

            var stored = await Retry(
                () => api.Call<string>("history.get", new
                {
                    itemids = new[] { itemId },
                    history = NumericUnsignedValue,
                    countOutput = true
                }, default),
                c => int.Parse(c) >= count + 1,
                SyncTimeout);

            int.Parse(stored).ShouldBe(count + 1);
        }

        [Test]
        public async Task ShouldPushToHttpAgentItemWithTrappingEnabled()
        {
            var (host, hostId) = await CreateHost("HTTP agent host");
            var itemId = await CreateItem(hostId, "http.trap", NumericUnsignedValue, new Dictionary<string, object>
            {
                ["type"] = HttpAgentItem,
                ["allow_traps"] = 1,
                ["url"] = "http://127.0.0.1:9/",
                ["delay"] = "1h"
            });
            var sender = new Sender(ZabbixServer);

            var res = await SendWhenReady(sender, host, "http.trap", "42");

            await TestContext.Out.WriteLineAsync($"HTTP agent: {res.Response} / {res.Info}");
            res.ParseInfo().Processed.ShouldBe(1);
            (await WaitForHistory(itemId, NumericUnsignedValue, 1)).Select(r => r.Value).ShouldContain("42");
        }

        private async Task<(string Host, string HostId)> CreateHost(string name,
            IDictionary<string, object> overrides = null)
        {
            var host = name + " " + Guid.NewGuid().ToString("N");
            var parameters = new Dictionary<string, object>
            {
                ["host"] = host,
                ["groups"] = new[] { new { groupid = hostGroupId } }
            };

            foreach (var (key, value) in overrides ?? new Dictionary<string, object>())
                parameters[key] = value;

            var res = await api.Call<HostResponse>("host.create", parameters, default);

            var hostId = res.Hostids.ShouldHaveSingleItem();
            hostIds.Add(hostId);

            return (host, hostId);
        }

        private async Task<string> CreateItem(string hostId, string key, int valueType,
            IDictionary<string, object> overrides = null)
        {
            var parameters = new Dictionary<string, object>
            {
                ["name"] = key,
                ["key_"] = key,
                ["hostid"] = hostId,
                ["type"] = TrapperItem,
                ["value_type"] = valueType
            };

            foreach (var (name, value) in overrides ?? new Dictionary<string, object>())
                parameters[name] = value;

            var res = await api.Call<ItemResponse>("item.create", parameters, default);

            return res.Itemids.ShouldHaveSingleItem();
        }

        // The server picks up new hosts and items on the next configuration cache update.
        private static Task<SenderResponse> SendWhenReady(ISender sender, string host, string key, string value) =>
            Retry(
                () => sender.Send(host, key, value, default),
                r => r.ParseInfo().Processed == 1,
                SyncTimeout);

        private Task<HistoryRecord[]> WaitForHistory(string itemId, int valueType, int count) =>
            Retry(
                () => api.Call<HistoryRecord[]>("history.get", new
                {
                    itemids = new[] { itemId },
                    history = valueType,
                    output = "extend",
                    sortfield = "clock",
                    sortorder = "ASC"
                }, default),
                records => records.Length >= count,
                SyncTimeout);

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
