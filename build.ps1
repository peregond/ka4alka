param([switch]$Test,[string]$OutputDir='dist/Kachalka')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.tools/dotnet-home'
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.tools/nuget'
$sdk=Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
if($Test){& $sdk run --project tests/Kachalka.Tests.csproj -c Release; if($LASTEXITCODE -ne 0){throw 'Integration tests failed'}}
$publishPath=Join-Path $PSScriptRoot $OutputDir
& $sdk publish native/Kachalka.csproj -c Release -r win-x64 --self-contained true -o $publishPath -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
New-Item -ItemType Directory -Force (Join-Path $publishPath 'licenses') | Out-Null
foreach($package in @('monotorrent','mono.nat','reusabletasks','htmlagilitypack','microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')){
 $packageRoot=Join-Path $env:NUGET_PACKAGES $package
 if(Test-Path -LiteralPath $packageRoot){foreach($version in Get-ChildItem -LiteralPath $packageRoot -Directory){foreach($notice in Get-ChildItem -LiteralPath $version.FullName -File | Where-Object {$_.Name -match '^(LICENSE|ThirdPartyNotices)'}){Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $publishPath 'licenses' ($package+'-'+$notice.Name))}}}
}
Copy-Item -LiteralPath README.md -Destination (Join-Path $publishPath 'README.md')
Write-Host "Готово: $publishPath/Kachalka.exe"
