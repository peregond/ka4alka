param([string]$FromVersion='0.21.0',[string]$ReleaseDirectory='dist/release-0.21.1')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$release=[System.IO.Path]::GetFullPath($ReleaseDirectory)
$manifest=Get-Content (Join-Path $release 'latest.json') -Raw | ConvertFrom-Json
$state=Join-Path $PWD 'test-output/published-upgrade'
New-Item -ItemType Directory -Force $state | Out-Null
$env:KACHALKA_DATA=Join-Path $state 'data';New-Item -ItemType Directory -Force $env:KACHALKA_DATA | Out-Null
$folder=Join-Path $env:KACHALKA_DATA 'downloads';New-Item -ItemType Directory -Force $folder | Out-Null
@{Folder=$folder;FolderConfigured=$true;AutoResumeDownloads=$false;CheckForUpdates=$false;AutoUpdate=$false} | ConvertTo-Json | Set-Content (Join-Path $env:KACHALKA_DATA 'settings.json') -Encoding utf8NoBOM
$setup=Join-Path $state 'previous-setup.exe'
Invoke-WebRequest ('https://github.com/peregond/ka4alka/releases/download/v'+$FromVersion+'/Kachalka-Setup-'+$FromVersion+'.exe') -OutFile $setup -TimeoutSec 120
$sumsPath=Join-Path $state 'previous-checksums.txt'
Invoke-WebRequest ('https://github.com/peregond/ka4alka/releases/download/v'+$FromVersion+'/SHA256SUMS.txt') -OutFile $sumsPath -TimeoutSec 30
$sums=[System.IO.File]::ReadAllText($sumsPath)
$expected=([regex]::Match($sums,'(?m)^([a-f0-9]{64})\s+Kachalka-Setup-'+[regex]::Escape($FromVersion)+'\.exe\s*$')).Groups[1].Value
$actual=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if($expected.Length -ne 64 -or $actual -ne $expected){throw ('Published previous installer checksum mismatch; expected='+$expected+' actual='+$actual)}
$installer=Start-Process $setup -ArgumentList '/S' -PassThru
if(-not $installer.WaitForExit(60000) -or $installer.ExitCode -ne 0){throw 'Previous published installer failed'}
$install=Join-Path $env:LOCALAPPDATA 'Programs/Kachalka'
$old=$null;$helper=$null
try{
 $old=Start-Process (Join-Path $install 'Kachalka.exe') -PassThru
 $limit=(Get-Date).AddSeconds(30)
 while(-not $old.HasExited -and $old.MainWindowHandle -eq 0 -and (Get-Date) -lt $limit){Start-Sleep -Milliseconds 200;$old.Refresh()}
 if($old.HasExited -or $old.MainWindowHandle -eq 0){throw 'Previous published WPF app did not start'}
 $id=[Guid]::NewGuid().ToString('N');$work=Join-Path $env:KACHALKA_DATA ('updates/'+$id);New-Item -ItemType Directory -Force $work | Out-Null
 Copy-Item (Join-Path $release 'Kachalka-win-x64.zip') (Join-Path $work 'package.zip')
 Copy-Item (Join-Path $release 'latest.json') $work;Copy-Item (Join-Path $release 'latest.sig') $work
 # Use the real previously shipped updater and its embedded production trust key.
 Copy-Item (Join-Path $install 'Kachalka.Updater.exe') (Join-Path $work 'updater.exe')
 $job=Join-Path $work 'job.json'
 @{InstallDirectory=$install;ArchivePath=(Join-Path $work 'package.zip');ManifestPath=(Join-Path $work 'latest.json');SignaturePath=(Join-Path $work 'latest.sig');ParentPid=$old.Id;ParentStartTicks=$old.StartTime.ToUniversalTime().Ticks;Id=$id} | ConvertTo-Json | Set-Content $job -Encoding utf8NoBOM
 $helper=Start-Process (Join-Path $work 'updater.exe') -ArgumentList @('--job',('"'+$job+'"')) -PassThru
 if(-not $old.CloseMainWindow() -or -not $old.WaitForExit(30000)){throw 'Previous application did not close'}
 if(-not $helper.WaitForExit(90000) -or $helper.ExitCode -ne 0){throw 'Published upgrade failed; inspect result.txt'}
 if((Get-Content (Join-Path $work 'healthy') -Raw) -ne $manifest.version){throw 'Production update health check failed'}
 if((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Kachalka').DisplayVersion -ne $manifest.version){throw 'Production registered version incorrect'}
 if((Get-Content (Join-Path $env:KACHALKA_DATA 'settings.json') -Raw | ConvertFrom-Json).Folder -ne $folder){throw 'Production upgrade lost configured folder'}
 ('PASS: previously published '+$FromVersion+' accepts signed '+$manifest.version+', starts WPF and preserves settings') | Set-Content (Join-Path $state 'checks.txt')
 Get-Content (Join-Path $state 'checks.txt')
}finally{
 foreach($process in @($old,$helper)){if($null -ne $process){if(-not $process.HasExited){$process.Kill($true);$process.WaitForExit()};$process.Dispose()}}
 Get-Process Kachalka -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $install 'Kachalka.exe')} | Stop-Process
 Remove-Item $setup -ErrorAction SilentlyContinue
}
