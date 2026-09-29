[CmdletBinding()]
param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'src\P1Z.Subtitler\P1Z.Subtitler.csproj'
$tests = Join-Path $repository 'tests\P1Z.Subtitler.Tests\P1Z.Subtitler.Tests.csproj'
$output = Join-Path $repository 'dist'

dotnet run --project $tests -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination (Join-Path $output 'README.md') -Force
Write-Host "Published p1z.subtitler to $output"
