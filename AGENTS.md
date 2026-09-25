# AGENTS.md

Guidance for coding agents working on this repository. To *use* the package, read
[README.md](README.md). For the release process in detail, read [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

| Path | Contents |
|---|---|
| `src/` | The library: one project, `net10.0`, no runtime dependencies. |
| `src/Sender.cs` | Opens the TCP connection; the connect timeout and argument checks. |
| `src/SenderSkeleton.cs` | One request per connection: write the request, then read the reply with the response timeout. |
| `src/Formatter.cs` | The wire format: the 13-byte `ZBXD` header, the JSON body, decompression and limits. |
| `src/ZabbixJsonContext.cs` | Source-generated JSON metadata for every serialized type. |
| `src/SenderResponse.cs`, `src/SenderResponseInfo.cs` | The reply, and parsing of its `info` string. |
| `src/ZabbixSender.Async.xml` | Generated from the XML doc comments by the build. It is committed. |
| `tests/SenderTests.cs` | `Sender` against `FakeZabbixServer`, a loopback fake: connections, timeouts, cancellation and errors. |
| `tests/FormatterTests.cs`, `tests/SenderResponseTests.cs` | The wire format and reply parsing. |
| `tests/AotCompatibilityTests.cs` | Checks that the tests run with reflection-based JSON disabled. |
| `tests/IntegrationTests.cs`, `tests/RestApi.cs` | `[Category("Integration")]`: a real Zabbix server, set up through its JSON-RPC API. |
| `compose.yaml` | Zabbix server, web frontend and PostgreSQL for the integration tests. |
| `tools/check-publication-triggers.py` | The publication gate that CI runs. |
| `.github/workflows/` | `ci.yml` for pull requests and `master`; `release.yml` publishes to nuget.org on `v*` tags. |

## Commands

```shell
dotnet build -c Release
dotnet test -c Release --filter "TestCategory!=Integration"   # unit tests, no Zabbix needed

docker compose up -d --wait                                   # Zabbix 7.4; ZABBIX_VERSION=7.0 for the LTS
dotnet test -c Release --filter "TestCategory=Integration"
docker compose down -v

python tools/check-publication-triggers.py                    # needs pyyaml
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12 -no-color -oneline
```

CI runs the integration tests against both Zabbix 7.4 and 7.0. Run both before changing anything
that depends on server behaviour.

## Conventions

* **Tests first.** Every behaviour change or bug fix needs a test.
  * Prefer a unit test with `FakeZabbixServer`.
  * Add an integration test when the behaviour depends on how Zabbix itself responds.
* **Exceptions in tests.** Shouldly replaces the exception of a cancelled task with a new
  `TaskCanceledException`, which loses the `InnerException`. To assert on it, use the `CatchAsync`
  helper in `SenderTests`.
* **Reflection-free JSON.** The test project sets `JsonSerializerIsReflectionEnabledByDefault` to
  `false`, as trimmed, Native AOT and file-based apps do.
  * Library code must use `JsonTypeInfo`-based serializer calls, and every serialized type must be
    listed in `ZabbixJsonContext`.
  * The tests' own JSON uses `AotCompatibilityTests.ReflectionJson`.
  * The library project sets `IsAotCompatible`, so a build with trim or AOT warnings means a
    regression.
* **Documentation.**
  * Every public member has XML docs, including `<exception>` tags. After changing doc comments,
    build and commit the regenerated `src/ZabbixSender.Async.xml`.
  * `README.md` is packed into the package and shown on nuget.org, so its links must be absolute.
  * Keep the README's *Results and errors* table in sync with the tests.
* **Changelog.** Add user-visible changes under `## [Unreleased]` in `CHANGELOG.md`.
* **Compatibility.**
  * Don't add runtime dependencies.
  * Don't make breaking public API changes without a major version bump.
  * The wire format must stay compatible with Zabbix 7.0 LTS.

## Releasing

Packages are published only by `release.yml`, when a `v*` tag is pushed, through nuget.org trusted
publishing. There is no API key. Never push packages manually, and never add workflows or steps that
could publish from anything other than a tag; the publication gate fails such changes. The steps
are in [CONTRIBUTING.md](CONTRIBUTING.md#releasing).