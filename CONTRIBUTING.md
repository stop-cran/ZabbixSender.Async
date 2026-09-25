# Contributing

## Prerequisites

* The .NET 10 SDK. [global.json](global.json) pins the SDK band.
* Docker, for the integration tests.

## Building and testing

```shell
dotnet build
dotnet test --filter "TestCategory!=Integration"   # unit tests only, no Zabbix needed
```

The unit tests run `Sender` against `FakeZabbixServer`, a loopback server in the test project, so
timeouts, dropped connections and malformed replies can be tested without Zabbix. The test project
disables reflection-based JSON serialization, as trimmed and Native AOT apps do, so the whole suite
also checks AOT compatibility.

The integration tests (`[Category("Integration")]`) run the whole flow against a real Zabbix server:

1. Create a host group, host and trapper item through the JSON-RPC API.
2. Send values with `Sender`.
3. Read the values back from history.

They also check which values Zabbix rejects, for example for disabled items or a mismatched
*Allowed hosts*, so the README's error table stays accurate.

[compose.yaml](compose.yaml) starts Zabbix with PostgreSQL:

```shell
docker compose up -d                       # Zabbix 7.4
dotnet test                                # all tests; waits up to 3 minutes for Zabbix to start
docker compose down -v
```

To test against another version, such as the 7.0 LTS, start from an empty database, because it is
created for one version: `docker compose down -v`, then `ZABBIX_VERSION=7.0 docker compose up -d`
(in PowerShell: `$env:ZABBIX_VERSION='7.0'; docker compose up -d`).

The tests use these environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `ZABBIX_API_URL` | `http://localhost:8080/api_jsonrpc.php` | Zabbix frontend JSON-RPC endpoint |
| `ZABBIX_SERVER` | `127.0.0.1` | Zabbix server or proxy that receives sender data on port 10051 |

The tests log in as `Admin` / `zabbix`, the default credentials of a new installation. Every object
they create gets a unique name and is deleted afterwards.

`src/ZabbixSender.Async.xml` is generated from the XML doc comments on build. It is committed, so
commit it whenever the doc comments change.

[AGENTS.md](AGENTS.md) lists the repository layout and conventions in one place, for people and
coding agents alike.

## Continuous integration

[ci.yml](.github/workflows/ci.yml) runs on every pull request and every push to `master`:

* **build**: builds, then runs all tests against Zabbix 7.4 and 7.0. Test results and coverage are
  published from this job.
* **lint**: validates the workflows with [actionlint](https://github.com/rhysd/actionlint).
* **publication-security**: runs [tools/check-publication-triggers.py](tools/check-publication-triggers.py).
  It fails if any workflow that can publish to nuget.org is reachable other than by a tag push, or
  uses an action not pinned to a commit SHA.

## Releasing

Packages are published only by [release.yml](.github/workflows/release.yml), and only when a tag
matching `v*` is pushed. Publishing uses
[nuget.org trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the
workflow exchanges its GitHub OIDC token for a short-lived nuget.org key, so the repository stores
no API key.

To release version `X.Y.Z`, or a preview such as `X.Y.Z-preview.N`:

1. Set `<Version>` in [src/ZabbixSender.Async.csproj](src/ZabbixSender.Async.csproj).
   `<PackageReleaseNotes>` links to this version's section of the changelog on its own.
2. In [CHANGELOG.md](CHANGELOG.md), move the entries under `## [Unreleased]` into a new
   `## [X.Y.Z] - YYYY-MM-DD` section, and update the link references at the bottom. The section
   becomes the GitHub release notes, and the release fails if the section is missing or empty.
3. Get the change reviewed. A stable version must be merged to `master` first. A preview can be
   tagged on a pull request branch, so it can be tried before merging.
4. Tag the commit and push the tag:

   ```shell
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

The workflow has two jobs.

The **build** job has read-only access to the repository and no OIDC token, so the code it runs
(restore, build, tests, the Zabbix containers) cannot obtain a nuget.org key. It:

1. Checks that the tag matches `<Version>`, and that a stable version is on `master`.
2. Extracts the release notes.
3. Runs all tests against Zabbix.
4. Packs the package, and checks that it contains the README, licence, XML docs and symbols, and
   that its source link points at the tagged commit.
5. Uploads the packages and release notes as a workflow artifact.

The **publish** job runs in the `nuget` environment and is the only job with an OIDC token. It
does not check out or build anything. It:

1. Downloads the artifact, which fails if its digest doesn't match the upload.
2. Checks, without running repository code, that the artifact holds exactly the `.nupkg` and
   `.snupkg` named for the tag, that their `.nuspec` files declare `ZabbixSender.Async` and the
   tag's version, and that a stable version's commit is on `master`.
3. Records a build provenance attestation.
4. Exchanges the OIDC token for a short-lived nuget.org key, and pushes the `.nupkg` and `.snupkg`.
5. Creates a GitHub release. The release is marked as a prerelease when the version has a `-`
   suffix.

The push uses `--skip-duplicate`. If the publish job fails, use *Re-run failed jobs* within the
artifact's 7-day retention: the job publishes the same files the build job verified. Re-running the
whole workflow rebuilds the package, so the attestation would describe a rebuilt file rather than the
one already published. nuget.org never lets a published version be replaced. To fix a bad
release, unlist it on nuget.org and release a new version.

To verify that a package was built by this workflow, download it from the GitHub release. The copy
on nuget.org, and so in the global packages folder, also carries nuget.org's repository signature,
so its digest differs and it does not verify.

```shell
gh release download vX.Y.Z --repo stop-cran/ZabbixSender.Async --pattern '*.nupkg'
gh attestation verify ZabbixSender.Async.X.Y.Z.nupkg --repo stop-cran/ZabbixSender.Async
```

### One-time configuration

These settings live outside the repository. Renaming the workflow file or the environment breaks
publishing until they are updated.

| Where | Setting |
|---|---|
| nuget.org → *Trusted Publishing* | Repository owner `stop-cran`, repository `ZabbixSender.Async`, workflow file `release.yml`, environment `nuget` |
| GitHub → *Settings → Environments → nuget* | Deployment tags restricted to `v*` |
