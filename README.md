# Overview [![NuGet](https://img.shields.io/nuget/v/ZabbixSender.Async.svg)](https://www.nuget.org/packages/ZabbixSender.Async) [![CI](https://github.com/stop-cran/ZabbixSender.Async/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/stop-cran/ZabbixSender.Async/actions/workflows/ci.yml) [![Coverage Status](https://coveralls.io/repos/github/stop-cran/ZabbixSender.Async/badge.svg?branch=master)](https://coveralls.io/github/stop-cran/ZabbixSender.Async?branch=master)

The package provides a tool to send data to Zabbix in the same way as the [zabbix_sender](https://www.zabbix.com/documentation/7.4/en/manual/concepts/sender) tool. It implements [Zabbix Sender Protocol 7.4](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/zabbix_sender).

# Requirements

* .NET 10. Applications still on .NET 9 should use version [1.3.0](https://www.nuget.org/packages/ZabbixSender.Async/1.3.0).
* A Zabbix server or proxy. Every change is tested against Zabbix 7.4, the latest stable release, and Zabbix 7.0 LTS.

# Installation

The NuGet package is available [here](https://www.nuget.org/packages/ZabbixSender.Async/).

```shell
dotnet add package ZabbixSender.Async
```

To try a preview version, add `--prerelease`.

# Example

```C#
using ZabbixSender.Async;

var sender = new Sender("192.168.0.10");
var response = await sender.Send("MonitoredHost1", "trapper.item1", "12");
Console.WriteLine(response.IsSuccess); // true when Response is "success"
Console.WriteLine(response.Info);      // e.g. "processed: 1; failed: 0; total: 1; seconds spent: 0.000253"
```

You can send several values in one request. Each value can optionally carry its own timestamp. The `Info` field can also be read in structured form:

```C#
var response = await sender.Send(new[]
{
    new SendData { Host = "MonitoredHost1", Key = "trapper.item1", Value = "12" },
    new SendData { Host = "MonitoredHost1", Key = "trapper.item2", Value = "3.14", Clock = DateTimeOffset.UtcNow.AddMinutes(-1) }
}, cancellationToken);

var info = response.ParseInfo();
Console.WriteLine($"{info.Processed} of {info.Total} processed, {info.Failed} failed");
```

The `Sender` constructor accepts the server port (default `10051`), the connect, send and receive timeout in milliseconds (default `500`), and the socket buffer size.

# Remarks

For the requests above to be processed, the host `MonitoredHost1` must be [configured](https://www.zabbix.com/documentation/7.4/en/manual/config/hosts/host). The same must be [done](https://www.zabbix.com/documentation/7.4/en/manual/config/items/item) for the items `trapper.item1` and `trapper.item2`, and they must be of type [Zabbix trapper](https://www.zabbix.com/documentation/7.4/en/manual/config/items/itemtypes/trapper), or [HTTP agent](https://www.zabbix.com/documentation/7.4/en/manual/config/items/itemtypes/http) with trapping enabled. If an item restricts *Allowed hosts*, include the address the data is sent from. Each value must match the item's configured *Type of information*.

A value the server rejects doesn't throw. It is counted in `ParseInfo().Failed`. A malformed or truncated response from the server throws `ProtocolException`.

# Running Zabbix locally

The repository's [compose.yaml](https://github.com/stop-cran/ZabbixSender.Async/blob/master/compose.yaml) starts a Zabbix server on port `10051` and the web frontend on http://localhost:8080 (user `Admin`, password `zabbix`), backed by PostgreSQL:

```shell
docker compose up -d                        # Zabbix 7.4
ZABBIX_VERSION=7.0 docker compose up -d     # or another version
```

To set up Zabbix in containers yourself, see the [official instructions](https://www.zabbix.com/documentation/7.4/en/manual/installation/containers).

# Contributing and releases

* [CHANGELOG.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/CHANGELOG.md): what changed in each version.
* [CONTRIBUTING.md](https://github.com/stop-cran/ZabbixSender.Async/blob/master/CONTRIBUTING.md): building, testing and the release process.
