# Implementation plan review

Reviewed on 22 October 2026 against the supplied draft. The draft is a product proposal, not authorization to publish packages, reserve names, contact developers, or operate external accounts.

## Findings

1. **Header emission is already in Asp.Versioning.** Its current [version-policy documentation](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-policies.html) explicitly supports RFC 9745 Deprecation, RFC 8594 Sunset and RFC 8288 links. The [10.0 preview release notes](https://github.com/dotnet/aspnet-api-versioning/releases/tag/v10.0.0-preview.1) describe deprecation support. Treat headers as necessary integration, not the main differentiator.
2. **OpenAPI is stable.** [Asp.Versioning.OpenApi 10.2.3](https://www.nuget.org/packages/Asp.Versioning.OpenApi/10.2.3) is available; its history shows 10.0.0 GA on 30 July 2026. The draft's April RC observation is out of date. The [Http adapter dependency](https://www.nuget.org/packages/Asp.Versioning.Http/10.2.3) targets .NET 10. This preview targets .NET 10 throughout; supporting .NET 8 would require a separate adapter dependency matrix.
3. **Differentiation needs validation.** Retirement enforcement, brownouts, a consolidated registry, and bounded in-app telemetry form a useful implementation scope. The issue scan found [a discussion of deprecating default versions](https://github.com/dotnet/aspnet-api-versioning/discussions/968) and [a feature request mentioning custom 410 behavior](https://github.com/dotnet/aspnet-api-versioning/issues/1183). These are relevant examples, not proof of market demand or absence of competing functionality. Interviews remain outstanding; no developers were contacted.
4. **Names are not reserved.** Direct public NuGet flat-container lookups for `apilifecycle`, `apilifecycle.abstractions`, `apilifecycle.aspnetcore`, `apilifecycle.versioning`, and `apilifecycle.testing` returned HTTP 404 on the review date. That only means no public package index was returned; it does not prove an ID is claimable. No push or reservation was attempted.
5. **The dependency promise needs a qualification.** The abstractions have no dependencies; the ASP.NET Core package uses the shared framework; the optional adapter necessarily references Asp.Versioning. Tests have development-only dependencies.
6. **Time/state semantics were underspecified.** This preview treats `SunsetAt` as an inclusive retirement cutoff, and `Sunset` as an explicitly announced pre-retirement state. Header advertisement is immediate, including future dates. See [the lifecycle contract](lifecycle-model.md).
7. **Performance targets need measurement.** Frozen lookups and compiled headers are implemented. Client hashing allocates and is opt-in. CI gates packages at 150 KiB and warmed active middleware allocations at 256 B/request without listeners/client tracking. This measures middleware in isolation; end-to-end zero-allocation claims and production throughput targets require separate benchmarks.

## Implemented preview

- Four packable projects: abstractions, ASP.NET Core middleware, Asp.Versioning adapter, testing helpers.
- Code and configuration registration; immutable policy snapshots; startup validation.
- Standard date headers and successor/deprecation links, preserving existing Link values.
- Retirement 410 and brownout 503 ProblemDetails, Retry-After, injectable TimeProvider.
- BCL request/duration metrics with route templates and optional bounded keyed client hashes.
- Strict endpoint coverage, explicit exemptions, and version-neutral adapter support.
- Minimal API sample, integration/conformance tests, build/test/pack CI, 150 KiB package budget.

## Follow-up work

- Validate positioning with API operators. MIT, Rahul Nagrikar author metadata, the GitHub repository, and a trusted-publishing release workflow were added during prerelease preparation.
- MVC controller sample and integration coverage; expanded adapter coverage for header/media type readers. URL segments, assumed defaults, query-reader selection and neutral endpoints have initial coverage.
- Project registry policies into Asp.Versioning/OpenAPI so descriptions and headers share the registry.
- Usage reporting/export, top-N aggregation, persistent privacy-aware identities if justified by real use cases.
- Recurring brownout schedules, cancellation/error telemetry conventions, broader benchmarks and allocation budgets.
- SourceLink, symbols and package API compatibility checks before stable release.
- OpenAPI contract diff tool/SARIF as v1.1 work; no empty placeholder tool is shipped.
- Verify ID claimability through the first NuGet publish. The owner has authorized publication of this prerelease.

The original document's scope is a roadmap; this preview is an initial implementation slice, not completion of the entire v1/v1.1 plan.

## Initial local verification

- .NET SDK 10.0.303 / runtime 10.0.11 on Windows: Release solution build passed with zero warnings/errors.
- All 32 tests passed, including header conformance, date boundaries, policy/startup validation, configuration binding, adapter URL/default/neutral behavior, client metrics, and the isolated allocation budget.
- Four local `0.0.1-preview.1` NuGet packages were generated, each below 22 KiB (150 KiB budget); manifests and README inclusion were inspected.
- A separate app restored the generated Versioning and Testing packages from the local feed, received the transitive packages, built, and asserted an active registered policy.
- CI is configured for Windows/Linux; Linux CI has not run in this workspace. No packages were published.

## Prerelease preparation

The owner selected MIT and authorized pushing to `Rahul2749/APILifecycle` and publishing `0.0.1-preview.1`. Package metadata and public README links are prepared for that repository. Exception metrics no longer report the default 200 when an exception escapes before headers start; an outer error handler may still choose a different status. The updated local suite passes all 34 tests. The release workflow validates on Windows/Linux, checks package licenses and source commits, and uses NuGet trusted publishing as `rahulnagrikar`. See [release instructions](releasing.md).
