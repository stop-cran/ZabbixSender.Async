using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Shouldly;

namespace ZabbixSender.Async.Tests
{
    public class IntegrationTests
    {
        [Test]
        [TestCase("Monitored host 1", "item_key1")]
        public async Task ShouldPushData(string host, string itemKey)
        {
            var api = RestService.For<IZabbixApi>("http://localhost:8080/api_jsonrpc.php");

            var authRes = await api
                .Request(new {User = "Admin", Password = "zabbix"}, "user.login", null)
                .As<string>(default);

            authRes.Content.ShouldNotBeNull();

            var auth = authRes.Content.Result; 
            auth.ShouldNotBeNullOrEmpty();

            var hostCreateReq = new
            {
                host,
                interfaces = new[]
                {
                    new
                    {
                        Type = 1,
                        Main = 1,
                        Useip = 1,
                        Ip = "127.0.0.1",
                        Dns = "",
                        Port = "10050"
                    }
                },
                groups = new[]
                {
                    new
                    {
                        Groupid = "2"
                    }
                }
            };

            var hostCreateRes = await api.Request(hostCreateReq, "host.create", auth)
                .As<HostResponse>(default);
            
            hostCreateRes.Content.ShouldNotBeNull();

            var hostId = hostCreateRes.Content.Result.Hostids.ShouldHaveSingleItem();
            hostId.ShouldNotBeNullOrEmpty();
            await TestContext.Out.WriteLineAsync("Created monitored host with id " + hostId);

            var itemReq = new
            {
                name = "test item",
                key_ = itemKey,
                hostid = hostId,
                type = 2,
                value_type = 3,
                interfaceid = 0
            };

            var itemCreateRes = await api.Request(itemReq, "item.create", auth)
                .As<ItemResponse>(default);

            itemCreateRes.Content.ShouldNotBeNull();
            
            var itemId = itemCreateRes.Content.Result.Itemids.ShouldHaveSingleItem();
            itemId.ShouldNotBeNullOrEmpty();
            await TestContext.Out.WriteLineAsync("Created item with id " + itemId);

            await Task.Delay(10000);

            var sender = new Sender("127.0.0.1");
            var res = await sender.Send(host, itemKey, "123", default);
            
            res.IsSuccess.ShouldBeTrue();
            res.ParseInfo().Processed.ShouldBe(1);
        }
    }
}