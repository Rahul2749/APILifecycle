$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot
$packages = @(Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/packages') -Filter '*.nupkg')
$expected = @('ApiLifecycle.Abstractions', 'ApiLifecycle.AspNetCore', 'ApiLifecycle.Versioning', 'ApiLifecycle.Testing')
if ($packages.Count -ne $expected.Count) { throw 'Expected exactly four packages' }
foreach ($package in $packages) {
    if ($package.Length -gt 150KB) { throw "Package exceeds 150 KiB: $($package.Name)" }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $manifest = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($manifest.Count -ne 1) { throw "Invalid manifest count: $($package.Name)" }
        $reader = [System.IO.StreamReader]::new($manifest[0].Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $xml.package.metadata
        if ($metadata.id -notin $expected) { throw "Unexpected package: $($metadata.id)" }
        if ($metadata.version -ne '0.0.1-preview.1') { throw "Unexpected version: $($metadata.version)" }
        if ($metadata.license.type -ne 'expression' -or $metadata.license.InnerText -ne 'MIT') { throw 'Missing MIT license metadata' }
        if ($metadata.authors -ne 'Rahul Nagrikar') { throw 'Unexpected author' }
        if ($metadata.repository.url -ne 'https://github.com/Rahul2749/APILifecycle') { throw 'Missing repository URL' }
        if ($metadata.repository.commit -notmatch '^[a-f0-9]{40}$') { throw 'Missing source commit' }
        if (-not ($archive.Entries | Where-Object FullName -EQ 'README.md')) { throw 'Missing README' }
        Write-Output "$($metadata.id) $($metadata.version): $($package.Length) bytes, MIT, commit $($metadata.repository.commit)"
    } finally { $archive.Dispose() }
}
