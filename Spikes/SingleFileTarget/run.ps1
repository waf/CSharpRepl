#!/usr/bin/env pwsh
# Builds the external bootstrap, publishes the target as a SINGLE FILE two ways (framework-dependent and
# self-contained), and runs each with DOTNET_STARTUP_HOOKS to observe how the inspector degrades when the
# target's assemblies are bundled (empty Assembly.Location).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rid  = 'win-x64'

dotnet build "$root\Bootstrap\Spike5.Bootstrap.csproj" -c Debug -v quiet | Out-Null
$bootstrap = Join-Path $root 'Bootstrap\bin\Debug\net10.0\Spike5.Bootstrap.dll'
if (-not (Test-Path $bootstrap)) { throw "bootstrap not built: $bootstrap" }

function Publish-And-Run([string]$label, [string]$selfContained)
{
    $pub = Join-Path $root "Target.Single\publish\$label"
    Write-Host "`n============================================================"
    Write-Host " $label single-file (PublishSingleFile=true, SelfContained=$selfContained)"
    Write-Host "============================================================"
    dotnet publish "$root\Target.Single\Target.Single.csproj" -c Release -r $rid `
        --self-contained $selfContained -p:PublishSingleFile=true -o $pub -v quiet | Out-Null

    $exe = Join-Path $pub 'Target.Single.exe'
    if (-not (Test-Path $exe)) { throw "single-file exe not produced: $exe" }

    $env:DOTNET_STARTUP_HOOKS = $bootstrap
    try { & $exe }
    finally { Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue }
}

Publish-And-Run 'framework-dependent' 'false'
Publish-And-Run 'self-contained'      'true'
