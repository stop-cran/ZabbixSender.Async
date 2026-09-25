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
        public void CustomSettingsWithoutResolverShouldThrow()
        {
            var formatter = new Formatter(new JsonSerializerOptions());

            using var stream = new MemoryStream();

            Should.Throw<NotSupportedException>(() => formatter.WriteRequest(stream, new[] { new SendData() }));
        }
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