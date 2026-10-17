param(
 [Parameter(Mandatory)][string]$Archive,
 [string]$Version='0.43.1',
 [string]$Output='test-output/installer-compression'
)
$ErrorActionPreference='Stop'
# This installs and uninstalls real packages; only run on disposable Windows CI machines.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows'){throw 'Requires a disposable Windows GitHub runner'}
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid fixture version'}
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$outputPath=[IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Force $outputPath | Out-Null
$install=Join-Path $env:LOCALAPPDATA 'Programs/Kachalka'
$state=Join-Path $env:LOCALAPPDATA 'Kachalka'
if((Test-Path $install) -or (Test-Path $state)){throw 'Runner already has Kachalka data'}
$payload=Join-Path $outputPath 'payload'
Expand-Archive -LiteralPath $Archive -DestinationPath $payload
$app=@(Get-ChildItem $payload -Directory)
if($app.Count -ne 1 -or -not(Test-Path (Join-Path $app[0].FullName 'Kachalka.exe'))){throw 'Unexpected archive layout'}
$appPath=$app[0].FullName
$files=@(Get-ChildItem $appPath -File -Recurse | ForEach-Object {
 [pscustomobject]@{Path=[IO.Path]::GetRelativePath($appPath,$_.FullName);Hash=(Get-FileHash $_.FullName -Algorithm SHA256).Hash;Bytes=$_.Length}
})
$uninstall=Join-Path $outputPath 'uninstall.nsh'
./installer/write-uninstall.ps1 -ApplicationDirectory $appPath -OutputPath $uninstall
$source=Join-Path $root 'installer/Kachalka.nsi'
$baseline=Join-Path $root 'installer/compression-baseline.nsi'
$text=Get-Content $source -Raw
if($text -notmatch 'SetCompressor /SOLID lzma' -or $text -notmatch 'SetCompressorDictSize 8'){throw 'Expected memory-bounded LZMA settings'}
$text.Replace('SetCompressor /SOLID lzma','SetCompressor /SOLID zlib').Replace('SetCompressorDictSize 8','') | Set-Content $baseline -Encoding utf8NoBOM
$compiler='C:/Program Files (x86)/NSIS/makensis.exe'
$results=@()
try {
 foreach($kind in @('zlib','lzma')){
  $script=if($kind -eq 'zlib'){$baseline}else{$source}
  $setup=Join-Path $outputPath ($kind+'.exe')
  & $compiler /INPUTCHARSET UTF8 ('/DVERSION='+$Version) ('/DAPP_DIR='+$appPath) ('/DAPP_GLOB='+$appPath+'\*') ('/DUNINSTALL_SCRIPT='+$uninstall) ('/DOUTPUT='+$setup) $script
  if($LASTEXITCODE -ne 0){throw "Compiler failed: $kind"}
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $process=Start-Process $setup -ArgumentList '/S' -PassThru
  $peak=0L
  while(-not $process.HasExited){
   $process.Refresh();$peak=[Math]::Max($peak,$process.PeakWorkingSet64)
   if($watch.Elapsed.TotalSeconds -gt 120){$process.Kill();throw "Installation timed out: $kind"}
   Start-Sleep -Milliseconds 50
  }
  $watch.Stop()
  if($process.ExitCode -ne 0){throw "Installation failed: $kind"}
  foreach($file in $files){
   $target=Join-Path $install $file.Path
   if(-not(Test-Path $target) -or (Get-FileHash $target -Algorithm SHA256).Hash -ne $file.Hash){throw "Payload differs: $($file.Path)"}
  }
  if(@(Get-ChildItem $install -File -Recurse).Count -ne $files.Count){throw 'Unexpected installed files'}
  $marker=Join-Path $state 'compression-preservation.txt'
  Set-Content $marker 'Keep settings and queue' -Encoding utf8NoBOM
  $remove=Start-Process (Join-Path $state 'installation/Uninstall.exe') -ArgumentList '/S' -PassThru -Wait
  if($remove.ExitCode -ne 0){throw 'Uninstaller failed'}
  $deadline=(Get-Date).AddSeconds(30)
  while((Test-Path $install) -and (Get-Date) -lt $deadline){Start-Sleep -Milliseconds 100}
  if(Test-Path $install){throw 'Installed payload was not removed'}
  if((Get-Content $marker -Raw).Trim() -ne 'Keep settings and queue'){throw 'Uninstall removed user data'}
  $results+=[pscustomobject]@{Compressor=$kind;InstallerBytes=(Get-Item $setup).Length;InstallSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);PeakWorkingSetBytes=$peak;VerifiedFiles=$files.Count;PayloadSha256='all matched';UserDataPreserved=$true}
 }
 if($results[1].InstallerBytes -ge $results[0].InstallerBytes){throw 'LZMA did not reduce installer size'}
 $report=[ordered]@{FixtureVersion=$Version;UnpackedBytes=($files | Measure-Object Bytes -Sum).Sum;Results=$results;SavedBytes=$results[0].InstallerBytes-$results[1].InstallerBytes;SavedPercent=[Math]::Round(100*(1-$results[1].InstallerBytes/$results[0].InstallerBytes),2)}
 $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outputPath 'checks.json') -Encoding utf8NoBOM
 $report | ConvertTo-Json -Depth 5 | Write-Output
} finally {
 Remove-Item $baseline -ErrorAction SilentlyContinue
}
