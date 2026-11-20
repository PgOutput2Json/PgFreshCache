# AGENTS.md

Guidance for coding agents working in this repository.

## Commands

```bash
# Build everything (the package is created on build: GeneratePackageOnBuild)
dotnet build PgFreshCache.sln

# Unit tests (no database server)
dotnet test PgFreshCache.Tests/PgFreshCache.Tests.csproj

# Integration tests (need Docker - they start PostgreSQL with Testcontainers; PG_IMAGE picks the image)
dotnet test PgFreshCache.IntegrationTests/PgFreshCache.IntegrationTests.csproj
```

The library targets `net8.0`, `net9.0` and `net10.0`: EF Core 9 on net8.0/net9.0, EF Core 10 on net10.0. The test projects target
net8.0 and net10.0 to cover both. CI (`.github/workflows/tests.yml`) runs both test projects on every pull request and on `main`,
and the integration tests a second time with `TZ=Europe/Zagreb`, since the runners are on UTC. `TreatWarningsAsErrors` is on.

PgFreshCache is a layer over [PgOutput2Json](https://github.com/PgOutput2Json/PgOutput2Json) (`PgOutput2Json.Sqlite`): test what
PgFreshCache does (the DI registration, the cache `DbContext`, the options, the worker), not PgOutput2Json itself.

## Package and Releases

- `PgFreshCache.Lite/PgFreshCache.Lite.csproj` has the version and the package settings: symbols (`.snupkg`) with Source Link,
  and Release builds as `ContinuousIntegrationBuild` (deterministic, paths normalized to `/_/`), so a package built on any
  machine maps its sources to the commit on GitHub. `./publish.sh` builds the package in Release and pushes it to nuget.org (it
  asks for the API key, and warns when HEAD is not tagged `v<version>`).
- The public API (PublicApiAnalyzers): `PublicAPI.Shipped.txt` (as of the last release on nuget.org) and `PublicAPI.Unshipped.txt`
  (changes since, removals as `*REMOVED*`) next to the project. A public change fails the build (RS0016, RS0017) until it is
  listed: `dotnet format analyzers PgFreshCache.Lite/PgFreshCache.Lite.csproj --diagnostics RS0016 --severity info` adds new
  entries (it does not add removals). New types are internal unless users need them; the tests reach internals through
  `InternalsVisibleTo`.
- Package validation (`EnablePackageValidation`): the package is compared with `PackageValidationBaselineVersion` (a release on
  nuget.org, downloaded once) and between its target frameworks. A removed or changed API fails the Release build (CP0002 etc.)
  unless it is in `PgFreshCache.Lite/CompatibilitySuppressions.xml` (generate it with `-p:ApiCompatGenerateSuppressionFile=true`,
  only for an intended break). The baseline is the last minor or major release (x.y.0), `2.1.0` for now: patch releases do
  not move it. When a minor or major release is on nuget.org, it becomes the baseline, the suppressions are deleted, and
  `PublicAPI.Unshipped.txt` moves into `PublicAPI.Shipped.txt` (the `*REMOVED*` entries delete their shipped lines).
