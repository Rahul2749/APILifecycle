# Changelog

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
