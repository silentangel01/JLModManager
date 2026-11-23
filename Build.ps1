param([string]$Dotnet='dotnet',[switch]$Test)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $PSScriptRoot
try {
  if($Test) {
    & $Dotnet restore 'Tests/Tests.csproj' --configfile 'NuGet.Config'
    if($LASTEXITCODE -ne 0){throw 'Test restore failed'}
    & $Dotnet run --project 'Tests/Tests.csproj' -c Release --no-restore -- 'test-results'
    if($LASTEXITCODE -ne 0){throw 'Tests failed'}
  }
  & $Dotnet publish 'App/App.csproj' -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:RestoreConfigFile=$PSScriptRoot\NuGet.Config" -o 'publish'
  if($LASTEXITCODE -ne 0){throw 'Publish failed'}
  Copy-Item -LiteralPath 'README.md' -Destination 'publish'
  Write-Output 'Published to ./publish. Preserve Data when updating an existing installation.'
} finally { Pop-Location }
