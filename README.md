# Synopsis

This package provide a tool to send data to Zabbix in the same way as zabbix_sender tool. It implements [Zabbix Sender Protocol 4.0](https://www.zabbix.org/wiki/Docs/protocols/zabbix_sender/4.0).

# Installation

NuGet package is available [here](https://www.nuget.org/packages/ZabbixSender.Async/).

```PowerShell
PM> Install-Package ZabbixSender.Async
```

# Example

```C#
var sender = new ZabbixSender.Async.Sender("192.168.0.10");
var response = sender.Send("MonitoredHost1", "trapper.item1", "10");
Console.WriteLine(reponse.Response); // "success" or "fail"
Console.WriteLine(response.Info);    // e.g. "Processed 1 Failed 0 Total 1 Seconds spent 0.000253"
```