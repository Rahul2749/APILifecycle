param([string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $root 'artifacts/packages' }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$commit = git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source commit unavailable' }
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
$expected = @('ApiLifecycle.Abstractions', 'ApiLifecycle.AspNetCore', 'ApiLifecycle.Versioning', 'ApiLifecycle.Testing')
if ($packages.Count -ne $expected.Count) { throw 'Expected exactly four packages' }
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($package in $packages) {
    if ($package.Length -gt 150KB) { throw "Package exceeds 150 KiB: $($package.Name)" }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $manifest = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($manifest.Count -ne 1) { throw "Invalid manifest count: $($package.Name)" }
        $reader = [System.IO.StreamReader]::new($manifest[0].Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $xml.package.metadata
        if ($metadata.id -notin $expected -or -not $seen.Add([string]$metadata.id)) { throw "Unexpected/duplicate package: $($metadata.id)" }
        if ($metadata.version -ne $version) { throw "Unexpected version: $($metadata.version), expected $version" }
        if ($metadata.license.type -ne 'expression' -or $metadata.license.InnerText -ne 'MIT') { throw 'Missing MIT license metadata' }
        if ($metadata.authors -ne 'Rahul Nagrikar') { throw 'Unexpected author' }
        if ($metadata.repository.url -ne 'https://github.com/Rahul2749/APILifecycle') { throw 'Missing repository URL' }
        if ($metadata.repository.commit -ne $commit) { throw "Package does not match source commit $commit" }
        if (-not ($archive.Entries | Where-Object FullName -EQ 'README.md')) { throw 'Missing README' }
        if (-not ($archive.Entries | Where-Object FullName -EQ "lib/net10.0/$($metadata.id).dll")) { throw 'Missing .NET 10 assembly' }
        foreach ($dependency in @($metadata.dependencies.group.dependency)) {
            if ($version -notmatch '-' -and $dependency.version -match '-') { throw "Stable package depends on prerelease: $($dependency.id)" }
        }
        & (Join-Path $PSScriptRoot 'Verify-Symbols.ps1') -Path (Join-Path $PackageDirectory "$($metadata.id).$version.snupkg")
        Write-Output "$($metadata.id) $($version): $($package.Length) bytes; MIT; source $commit; portable symbols and SourceLink verified"
    } finally { $archive.Dispose() }
}
