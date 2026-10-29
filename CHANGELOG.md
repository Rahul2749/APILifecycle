# Changelog

## 1.0.0 — release candidate prepared 2026-10-30

First stable release scope: lifecycle registry, standard headers, retirement/brownout enforcement, bounded BCL metrics, Asp.Versioning integration, and testing helpers on .NET 10. Publication is a separate release action.

- Preserve ASP.NET Core 405 and Asp.Versioning 400 responses in strict mode, including invalid, unspecified, unsupported, and ambiguous versions.
- Match parsed API versions rather than their string formatting, so `1` and `1.0` resolve consistently. Invalid keys and semantic duplicates fail startup in permissive mode too.
- Validate null policy collections/entries and null brownout collections/entries with actionable options errors.
- Add an MVC sample and regression coverage for MVC implicit/explicit actions, query/header/media-type readers, authentication order, date/status versions, and concurrent client caps.
- Derive the meter version from the package assembly.
- Ship portable symbols and SourceLink; validate source commit, stable dependencies, and package identity in CI.
- Test actual NuGet artifacts with a fresh consumer over Kestrel, including HEAD requests, framework errors, and concurrent requests.
- Read release versions from project metadata and default the release workflow to validation only. Stable versions create normal GitHub releases; suffixed versions create prereleases.

Deferred: reports/export, recurring brownouts, OpenAPI policy projection, contract-diff tooling, per-API-name policies, and older target frameworks.

## 0.0.1-preview.1 — 2026-10-22

Initial experimental release for .NET 10. Public APIs may change before 1.0.

- Immutable lifecycle policies and startup validation, including code/configuration registration.
- RFC 9745 Deprecation, RFC 8594 Sunset, successor and deprecation-policy links.
- HTTP 410 retirement enforcement and one-off HTTP 503 brownout windows with ProblemDetails.
- Request counters/duration metrics, route-template tags, and optional bounded client hashes.
- Asp.Versioning.Http 10.2.3 adapter with URL, assumed-default, and neutral-endpoint coverage.
- Framework-independent testing helpers and a runnable minimal API sample.
- Correct exception metrics: omit unknown status before headers start instead of reporting 200.

Known limitations: .NET 10 only; policies reload only on restart; client buckets are first-seen rather than top-N and rotate with each host; no reports, recurring brownouts, OpenAPI policy projection, contract-diff tool, or MVC integration tests.
