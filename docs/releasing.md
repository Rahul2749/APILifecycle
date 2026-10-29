# Publishing releases

The `Release` GitHub Actions workflow (`.github/workflows/publish.yml`) reads its version from `Directory.Build.props`. Manual dispatch defaults to `publish=false`: it validates on Windows/Linux and produces artifacts without creating a tag, publishing packages, or requesting a NuGet credential. You can run validation on a preparation branch. Publication requires explicitly selecting `publish=true` and running from `main`.

Validation runs the Release build and regression suite, audits transitive dependencies during restore, checks all four package manifests, stable dependencies, source commits, portable PDBs and SourceLink, and runs a real Kestrel package consumer with no project references. ApiLifecycle.* package source mapping and a fresh cache ensure this consumer uses the candidate artifacts rather than a stale local or public version.

After validation succeeds on both operating systems, publishing binds `v<version>` to the exact source commit, obtains a short-lived NuGet credential, and pushes packages and their symbols. A normal GitHub release is created for stable versions; versions with a suffix create a prerelease. Both package and symbols files are attached. A tag belonging to another commit blocks publishing; it is never moved automatically.

NuGet publishing account: `rahulnagrikar`. The NuGet trusted policy must match GitHub owner `Rahul2749`, repository `APILifecycle`, workflow file `publish.yml`, with no environment restriction. Scope it to new packages and versions of `ApiLifecycle.*`; unlist/relist is unnecessary. No permanent API key or repository secret is required.

To prepare another release, update `Version` and release notes in `Directory.Build.props`, installation examples, and `CHANGELOG.md`. Keep `PackageValidationBaselineVersion` in `Directory.Build.targets` at the last supported release (currently the published preview); after 1.0.0 is published, use 1.0.0 to guard the 1.x API. Version strings and filenames in the workflows/scripts are derived automatically. Use a clean package output directory and run:

```powershell
dotnet restore ApiLifecycle.slnx -p:NuGetAuditMode=all
dotnet build ApiLifecycle.slnx -c Release --no-restore
dotnet test --solution ApiLifecycle.slnx -c Release --no-build
dotnet pack ApiLifecycle.slnx -c Release --no-build -o artifacts/packages
./scripts/Verify-Packages.ps1
./scripts/Test-Packages.ps1
```

The final publish is a separate, deliberate action after reviewing the validation-only run. Do not reuse an existing release version for changed code; NuGet versions are immutable. Rerunning the same workflow commit can resume a partial push with `--skip-duplicate`. Published symbols and package signatures may take time to become available on NuGet; verify installation from a clean consumer after publication.

The first tag is `v0.0.1-preview.1`. The four package IDs are `ApiLifecycle.Abstractions`, `ApiLifecycle.AspNetCore`, `ApiLifecycle.Versioning`, and `ApiLifecycle.Testing`. Publishing this family does not reserve the plain `ApiLifecycle` ID or register a prefix reservation.
