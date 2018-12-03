# Overview

The package provides a tool to send data to Zabbix in the same way as [zabbix_sender](https://www.zabbix.com/documentation/4.0/ru/manual/concepts/sender) tool. It implements [Zabbix Sender Protocol 4.0](https://www.zabbix.org/wiki/Docs/protocols/zabbix_sender/4.0).

# Installation

NuGet package is available [here](https://www.nuget.org/packages/ZabbixSender.Async/).

```PowerShell
PM> Install-Package ZabbixSender.Async
```

# Example

```C#
var sender = new ZabbixSender.Async.Sender("192.168.0.10");
var response = sender.Send("MonitoredHost1", "trapper.item1", "12");
Console.WriteLine(reponse.Response); // "success" or "fail"
Console.WriteLine(response.Info);    // e.g. "Processed 1 Failed 0 Total 1 Seconds spent 0.000253"
```

# Remarks

Note, that in order for the request to be accepted, hosts like `MonitoredHost1` above, should be [configured](https://www.zabbix.com/documentation/4.0/manual/config/hosts/host). The same should be done for the items (like `trapper.item1` above). The item(s) type should be [Zabbix trapper](https://www.zabbix.com/documentation/4.0/manual/config/items/itemtypes/trapper). Also the values passed (`12` above) should respect the type of information, configured for each item.