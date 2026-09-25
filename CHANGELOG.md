# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- Response buffers grow as data arrives. A header that declares a huge length no longer causes an
  allocation of that size before any data is received.
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

[Unreleased]: https://github.com/stop-cran/ZabbixSender.Async/compare/v1.4.0-preview.1...HEAD
[1.4.0-preview.1]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.4.0-preview.1
[1.3.0]: https://www.nuget.org/packages/ZabbixSender.Async/1.3.0
[1.2.0]: https://www.nuget.org/packages/ZabbixSender.Async/1.2.0
[1.1.0]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.1.0
[1.0.7]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.0.7
[1.0.6]: https://github.com/stop-cran/ZabbixSender.Async/releases/tag/v1.0.6
