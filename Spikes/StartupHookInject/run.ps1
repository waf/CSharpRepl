#!/usr/bin/env pwsh
# Builds the bootstrap (which pulls in Contracts + Engine + Roslyn) and the target, then launches the
# target with DOTNET_STARTUP_HOOKS pointing at the bootstrap DLL — proving cross-process injection.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

dotnet build "$root\Bootstrap\Spike2.Bootstrap.csproj" -c Debug -v quiet | Out-Null
dotnet build "$root\Target\Target.csproj" -c Debug -v quiet | Out-Null

$bootstrap = Join-Path $root 'Bootstrap\bin\Debug\net10.0\Spike2.Bootstrap.dll'
$target    = Join-Path $root 'Target\bin\Debug\net10.0\Target.dll'

if (-not (Test-Path $bootstrap)) { throw "bootstrap not built: $bootstrap" }
if (-not (Test-Path $target))    { throw "target not built: $target" }

Write-Host "DOTNET_STARTUP_HOOKS = $bootstrap`n"
$env:DOTNET_STARTUP_HOOKS = $bootstrap
try { dotnet $target }
finally { Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue }
