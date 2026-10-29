param([string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $root 'artifacts/packages' }
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$runDirectory = Join-Path $root ('artifacts/package-smoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$cache = Join-Path $runDirectory 'packages'
$config = Join-Path $runDirectory 'NuGet.Config'
$escapedFeed = [Security.SecurityElement]::Escape($PackageDirectory)
@"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="candidate"><package pattern="ApiLifecycle.*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config
$project = Join-Path $root 'tests/ApiLifecycle.PackageSmoke/ApiLifecycle.PackageSmoke.csproj'
dotnet restore $project --configfile $config --packages $cache --no-http-cache --disable-parallel "-p:LifecyclePackageVersion=$version" -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed' }
dotnet run --project $project -c Release --no-restore "-p:LifecyclePackageVersion=$version" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Real HTTP package consumer tests failed' }
Write-Output "Fresh consumer used candidate packages from $PackageDirectory with isolated cache $cache"
