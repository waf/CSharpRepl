#!/usr/bin/env pwsh
# Builds the bootstrap + the Generic Host console target, then launches it with DOTNET_STARTUP_HOOKS.
# Runs TWICE: once with Host.CreateDefaultBuilder (legacy HostBuilder) and once with
# Host.CreateApplicationBuilder, to see which builders fire the capturable "HostBuilt" diagnostic.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

dotnet build "$root\Bootstrap\Spike4.Bootstrap.csproj" -c Debug -v quiet | Out-Null
dotnet build "$root\Target.Worker\Target.Worker.csproj" -c Debug -v quiet | Out-Null

$bootstrap = Join-Path $root 'Bootstrap\bin\Debug\net10.0\Spike4.Bootstrap.dll'
$target    = Join-Path $root 'Target.Worker\bin\Debug\net10.0\Target.Worker.dll'
if (-not (Test-Path $bootstrap)) { throw "bootstrap not built: $bootstrap" }
if (-not (Test-Path $target))    { throw "target not built: $target" }

$env:DOTNET_STARTUP_HOOKS = $bootstrap
try
{
    Write-Host "============================================================"
    Write-Host " RUN 1: Host.CreateDefaultBuilder (legacy HostBuilder)"
    Write-Host "============================================================"
    dotnet $target

    Write-Host "`n============================================================"
    Write-Host " RUN 2: Host.CreateApplicationBuilder (modern)"
    Write-Host "============================================================"
    dotnet $target --app-builder
}
finally { Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue }
