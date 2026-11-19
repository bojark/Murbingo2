param([switch]$Publish, [switch]$Standalone)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build/packages'
$taskProject = [xml](Get-Content -LiteralPath "$PSScriptRoot/src/Murbingo.App/Murbingo.App.csproj" -Raw)
$taskVersion = $taskProject.Project.PropertyGroup.Version
$taskBuildName = "Murbingo2-v$taskVersion-win-x64"
if ($Publish) {
    if ($Standalone) {
        dotnet publish "$PSScriptRoot/src/Murbingo.App/Murbingo.App.csproj" -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -o "$PSScriptRoot/artifacts/$taskBuildName-standalone"
    } else {
        dotnet publish "$PSScriptRoot/src/Murbingo.App/Murbingo.App.csproj" -c Release -r win-x64 --self-contained false -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false -o "$PSScriptRoot/artifacts/$taskBuildName"
    }
} else {
    dotnet build "$PSScriptRoot/src/Murbingo.App/Murbingo.App.csproj" -c Release -p:NuGetAudit=false
}
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
