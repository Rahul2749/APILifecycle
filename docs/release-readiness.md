# 1.0.0 release preparation

Prepared 30 October 2026. Local and hosted release validation passed, and [preparation PR #1](https://github.com/Rahul2749/APILifecycle/pull/1) was merged after publication was authorized. The compatibility baseline used for these checks is `0.0.1-preview.1`. Local results are recorded below; hosted results are available in [GitHub Actions](https://github.com/Rahul2749/APILifecycle/actions). See the [1.0.0 release](https://github.com/Rahul2749/APILifecycle/releases/tag/v1.0.0) for published artifacts.

## Supported release scope

.NET 10; four packages (Abstractions, AspNetCore, Versioning, Testing); code/configuration policies, deprecation/sunset/successor headers, retirement/brownout ProblemDetails, BCL usage metrics, bounded opt-in client IDs, startup validation, and testing assertions. Minimal APIs and MVC controllers are covered, including URL, query, header and media-type readers, assumed defaults, neutral endpoints, and date/status version semantics. Policies apply host-wide per version key.

## Findings fixed before stable

- Strict mode previously intercepted method and versioning failures and could turn 400/405 into server errors. Synthetic errors now bypass lifecycle resolution and retain their framework responses.
- String formatting could prevent equivalent version keys from matching, or let invalid/ambiguous keys bypass enforcement in permissive mode. The adapter now matches parsed versions and validates keys at startup.
- Null policy/brownout configuration raised NullReferenceException. It now raises actionable OptionsValidationException messages.
- Meter version was hard-coded to 0.0.1. It now follows the built assembly version.
- Release scripts were bound to preview names. Versions are now derived from metadata, symbols are shipped, and release dispatch defaults to validation only.

## Verification record

- Clean published-preview consumer: restored from nuget.org into a fresh cache; real Kestrel passed 12 HTTP checks and 100 concurrent requests using NuGet references only, plus registry and endpoint assertions.
- Expanded source suite: 57 passing tests. New checks include invalid/unsupported/ambiguous/missing version requests; 405 handling; MVC implicit/explicit actions; query/header/media-type readers; authentication before retirement; date/status versions and numeric aliases; null configuration; client caps under concurrency.
- Release solution build: zero warnings/errors, including both minimal API and MVC samples.
- Candidate package consumer: 17 passing HTTP checks plus 100 concurrent requests, startup/registry assertions, mapped HEAD responses, and framework errors. The script uses a new cache and forces ApiLifecycle.* to the generated feed.
- Direct/transitive vulnerability report: 7 solution projects audited; zero vulnerable package findings at the check time. This report is not a guarantee about future advisories.
- Four .nupkg files plus four portable-symbol .snupkg files; MIT metadata, repository identity/commit, SourceLink and the 150 KiB package budget are checked. The final commit will be repacked before release.
- SDK API compatibility against the published preview passed for all four packages with no suppressions. The same check is enforced in Directory.Build.targets. Hosted Windows/Linux checks repeat the build, 57 regressions, package verification and real HTTP consumer as the final release gates.

## Deliberately deferred

Usage-report storage/export and top-N ranking, recurring brownouts, per-API-name registries, OpenAPI policy projection, contract-diff tooling, analyzers, configuration hot reload, AOT/trimming guarantees, older target frameworks, and production-scale throughput benchmarks. Client hashes rotate per host; users needing stable client reporting must account for this. Authentication/authorization is configured by the application and should precede lifecycle middleware.

## Release action

The preparation run used `publish=false` and created no stable tag or package. The authorized release runs `Release` from main with `publish=true`, repeating all validation before publishing. See [releasing.md](releasing.md) for the complete process.
