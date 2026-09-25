using System;
using System.Text.Json;
using NUnit.Framework;
using Shouldly;

namespace ZabbixSender.Async.Tests
{
    [TestFixture]
    public class SenderResponseTests
    {
        [TestCase("processed: 1; failed: 0; total: 1; seconds spent: 0.000055", 1, 0, 1, 0.000055)]
        [TestCase("processed: 0; failed: 3; total: 3; seconds spent: 0.001234", 0, 3, 3, 0.001234)]
        [TestCase("processed: 998; failed: 2; total: 1000; seconds spent: 1.500000", 998, 2, 1000, 1.5)]
        public void ParseInfoShouldReadCounters(string info, int processed, int failed, int total, double seconds)
        {
            var parsed = new SenderResponse { Response = "success", Info = info }.ParseInfo();

            parsed.Processed.ShouldBe(processed);
            parsed.Failed.ShouldBe(failed);
            parsed.Total.ShouldBe(total);
            parsed.TimeSpent.ShouldBe(TimeSpan.FromSeconds(seconds));
            parsed.ToString().ShouldBe(info);
        }

        [Test]
        [SetCulture("de-DE")]
        public void ParseInfoShouldNotDependOnCurrentCulture()
        {
            var parsed = new SenderResponseInfo("processed: 1; failed: 0; total: 1; seconds spent: 0.250000");

            parsed.TimeSpent.ShouldBe(TimeSpan.FromMilliseconds(250));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("cannot parse request")]
        [TestCase("processed: x; failed: 0; total: 1; seconds spent: 0.000055")]
        [TestCase("processed: 99999999999; failed: 0; total: 1; seconds spent: 0.000055")]
        public void ParseInfoShouldThrowProtocolExceptionForUnexpectedFormat(string info)
        {
            var ex = Should.Throw<ProtocolException>(() => new SenderResponseInfo(info));

            ex.Message.ShouldContain(info ?? string.Empty);
        }

        [TestCase("success", true)]
        [TestCase("failed", false)]
        [TestCase(null, false)]
        public void IsSuccessShouldReflectResponseField(string response, bool expected) =>
            new SenderResponse { Response = response }.IsSuccess.ShouldBe(expected);

        [Test]
        public void ClockShouldRoundTripAsUnixSeconds()
        {
            var clock = new DateTimeOffset(2026, 9, 25, 12, 30, 15, TimeSpan.FromHours(3));

            var json = JsonSerializer.Serialize(new SendData { Host = "h", Key = "k", Value = "v", Clock = clock }, AotCompatibilityTests.ReflectionJson);

            json.ShouldContain("\"Clock\":" + clock.ToUnixTimeSeconds());
            JsonSerializer.Deserialize<SendData>(json, AotCompatibilityTests.ReflectionJson).Clock.ShouldBe(clock);
        }

        [Test]
        public void ClockShouldBeTruncatedToWholeSeconds()
        {
            var clock = new DateTimeOffset(2026, 9, 25, 12, 30, 15, 999, TimeSpan.Zero);

            var json = JsonSerializer.Serialize(new SendData { Clock = clock }, AotCompatibilityTests.ReflectionJson);

            JsonSerializer.Deserialize<SendData>(json, AotCompatibilityTests.ReflectionJson).Clock.ShouldBe(clock.AddMilliseconds(-999));
        }
    }
}
