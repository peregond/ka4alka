param(
 [string]$Version='0.37.0',
 [string]$SigningKeyPath=$env:KACHALKA_SIGNING_KEY,
 [string]$MakeNsis='makensis'
)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version'}
if(-not $SigningKeyPath){throw 'Supply -SigningKeyPath with the private release-signing key'}
$shortVersion=([Version]$Version).ToString(2)
$output='dist/Kachalka-'+$shortVersion
./build.ps1 -Test -OutputDir $output
$sdk=Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
& $sdk run --project update-tests/Kachalka.UpdateTests.csproj -c Release
if($LASTEXITCODE -ne 0){throw 'Updater checks failed'}
$archive='dist/Kachalka-'+$shortVersion+'.zip'
Compress-Archive -LiteralPath $output -DestinationPath $archive -CompressionLevel Optimal -Force
./package-release.ps1 -Version $Version -ArchivePath $archive -SigningKeyPath $SigningKeyPath
& $sdk run --project update-tests/Kachalka.UpdateTests.csproj -c Release -- --verify-release ('dist/release-'+$Version)
if($LASTEXITCODE -ne 0){throw 'Release verification failed'}
$release=Join-Path $PSScriptRoot ('dist/release-'+$Version)
$uninstallScript=Join-Path $PSScriptRoot '.tools/uninstall-files.nsh'
./installer/write-uninstall.ps1 -ApplicationDirectory $output -OutputPath $uninstallScript
$appDirectory=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $output))
& $MakeNsis '/INPUTCHARSET' 'UTF8' ('/DVERSION='+$Version) ('/DAPP_DIR='+$appDirectory) ('/DAPP_GLOB='+$appDirectory+'\*') ('/DUNINSTALL_SCRIPT='+$uninstallScript) ('/DOUTPUT='+(Join-Path $release ('Kachalka-Setup-'+$Version+'.exe'))) (Join-Path $PSScriptRoot 'installer/Kachalka.nsi')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $release ('Kachalka-Setup-'+$Version+'.exe')
((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[System.IO.Path]::GetFileName($setup)) | Add-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host ('Release ready: '+$release)
