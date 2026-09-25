using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace ZabbixSender.Async.Tests
{
    /// <summary>
    /// Trimmed and Native AOT applications, including .NET 10 file-based apps run with <c>dotnet run app.cs</c>,
    /// disable reflection-based System.Text.Json serialization. The test project does the same with the
    /// JsonSerializerIsReflectionEnabledByDefault property, so every test checks that the library works without it.
    /// </summary>
    [TestFixture]
    public class AotCompatibilityTests
    {
        /// <summary>
        /// Options for the tests' own JSON, which doesn't need to work without reflection.
        /// </summary>
        internal static readonly JsonSerializerOptions ReflectionJson = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        [Test]
        public void ReflectionBasedJsonShouldBeDisabled() =>
            JsonSerializer.IsReflectionEnabledByDefault.ShouldBeFalse();

        [Test]
        public async Task CustomSettingsWithSourceGeneratedResolverShouldBeUsed([Values] bool useAsync)
        {
            var formatter = new Formatter(IndentedZabbixJsonContext.Default.Options);
            var data = new[] { new SendData { Host = "host1", Key = "key1", Value = "1" } };

            using var stream = new MemoryStream();

            if (useAsync)
                await formatter.WriteRequestAsync(stream, data);
            else
                formatter.WriteRequest(stream, data);

            Encoding.UTF8.GetString(stream.ToArray(), 13, (int)stream.Length - 13)
                .ShouldContain("\n  \"request\": \"sender data\"");
        }

        [Test]
        public async Task CustomSettingsWithoutResolverShouldUseBuiltInMetadata([Values] bool useAsync)
        {
            var settings = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var payload = await WriteRequest(new Formatter(settings), useAsync);

            payload.ShouldStartWith("{\"request\":\"sender data\",\"data\":[{\"host\":\"host1\",\"key\":\"key1\",\"value\":\"1\"}],\"clock\":");
            settings.TypeInfoResolver.ShouldBeNull();
            settings.IsReadOnly.ShouldBeFalse();
        }

        [Test]
        public async Task CustomSettingsShouldKeepTheirNamingPolicy([Values] bool useAsync)
        {
            var payload = await WriteRequest(new Formatter(new JsonSerializerOptions()), useAsync);

            payload.ShouldStartWith("{\"Request\":\"sender data\",\"Data\":[{\"Host\":\"host1\",\"Key\":\"key1\",\"Value\":\"1\",\"Clock\":null}],\"Clock\":");
        }

        [Test]
        public async Task NullSettingsShouldMeanDefaults([Values] bool useAsync)
        {
            var payload = await WriteRequest(new Formatter(null), useAsync);

            payload.ShouldStartWith("{\"request\":\"sender data\",\"data\":[{\"host\":\"host1\",\"key\":\"key1\",\"value\":\"1\"}],\"clock\":");
        }

        [Test]
        public async Task CustomSettingsWithUnrelatedResolverShouldFallBackToBuiltInMetadata([Values] bool useAsync)
        {
            var formatter = new Formatter(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = UnrelatedJsonContext.Default
            });

            using var stream = new MemoryStream(FakeZabbixServer.Packet(
                "{\"response\":\"success\",\"info\":\"processed: 1; failed: 0; total: 1; seconds spent: 0.000055\"}"));

            var response = useAsync ? await formatter.ReadResponseAsync(stream) : formatter.ReadResponse(stream);

            response.IsSuccess.ShouldBeTrue();
            (await WriteRequest(formatter, useAsync)).ShouldStartWith("{\"request\":\"sender data\"");
        }

        private static async Task<string> WriteRequest(Formatter formatter, bool useAsync)
        {
            var data = new[] { new SendData { Host = "host1", Key = "key1", Value = "1" } };

            using var stream = new MemoryStream();

            if (useAsync)
                await formatter.WriteRequestAsync(stream, data);
            else
                formatter.WriteRequest(stream, data);

            return Encoding.UTF8.GetString(stream.ToArray(), 13, (int)stream.Length - 13);
        }
    }

    [JsonSerializable(typeof(Version))]
    internal partial class UnrelatedJsonContext : JsonSerializerContext
    {
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true)]
    [JsonSerializable(typeof(ZabbixDataMessage))]
    [JsonSerializable(typeof(SenderResponse))]
    internal partial class IndentedZabbixJsonContext : JsonSerializerContext
    {
    }
}