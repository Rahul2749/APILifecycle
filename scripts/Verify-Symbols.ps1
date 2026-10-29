param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path)) { throw "Missing symbols: $Path" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
try {
    $pdbs = @($archive.Entries | Where-Object FullName -Like '*.pdb')
    if ($pdbs.Count -ne 1) { throw 'Expected one portable PDB per symbols package' }
    $stream = $pdbs[0].Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        $bytes = $memory.ToArray()
        if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'BSJB') { throw 'Symbols must be portable PDBs' }
        if (-not [Text.Encoding]::UTF8.GetString($bytes).Contains('https://raw.githubusercontent.com/Rahul2749/APILifecycle/', [StringComparison]::OrdinalIgnoreCase)) {
            throw "SourceLink mapping missing: $Path"
        }
    } finally { $stream.Dispose(); $memory.Dispose() }
} finally { $archive.Dispose() }
