# Publishing previews

The `Publish prerelease` GitHub Actions workflow (`.github/workflows/publish.yml`) is manually dispatched from `main`. It builds and tests on Windows and Linux, verifies package metadata and size, then publishes the Linux artifacts with NuGet trusted publishing. It binds `v0.0.1-preview.1` to the workflow's source commit before pushing packages, so a retry cannot silently publish a different commit under the same version. It creates a GitHub prerelease and attaches the packages after NuGet accepts all four pushes.

NuGet publishing account: `rahulnagrikar`. The NuGet trusted policy must match GitHub owner `Rahul2749`, repository `APILifecycle`, workflow file `publish.yml`, with no environment restriction. Scope it to new packages and versions of `ApiLifecycle.*`; unlist/relist is unnecessary. No permanent API key or repository secret is required.

To release a later preview, update the version in `Directory.Build.props`, release filenames/tag/version checks in `publish.yml` and `scripts/Verify-Packages.ps1`, installation examples, and the changelog together. Run the local Release checks before pushing. Do not reuse an existing release version for changed code; NuGet versions are immutable. Rerunning the same workflow commit can resume a partially completed push with `--skip-duplicate`.

The first tag is `v0.0.1-preview.1`. The four package IDs are `ApiLifecycle.Abstractions`, `ApiLifecycle.AspNetCore`, `ApiLifecycle.Versioning`, and `ApiLifecycle.Testing`. Publishing this family does not reserve the plain `ApiLifecycle` ID or register a prefix reservation.
