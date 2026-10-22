# Lifecycle model, preview 0.0.1

Each registry entry is keyed by an ordinal, case-sensitive version string, and snapshots an immutable policy at startup. The Asp.Versioning adapter uses its canonical string representation, such as `1.0`, `2.0-beta`, or a date-based version. No API name/service discriminator is supported yet: a version policy applies to all matching endpoints within the host.

The baseline state defaults to Active. Preview must be explicitly selected. Deprecated and Sunset baselines require a deprecation timestamp; Sunset also requires a sunset timestamp. Explicit baseline states apply immediately. For active/preview baselines, reaching DeprecationAt changes the effective state to Deprecated. Any baseline reaches Retired at SunsetAt. An explicitly Retired baseline takes effect immediately without requiring historical dates. Dates are compared as UTC instants and use the injected TimeProvider. Scheduling activation of Preview into Active is outside this preview.

Evaluation order is: explicit retirement or sunset cutoff; explicit Deprecated/Sunset baseline; reached deprecation timestamp; baseline. SunsetAt must be on/after DeprecationAt when both exist. Reaching a scheduled sunset is a library enforcement policy, not a requirement imposed by RFC 8594. Deprecation by itself never blocks requests.

Brownouts are one-off half-open windows [Start, End). Every window needs a positive duration, starts on/after deprecation, ends no later than sunset when specified, and cannot overlap another window for the same version. Retirement wins over brownouts. The middleware returns 503 during a window and rounds remaining seconds up for Retry-After. At the exact end, normal service resumes. `EnforceRetirement=false` disables 410 rejection only.

SuccessorVersion must name another registered version and cannot form cycles. SuccessorUrl is a separately supplied absolute HTTP(S) URL; URLs cannot be generated from version keys without knowing the application's public routing. PolicyUrl links to human-readable deprecation information. URL header validation rejects relative URLs, unsafe schemes, control characters and Link delimiters.

Headers advertise configured dates even before they are reached. Deprecation uses an RFC 9745 structured date (`@` followed by Unix seconds). Sunset uses RFC 8594 HTTP-date in GMT. Compiled values are produced at startup. ApiLifecycle owns these two headers for tracked responses and appends its Link values. Configuring the same dates separately in Asp.Versioning should be avoided until registry-to-policy projection is implemented.

Requests need a selected endpoint before middleware runs. Unknown/unversioned endpoints pass through in permissive mode; strict mode fails startup for missing coverage, with a request-time guard for endpoints introduced later. Unmatched routes pass through as 404, and selected Asp.Versioning error endpoints retain versioning behavior. Explicit exemptions and version-neutral endpoints are outside lifecycle enforcement. Put authorization before lifecycle middleware so protected endpoints complete authorization first.

Client telemetry is opt-in. Route tags use endpoint templates, version tags come only from the validated registry, and client tags are bounded. At most N keyed hashes are retained per host, plus unknown/other buckets. This is first-seen capping, not a usage ranking. Keys rotate with each service instance, and no raw identifiers or IPs are stored. No administrative HTTP endpoints are exposed.

Policy reload, per-API-name registries, automatic endpoint discovery without a resolver, arbitrary recurrence rules, report storage, and OpenAPI projection are future work.
