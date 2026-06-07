#!/usr/bin/env pwsh
# Builds the bootstrap + the ASP.NET target, then launches the target with BOTH activation env vars:
#   DOTNET_STARTUP_HOOKS            -> loads the engine (as in Spike #2)
#   ASPNETCORE_HOSTINGSTARTUPASSEMBLIES -> activates the IHostingStartup that captures the IServiceProvider
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

dotnet build "$root\Bootstrap\Spike3.Bootstrap.csproj" -c Debug -v quiet | Out-Null
dotnet build "$root\Target.Web\Target.Web.csproj" -c Debug -v quiet | Out-Null

$bootstrap = Join-Path $root 'Bootstrap\bin\Debug\net10.0\Spike3.Bootstrap.dll'
$target    = Join-Path $root 'Target.Web\bin\Debug\net10.0\Target.Web.dll'
if (-not (Test-Path $bootstrap)) { throw "bootstrap not built: $bootstrap" }
if (-not (Test-Path $target))    { throw "target not built: $target" }

Write-Host "DOTNET_STARTUP_HOOKS              = $bootstrap"
Write-Host "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES = Spike3.Bootstrap`n"
$env:DOTNET_STARTUP_HOOKS = $bootstrap
$env:ASPNETCORE_HOSTINGSTARTUPASSEMBLIES = 'Spike3.Bootstrap'
try { dotnet $target }
finally {
    Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue
    Remove-Item Env:\ASPNETCORE_HOSTINGSTARTUPASSEMBLIES -ErrorAction SilentlyContinue
}
