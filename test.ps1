$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build/packages'
dotnet run --project "$PSScriptRoot/tests/Murbingo.Tests/Murbingo.Tests.csproj" -c Release -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
