# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `new Formatter(JsonSerializerOptions)` works again with options that have no `TypeInfoResolver`,
  as in 1.3.0. In 1.4.0-preview.2 every call with such options threw `NotSupportedException`, in
  every application. The library's source-generated metadata is now added after the options' own
  resolver, so custom options also work in trimmed and Native AOT apps. The options are copied,
  so the caller's instance is no longer made read-only.
- `Send` throws `ProtocolException` when the reply is JSON `null`. Before, it returned `null`.
- When the caller cancels, `OperationCanceledException.CancellationToken` is the caller's token.
  In 1.4.0-preview.2 it was an internal linked token.

### Changed

- `new Formatter(null)` uses the default settings. Before, it used PascalCase property names,
  which Zabbix rejects by closing the connection.

### Infrastructure

- Before attesting and pushing, the release workflow's publish job checks that the artifact holds
  exactly the packages named for the tag, and that their `.nuspec` files declare that id and
  version. For a stable version, it checks again that the commit is on `master`.
- The connect timeout and connect cancellation tests run on every platform, including Linux CI.
  New tests cover cancelling a send that is stuck writing, and, against Zabbix, custom
  `JsonSerializerOptions` and what Zabbix does with `"clock": null`.

## [1.4.0-preview.2] - 2026-09-25

### Fixed

- `Send` no longer adds about 50 ms to every call. It used to poll for the reply every 50 ms; now
  it reads the reply as soon as it arrives. Against a local Zabbix 7.4, a call dropped from about
  63 ms to 7 ms.
- Trimmed, Native AOT and .NET 10 file-based apps (`dotnet run app.cs`) can send values. Those apps
  disable reflection-based JSON, so every `Send` used to throw `InvalidOperationException`.
  Serialization now uses source-generated metadata, and the library is marked `IsAotCompatible`.
- The whole reply is subject to the timeout. A reply that stalled after its first bytes used to
  block `Send` until the caller cancelled.
- If the server closes the connection before a complete reply, `Send` throws `ProtocolException`
  at once. If the connection is reset, it throws `IOException` at once. Before, both ended in a
  `TaskCanceledException` after the timeout.
- Timeouts throw a `TaskCanceledException` whose `InnerException` is a `TimeoutException`. The
  message says whether connecting or waiting for the reply timed out, and names the address.
  Cancellation by the caller's token throws an `OperationCanceledException`, possibly a
  `TaskCanceledException`, without a `TimeoutException` inner exception. In 1.3.0, cancelling
  while waiting for the reply threw `TaskCanceledException`; catching `OperationCanceledException`
  covers both.
- The connection is disposed when connecting fails or times out. It used to leak.
- A `timeout` of 0 means no limit, like `Timeout.Infinite`. Before, it made every call fail.
- `Timeout.Infinite` (-1) works. Before, every `Send` failed: on Linux with `SocketException`
  (invalid argument), and on Windows with `TaskCanceledException` about 50 ms after sending.
- `ParseInfo()` throws `ProtocolException`, instead of `OverflowException` or
  `ArgumentNullException`, when `Info` is missing or has a count that doesn't fit in an `int`.

### Changed

- The `Sender` constructor throws `ArgumentOutOfRangeException` for a `timeout` below -1. Before,
  such a value failed on every `Send`.
- `SenderSkeleton` with a custom connection factory: the `TcpClient`'s `ReceiveTimeout` limits the
  whole reply, and 0 or -1 means waiting without limit. Before, 0 or -1 made `Send` throw
  `TaskCanceledException` about 50 ms after sending.
- Package metadata:
  - The licence is given as the `Apache-2.0` SPDX expression.
  - The description and tags are more specific.
  - The release notes link to this file at the release's tag.
  - `CHANGELOG.md` is included in the package.

### Added

- Documentation for users and coding agents:
  - The README covers result and error handling, timeouts, dependency injection, batching, value
    formats, Zabbix setup, troubleshooting, limitations, and where to find the docs and source
    of an installed version.
  - The XML docs list the exceptions of every `Send` overload.
  - `AGENTS.md` describes the repository conventions.
- Tests:
  - Unit tests of `Sender` against a fake server: timeouts, cancellation, connection errors,
    concurrency and latency.
  - Integration tests that pin which values Zabbix rejects: disabled hosts and items, *Allowed
    hosts*, HTTP agent items without trapping, and hosts that require encryption. They also cover
    values of the wrong type, Unicode text, a 10,000-value batch and HTTP agent items with
    trapping.
  - The tests run with reflection-based JSON disabled, as in Native AOT apps.

## [1.4.0-preview.1] - 2026-09-25

### Changed

- **The package targets .NET 10 (`net10.0`) only.** Applications still on .NET 9 should stay on
  1.3.0.
- The implementation and documentation follow the
  [Zabbix 7.4 sender protocol](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/zabbix_sender).
  The request bytes on the wire are unchanged, so any server that accepted requests from 1.3.0
  still accepts them. Integration tests run against Zabbix 7.4 (latest stable) and 7.0 (LTS).
- The request header is now written explicitly as the 13-byte little-endian structure from the
  [header specification](https://www.zabbix.com/documentation/7.4/en/manual/appendix/protocols/header_datalen):
  `ZBXD`, flags `0x01`, a 4-byte data length and 4 reserved bytes. Before, it came from
  `BitConverter` in host byte order.
- `Formatter.WriteRequest` serializes synchronously instead of blocking on an asynchronous call.

### Fixed

- Response headers are read in full. A header split across TCP segments used to be misread,
  because only a single `Read` call was made.
- Only the number of bytes the header declares is read from the response body. Anything after the
  declared length is no longer consumed or parsed.
- Truncated or malformed responses raise `ProtocolException` with a specific message.
  Covered cases:
  - a truncated header or body
  - a missing `ZBXD` signature
  - a missing protocol flag, or unknown flag bits
  - a declared length over the protocol's 1 GiB limit
  - invalid compressed data
- Response buffers grow as data arrives, so a header that declares a huge length can't force a
  large allocation.
- Compressed responses (flag `0x02`) and large-packet responses (flag `0x04`) sent by Zabbix are
  now accepted.

### Infrastructure

- Packages are published to nuget.org only from a `v*` tag push, by the new `release.yml` workflow.
  It uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): an
  OIDC token is exchanged for a short-lived key, so there is no stored API key. The build and tests
    run in a separate job without the OIDC token. Each release gets a build provenance attestation and
    a GitHub release with notes from this file. Previously every push to `master` published a package.
- CI (`ci.yml`) builds and tests every pull request against Zabbix 7.4 and 7.0. It also validates
  the workflows and fails if any workflow other than a tag-only one could publish.
- All GitHub Actions are pinned to commit SHAs.
- `compose.yaml` starts a local Zabbix server, web frontend and PostgreSQL for the integration
  tests.
- The integration tests use the Zabbix 7 API: `username` for login and a `Bearer` token. The test
  dependencies were updated to NUnit 4, Refit 15 and System.Text.Json.

## [1.3.0] - 2025-09-06

### Changed

- Migrated to .NET 9.

## [1.2.0] - 2023-01-30

### Changed

- Migrated to .NET 6.
- Replaced Newtonsoft.Json with System.Text.Json. The package has no runtime dependencies.

### Fixed

- Sender connections are cancelled correctly on timeout.

## [1.1.0] - 2021-06-08

### Changed

- Migrated to .NET 5.
- Added integration tests.

## [1.0.7] - 2020-06-25

### Added

- SourceLink support.

## [1.0.6] - 2020-03-25

### Changed

- Targets .NET Core 3.1. Earlier versions targeted .NET Standard 2.0 and .NET Framework 4.6.1.
- Implements, and is tested against, the Zabbix 4.4 sender protocol.

[Unreleased]: https://github.com/stop-cran/ZabbixSender.Async/compare/v1.4.0-preview.2...HEAD
[1.4.0-preview.2]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.4.0-preview.2
[1.4.0-preview.1]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.4.0-preview.1
[1.3.0]: https://www.nuget.org/packages/ZabbixSender.Async/1.3.0
[1.2.0]: https://www.nuget.org/packages/ZabbixSender.Async/1.2.0
[1.1.0]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.1.0
[1.0.7]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.0.7
[1.0.6]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.0.6
