# Overview [![NuGet](https://img.shields.io/nuget/v/ZabbixSender.Async.svg)](https://www.nuget.org/packages/ZabbixSender.Async) [![Build Status](https://travis-ci.com/stop-cran/ZabbixSender.Async.svg?branch=master)](https://travis-ci.com/stop-cran/ZabbixSender.Async) [![Coverage Status](https://coveralls.io/repos/github/stop-cran/ZabbixSender.Async/badge.svg?branch=)](https://coveralls.io/github/stop-cran/ZabbixSender.Async?branch=)

The package provides a tool to send data to Zabbix in the same way as [zabbix_sender](https://www.zabbix.com/documentation/4.0/ru/manual/concepts/sender) tool. It implements [Zabbix Sender Protocol 4.0](https://www.zabbix.org/wiki/Docs/protocols/zabbix_sender/4.0).

# Installation

NuGet package is available [here](https://www.nuget.org/packages/ZabbixSender.Async/).

```PowerShell
PM> Install-Package ZabbixSender.Async
```

# Example

```C#
var sender = new ZabbixSender.Async.Sender("192.168.0.10");
var response = await sender.Send("MonitoredHost1", "trapper.item1", "12");
Console.WriteLine(reponse.Response); // "success" or "fail"
Console.WriteLine(response.Info);    // e.g. "Processed 1 Failed 0 Total 1 Seconds spent 0.000253"
```

# Remarks

In order for the request above to be successfully processed, the host `MonitoredHost1` should be [configured](https://www.zabbix.com/documentation/4.0/manual/config/hosts/host). The same should be [done](https://www.zabbix.com/documentation/4.0/manual/config/items/item) for the item `trapper.item1`. The item(s) type should be [Zabbix trapper](https://www.zabbix.com/documentation/4.0/manual/config/items/itemtypes/trapper). Also the value `12` should respect the type of information, configured for the item.