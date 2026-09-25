# ZabbixSender.Async [![NuGet](https://img.shields.io/nuget/v/ZabbixSender.Async.svg)](https://www.nuget.org/packages/ZabbixSender.Async) [![CI](https://github.com/stop-cran/ZabbixSender.Async/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/stop-cran/ZabbixSender.Async/actions/workflows/ci.yml) [![Coverage Status](https://coveralls.io/repos/github/stop-cran/ZabbixSender.Async/badge.svg?branch=master)](https://coveralls.io/github/stop-cran/ZabbixSender.Async?branch=master)

An async .NET client for the [Zabbix sender protocol](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/zabbix_sender). It pushes values from .NET code to Zabbix [trapper items](https://www.zabbix.com/documentation/7.4/en/manual/config/items/itemtypes/trapper), like the [zabbix_sender](https://www.zabbix.com/documentation/7.4/en/manual/concepts/sender) utility, and returns how many values the server accepted.

* Sends one value or many in a single request, with optional per-value timestamps.
* Supports timeouts and cancellation.
* Thread-safe and dependency-free.
* Works in trimmed and Native AOT applications.
* Tested against Zabbix 7.4 (the latest stable release) and 7.0 LTS.

It does not read data from Zabbix or create hosts and items; use the [Zabbix API](https://www.zabbix.com/documentation/7.4/en/manual/api) for that. It is not a Zabbix agent: it only pushes values to trapper items.

## Installation

```shell
dotnet add package ZabbixSender.Async
```

The package requires .NET 10. Applications still on .NET 9 can use version [1.3.0](https://www.nuget.org/packages/ZabbixSender.Async/1.3.0). To try a preview version, add `--prerelease`.

## Quick start

In Zabbix, create a host named `web-01` with an item of type *Zabbix trapper* and key `app.orders.count` (see [Zabbix setup](#zabbix-setup)). Then:

```csharp
using ZabbixSender.Async;

var sender = new Sender("zabbix.example.com"); // port 10051, 500 ms timeout
var response = await sender.Send("web-01", "app.orders.count", "42");

if (!response.IsSuccess || response.ParseInfo().Failed > 0)
    Console.WriteLine($"Zabbix did not accept the value: {response.Info}");
```

`response.Info` is the server's summary, for example `processed: 1; failed: 0; total: 1; seconds spent: 0.000253`.

To send several values in one request:

```csharp
using System.Globalization;

var response = await sender.Send(new[]
{
    new SendData { Host = "web-01", Key = "app.orders.count", Value = "42" },
    new SendData { Host = "web-01", Key = "app.latency", Value = 12.5.ToString(CultureInfo.InvariantCulture),
                   Clock = DateTimeOffset.UtcNow.AddMinutes(-1) }
}, cancellationToken);

if (response.IsSuccess)
{
    var info = response.ParseInfo();
    Console.WriteLine($"{info.Processed} of {info.Total} processed, {info.Failed} failed");
}
```

## API at a glance

All types are in the `ZabbixSender.Async` namespace.

| Member | Purpose |
|---|---|
| `new Sender(string zabbixServer, int port = 10051, int timeout = 500, int bufferSize = 1024)` | The client, for one Zabbix server or proxy. No connection is opened until a value is sent. |
| `ISender` | The interface `Sender` implements, for dependency injection and test doubles. |
| `Send(string host, string key, string value, CancellationToken cancellationToken = default)` | Sends one value. |
| `Send(IEnumerable<SendData> data, CancellationToken cancellationToken = default)`, `Send(params SendData[] data)` | Sends many values in one request. |
| `SendData { Host, Key, Value, Clock }` | One value. `Host` is the host's technical name, `Key` the item key, `Value` a string, and `Clock` an optional `DateTimeOffset?`. |
| `SenderResponse { Response, Info, IsSuccess, ParseInfo() }` | The server's reply. `IsSuccess` means `Response == "success"`. |
| `SenderResponseInfo { Processed, Failed, Total, TimeSpent }` | `Info` parsed by `ParseInfo()`. |
| `ProtocolException` | The reply is not a valid Zabbix sender protocol response. |
| `SenderSkeleton`, `IFormatter`, `Formatter` | Building blocks of `Sender`: a custom connection factory, and the wire format with custom JSON settings. Most applications don't need them. |

The XML documentation for every public member, including the exceptions each method throws, ships in the package and shows up in IDEs.

## Results and errors

A `Send` call returns normally whenever the server replies, even if it rejects every value. Check the reply, not just for exceptions:

| Situation | What you get |
|---|---|
| Every value accepted | `IsSuccess` is `true`, and `ParseInfo().Failed` is 0. |
| Some or all values rejected. Causes: unknown host or item key; disabled host or item; the item is not a trapper item; the sender's address is not in the item's *Allowed hosts*; the host accepts only encrypted connections. | `IsSuccess` is still `true`, and `ParseInfo().Failed` is above 0. The reply only has counts: it does not say which values failed or why. See [troubleshooting](#troubleshooting-failed-values). |
| A value doesn't match the item's *Type of information*, for example text for a numeric item | Counted as **processed**, with `Failed` 0, but the value is not stored. The item becomes *Not supported*, with an error such as `Value of type "string" is not suitable for value type "Numeric (unsigned)"`. |
| The server refused the whole request | `IsSuccess` is `false`, and `Info` has the reason. `ParseInfo()` throws `ProtocolException`. |
| Nothing listens on the port, the host name doesn't resolve, or the network is unreachable | `SocketException`. On Windows, a refused connection is retried for about 2 seconds, so with a shorter `timeout` it ends as a connect timeout instead; see [Timeouts](#timeouts-and-cancellation). |
| Connecting or waiting for the reply took longer than `timeout` | `TaskCanceledException`, whose `InnerException` is a `TimeoutException`. The message says which step timed out and the address. |
| Your `CancellationToken` was cancelled | `OperationCanceledException`, possibly a `TaskCanceledException`, without a `TimeoutException` inner exception |
| The connection was reset while sending or receiving | `IOException`, usually with a `SocketException` inner exception |
| The port doesn't belong to a Zabbix trapper, or the connection closed before a complete reply, or the reply is truncated or malformed | `ProtocolException` |
| The `timeout` passed to the `Sender` constructor is below -1 | `ArgumentOutOfRangeException` |

A complete pattern:

```csharp
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ZabbixSender.Async;

try
{
    var response = await sender.Send(data, cancellationToken);

    if (!response.IsSuccess)
        logger.LogError("Zabbix refused the request: {Info}", response.Info);
    else if (response.ParseInfo() is { Failed: > 0 } info)
        logger.LogWarning("Zabbix rejected {Failed} of {Total} values", info.Failed, info.Total);
}
catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException)
{
    logger.LogWarning(ex, "Zabbix timed out"); // connect or response timeout
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    throw; // the caller cancelled
}
catch (Exception ex) when (ex is SocketException or IOException or ProtocolException)
{
    logger.LogWarning(ex, "Could not send to Zabbix");
}
```

## Timeouts and cancellation

* `timeout` is in milliseconds, 500 by default. It limits connecting, including resolving the host name, and separately limits waiting for the reply. So a call can take up to twice the timeout, plus the time to write the request.
* Writing the request is limited only by the `CancellationToken`.
* `0` or `Timeout.Infinite` (-1) removes the limit. Values below -1 throw `ArgumentOutOfRangeException`.
* On Windows, the OS retries a refused connection for about 2 seconds. With the default timeout, a closed port therefore shows up as a connect timeout rather than a `SocketException`. On Linux, a refused connection fails immediately with `SocketException`.
* To bound the whole call, pass a `CancellationToken`, for example from `new CancellationTokenSource(TimeSpan.FromSeconds(2))`.

## Dependency injection and testing

`Sender` is immutable and thread-safe, so create one per Zabbix server and share it:

```csharp
builder.Services.AddSingleton<ISender>(_ => new Sender(
    builder.Configuration["Zabbix:Server"]!,
    timeout: 2000));
```

In unit tests, replace `ISender` with a fake or a mock. `SenderResponse` has public setters, so a reply is easy to construct:

```csharp
var accepted = new SenderResponse
{
    Response = "success",
    Info = "processed: 1; failed: 0; total: 1; seconds spent: 0.000010"
};
```

## Batching and throughput

* Every `Send` opens a new TCP connection, sends one request, reads one reply and closes the connection. There is no connection pooling.
* To send many values, put them in one `Send` call: one round trip instead of many. A request with 10,000 values is covered by the integration tests.
* `Send` may be called concurrently from many threads. Each call uses its own connection.

## Values and timestamps

* `Value` is a string. Format numbers with `CultureInfo.InvariantCulture`, so the decimal separator is always a dot. Zabbix has no Boolean type; send `0` and `1` to a numeric item.
* The value must match the item's *Type of information*. *Numeric (unsigned)* takes non-negative integers, and *Numeric (float)* takes decimal numbers. *Text* and *Log* take any text, including Unicode and new lines, and *Character* takes short text.
* `Clock` is optional. The library sends whole seconds and drops the fractional part. Without `Clock`, the server uses the time it received the value.

## Zabbix setup

For a value to be accepted:

1. The host exists, and `SendData.Host` is its *Host name* (the technical name, not the *Visible name*).
2. The host has an item with the key `SendData.Key`. The item's type is *Zabbix trapper*, or *HTTP agent* with *Enable trapping* checked.
3. The host and the item are enabled.
4. The item's *Allowed hosts* is empty, or includes the address the Zabbix server sees the connection from. Watch for NAT and proxies.
5. The host's *Encryption* settings allow *No encryption* for connections from the host, because this library doesn't support TLS.
6. The server has loaded the host and item into its configuration cache. New or changed hosts and items become visible after the next cache update, controlled by `CacheUpdateFrequency` in the server configuration. Until then their values count as failed. `zabbix_server -R config_cache_reload` forces an update.

The same through the [JSON-RPC API](https://www.zabbix.com/documentation/7.4/en/manual/api/reference/item/create): `host.create` with `host`, then `item.create` with `key_`, `hostid`, `type: 2` (Zabbix trapper) and `value_type`: `0` float, `1` character, `2` log, `3` unsigned, `4` text. `trapper_hosts` sets *Allowed hosts*. [IntegrationTests.cs](https://github.com/stop-cran/ZabbixSender.Async/blob/master/tests/IntegrationTests.cs) has working examples.

### Troubleshooting failed values

When `Failed` is above 0:

* Go through the [setup](#zabbix-setup) list above for each value. The reply doesn't say which value failed; to find it, send the values one at a time.
* Send the same value with `zabbix_sender -z <server> -s "<host>" -k <key> -o <value> -vv` to rule out the library.
* If a value counts as processed but doesn't show up in *Latest data*, check whether the item is *Not supported*, and read its error.

## Limitations

* No TLS: neither PSK nor certificates. Send to hosts that accept unencrypted connections, or use `zabbix_sender`.
* One connection per `Send`, with no connection reuse.
* Requests are not compressed. Compressed replies are accepted.
* Timestamps have whole-second precision; the protocol's `ns` field is not sent.
* Only the sender protocol (`sender data` requests) is implemented; the agent protocols are not.

## Trimming, Native AOT and file-based apps

The library uses source-generated JSON serialization and is marked `IsAotCompatible`. It works in trimmed apps, in Native AOT apps, and in .NET 10 file-based apps (`dotnet run app.cs`), which disable reflection-based JSON. Custom `JsonSerializerOptions` passed to `Formatter` work there too: the library adds its source-generated metadata after their `TypeInfoResolver`, so a resolver of your own takes precedence.

Custom options replace the defaults, so keep `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`. Without camelCase names, Zabbix closes the connection and `Send` throws `ProtocolException`. Without `WhenWritingNull`, values without `Clock` are sent with `"clock": null`, and Zabbix does not store them. They count as failed or, when every value in the request has `"clock": null`, are not counted at all (`processed: 0; failed: 0; total: 0`).

## Running Zabbix locally

The repository's [compose.yaml](https://github.com/stop-cran/ZabbixSender.Async/blob/master/compose.yaml) starts a Zabbix server with the trapper on port `10051`, the web frontend on http://localhost:8080 (user `Admin`, password `zabbix`) and the API at http://localhost:8080/api_jsonrpc.php, backed by PostgreSQL:

```shell
docker compose up -d                        # Zabbix 7.4
docker compose down -v                      # stop and delete the data
```

To run another version, delete the data first, because the database is created for one version: `docker compose down -v`, then `ZABBIX_VERSION=7.0 docker compose up -d` (in PowerShell: `$env:ZABBIX_VERSION='7.0'; docker compose up -d`).

To set up Zabbix in containers yourself, see the [official instructions](https://www.zabbix.com/documentation/7.4/en/manual/installation/containers).

## For AI agents and tools

This section is for coding agents and tools working in a project that uses the package, without its source code.

* **Local documentation.** After a restore, the package folder is `<global-packages>/zabbixsender.async/<version>/`. It contains:
  * `README.md` (this file)
  * `CHANGELOG.md`
  * `lib/net10.0/ZabbixSender.Async.xml`, the XML docs for every public member, with exceptions and remarks

  `dotnet nuget locals global-packages --list` prints the global packages folder. It is `~/.nuget/packages` unless `NUGET_PACKAGES` overrides it.
* **The version in use.** `dotnet list package --include-transitive` shows it, and so does `obj/project.assets.json`.
* **Source code.** The source of version `X.Y.Z` is at `https://github.com/stop-cran/ZabbixSender.Async/tree/vX.Y.Z`; tags exist from v1.4.0-preview.1. The `.nuspec` inside the package records the exact commit under `<repository commit="...">`, so for any version the source is at `https://github.com/stop-cran/ZabbixSender.Async/tree/<commit>`. SourceLink and a symbol package (`.snupkg`) on nuget.org let debuggers step into the source.
* **Behaviour specification.** The tests are the most precise description of behaviour:
  * [SenderTests.cs](https://github.com/stop-cran/ZabbixSender.Async/blob/master/tests/SenderTests.cs): connections, timeouts, cancellation and errors, against a fake server
  * [IntegrationTests.cs](https://github.com/stop-cran/ZabbixSender.Async/blob/master/tests/IntegrationTests.cs): a real Zabbix server; which values are accepted or rejected, large batches, timestamps and Unicode
  * [FormatterTests.cs](https://github.com/stop-cran/ZabbixSender.Async/blob/master/tests/FormatterTests.cs): the wire format
  * [SenderResponseTests.cs](https://github.com/stop-cran/ZabbixSender.Async/blob/master/tests/SenderResponseTests.cs): parsing the reply
* **Protocol.** See the [sender protocol](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/zabbix_sender) and the [header format](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/header_datalen).
* **Provenance.** Every package since 1.4.0-preview.1 is built and published by GitHub Actions, with a build provenance attestation. The attestation covers the package as built, which is attached to the GitHub release. The copy on nuget.org, and so in the global packages folder, also carries nuget.org's repository signature, so its digest differs and it does not verify. To check a version: `gh release download vX.Y.Z --repo stop-cran/ZabbixSender.Async --pattern '*.nupkg'`, then `gh attestation verify ZabbixSender.Async.X.Y.Z.nupkg --repo stop-cran/ZabbixSender.Async`.
* **Bugs and questions.** Open an issue at https://github.com/stop-cran/ZabbixSender.Async/issues. Include the package version, the Zabbix version and the `Response`/`Info` or exception.
* **Changing this library.** Agents working on this repository itself should read [AGENTS.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/AGENTS.md).

## Compatibility

* **.NET:** 10.
* **Zabbix:** every change is tested against 7.4 and 7.0 LTS, as a server receiving the data. A Zabbix proxy accepts the same protocol.
* **Versioning:** the package follows [Semantic Versioning](https://semver.org/). [CHANGELOG.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/CHANGELOG.md) lists the changes in each version.

## Contributing and releases

* [CONTRIBUTING.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/CONTRIBUTING.md): building, testing and the release process.
* [AGENTS.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/AGENTS.md): conventions for coding agents working on the repository.