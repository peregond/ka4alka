# Portable text catalog regression harness

Run on Linux, Windows, or macOS with .NET 10:

```sh
dotnet run --project catalog-text-tests/Kachalka.CatalogTextTests.csproj
```

The harness links the real `TextCatalogDownload.cs`, `SharedCatalog.cs`, `BundledCatalog.cs`,
`SeasonalCatalog.cs`, `UnifiedSearch.cs`, and their shared-catalog/search tests. Transport tests exercise
real manifest validation, immutable URLs, digest checks, bounded gzip expansion,
truncation rejection, cancellation, and the manifest-404 compatibility boundary.
Shared catalog tests exercise real restore/refresh scheduling, rolling weekly
checks, nonblocking pages during downloads, joined refreshes, persistence of
successful unchanged checks, retry behavior, and invalid-cache repair.
Seasonal tests check explicit UTC timestamps in both `Z` and `+00:00` forms,
reject ambiguous or nonzero offsets, and verify inclusive start/exclusive end
boundaries. Unified search tests run the production local-result publication,
category identity, ranking, partial-failure, and cancellation logic.
Bundled catalog tests gate parsing of the actual embedded seed on a worker
thread, check that synchronous readers retain starter cards, and verify that
concurrent or cancelled waits share the same full-seed load.

`TestHost.cs` supplies small adapters for WPF-bound media models,
fixture HTTP responses, and disk access. Its `OnlineIndexClient.Media` converts
test fixtures and embedded rows using a reduced field set. This harness does **not** verify the production online row
parser, complete embedded metadata, `CacheFiles` implementation, IMDb supplement or filmography matching,
or UI behavior. Native
application build and Windows smoke tests remain responsible for those pieces.
