using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZabbixSender.Async.Tests
{
    [TestFixture]
    public class FormatterTests
    {
        private static readonly byte[] ZabbixHeader = new byte[] { 90, 66, 88, 68, 1 };

        [Test]
        public void RequestShouldStartWithZabbixHeader()
        {
            using (var stream = new MemoryStream())
            {
                new Formatter().WriteRequest(stream, new[] { new SendData() });
                Encoding.ASCII.GetString(stream.ToArray()).ShouldStartWith("ZBXD");
            }
        }

        [Test]
        public async Task RequestShouldStartWithZabbixHeaderAsync()
        {
            using (var stream = new MemoryStream())
            {
                await new Formatter().WriteRequestAsync(stream, new[] { new SendData() });
                Encoding.ASCII.GetString(stream.ToArray()).ShouldStartWith("ZBXD");
            }
        }

        [Test]
        public void RequestShouldEndWithJson()
        {
            using (var stream = new MemoryStream())
            {
                new Formatter().WriteRequest(stream, new[]
                {
                    new SendData
                    {
                         Host = "host1",
                         Key = "key1"
                    },
                    new SendData
                    {
                         Host = "host2",
                         Key = "key2",
                         Value = "value2",
                         Clock = new System.DateTime(2018, 12, 18)
                    },
                });

                var result = Encoding.ASCII.GetString(stream.ToArray());

                var json = JToken.Parse(result.Substring(result.IndexOf('{')));

                json.ShouldNotBeNull();
                json.ShouldBeOfType<JObject>();
            }
        }

        [Test]
        public async Task RequestShouldEndWithJsonAsync()
        {
            using (var stream = new MemoryStream())
            {
                await new Formatter().WriteRequestAsync(stream, new[]
                {
                    new SendData
                    {
                         Host = "host1",
                         Key = "key1"
                    },
                    new SendData
                    {
                         Host = "host2",
                         Key = "key2",
                         Value = "value2",
                         Clock = new System.DateTime(2018, 12, 18)
                    },
                });

                var result = Encoding.ASCII.GetString(stream.ToArray());

                var json = JToken.Parse(result.Substring(result.IndexOf('{')));

                json.ShouldNotBeNull();
                json.ShouldBeOfType<JObject>();
            }
        }

        [Test]
        public void ShouldCheckResponseHeader()
        {
            using (var stream = new MemoryStream())
                Should.Throw<ProtocolException>(() => new Formatter().ReadResponse(stream));
        }

        [Test]
        public void ShouldCheckResponseHeaderAsyncc()
        {
            using (var stream = new MemoryStream())
                Should.Throw<ProtocolException>(async () => await new Formatter().ReadResponseAsync(stream));
        }

        [Test]
        public void ShouldRethrowJsonError()
        {
            using (var stream = new MemoryStream(
                ZabbixHeader
                    .Concat(new byte[8])
                    .Concat(Encoding.ASCII.GetBytes("[ \"invalid\" ]"))
                    .ToArray()))
                Should.Throw<ProtocolException>(() => new Formatter().ReadResponse(stream));
        }

        [Test]
        public void ShouldRethrowJsonErrorAsync()
        {
            using (var stream = new MemoryStream(
                ZabbixHeader
                    .Concat(new byte[8])
                    .Concat(Encoding.ASCII.GetBytes("[ \"invalid\" ]"))
                    .ToArray()))
                Should.Throw<ProtocolException>(async () => await new Formatter().ReadResponseAsync(stream));
        }
    }
}