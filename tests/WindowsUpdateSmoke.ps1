$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$root=$PWD.Path
$evidence=Join-Path $root 'test-output/windows-update'
New-Item -ItemType Directory -Force $evidence | Out-Null
$state=Join-Path $evidence 'data'
New-Item -ItemType Directory -Force $state | Out-Null
$env:KACHALKA_DATA=$state
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$keyPath=Join-Path $evidence 'test-private.pem'
$publicKeyPath=Join-Path $root 'update-core/update-public.pem'
$originalPublicKey=[System.IO.File]::ReadAllText($publicKeyPath)
$rsa=[System.Security.Cryptography.RSA]::Create(3072)
try{
 [System.IO.File]::WriteAllText($keyPath,$rsa.ExportPkcs8PrivateKeyPem())
 # CI-only key. Never publish these fixture binaries or private test keys.
 [System.IO.File]::WriteAllText($publicKeyPath,$rsa.ExportSubjectPublicKeyInfoPem())
}finally{$rsa.Dispose()}
$folder=Join-Path $state 'downloads';New-Item -ItemType Directory -Force $folder | Out-Null
@{Folder=$folder;FolderConfigured=$true;AutoResumeDownloads=$false;CheckForUpdates=$false;AutoUpdate=$false} | ConvertTo-Json | Set-Content (Join-Path $state 'settings.json') -Encoding utf8NoBOM
$started=@()
try{
 dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release
 if($LASTEXITCODE -ne 0){throw 'Updater tests failed'}
 ./build.ps1 -Test -OutputDir 'dist/Kachalka-0.19'
 $uninstall=Join-Path $root '.tools/uninstall-files.nsh'
 ./installer/write-uninstall.ps1 -ApplicationDirectory 'dist/Kachalka-0.19' -OutputPath $uninstall
 $setup=Join-Path $evidence 'Kachalka-Setup-0.19.0.exe'
 $appDirectory=[System.IO.Path]::GetFullPath((Join-Path $root 'dist/Kachalka-0.19'))
 & 'C:/Program Files (x86)/NSIS/makensis.exe' '/V2' '/INPUTCHARSET' 'UTF8' '/DVERSION=0.19.0' ('/DAPP_DIR='+$appDirectory) ('/DAPP_GLOB='+$appDirectory+'\*') ('/DUNINSTALL_SCRIPT='+$uninstall) ('/DOUTPUT='+$setup) installer/Kachalka.nsi
 if($LASTEXITCODE -ne 0){throw 'Installer failed'}
 $installer=Start-Process -FilePath $setup -ArgumentList '/S' -PassThru
 if(-not $installer.WaitForExit(60000) -or $installer.ExitCode -ne 0){throw 'Silent installation failed'}
 $install=Join-Path $env:LOCALAPPDATA 'Programs/Kachalka'
 if(-not(Test-Path (Join-Path $install 'Kachalka.exe'))){throw 'Installation missing executable'}
 $desktop=[Environment]::GetFolderPath('DesktopDirectory')
 if(-not(Test-Path (Join-Path $desktop 'Качалка.lnk'))){
   $observed=@(Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {$_.Name}) -join ', '
   throw ('Desktop shortcut missing in '+$desktop+'; observed: '+$observed)
 }
 if((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Kachalka').DisplayVersion -ne '0.19.0'){throw 'Wrong installed version'}
 'PASS: EXE installation, shortcut, registered version 0.19.0' | Add-Content (Join-Path $evidence 'checks.txt')

 dotnet publish native/Kachalka.csproj -c Release -r win-x64 --self-contained true -p:Version=0.19.1 -p:DebugType=None -p:DebugSymbols=false -o dist/Kachalka-0.19.1
 if($LASTEXITCODE -ne 0){throw 'Next-version fixture build failed'}
 Copy-Item '.tools/updater-publish/Kachalka.Updater.exe' 'dist/Kachalka-0.19.1/'
 Compress-Archive -LiteralPath dist/Kachalka-0.19.1 -DestinationPath dist/Kachalka-0.19.1.zip -Force
 ./package-release.ps1 -Version 0.19.1 -ArchivePath dist/Kachalka-0.19.1.zip -SigningKeyPath $keyPath
 dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release -- --verify-release dist/release-0.19.1
 if($LASTEXITCODE -ne 0){throw 'Signed fixture verification failed'}
 $old=Start-Process (Join-Path $install 'Kachalka.exe') -PassThru;$started+= $old
 $limit=(Get-Date).AddSeconds(30)
 while(-not $old.HasExited -and $old.MainWindowHandle -eq 0 -and (Get-Date) -lt $limit){Start-Sleep -Milliseconds 250;$old.Refresh()}
 if($old.HasExited -or $old.MainWindowHandle -eq 0){throw 'Installed WPF window did not open'}
 $id=[Guid]::NewGuid().ToString('N');$work=Join-Path $state ('updates/'+$id);New-Item -ItemType Directory -Force $work | Out-Null
 Copy-Item dist/release-0.19.1/Kachalka-win-x64.zip (Join-Path $work 'package.zip')
 Copy-Item dist/release-0.19.1/latest.json $work;Copy-Item dist/release-0.19.1/latest.sig $work
 Copy-Item (Join-Path $install 'Kachalka.Updater.exe') (Join-Path $work 'updater.exe')
 $job=Join-Path $work 'job.json'
 @{InstallDirectory=$install;ArchivePath=(Join-Path $work 'package.zip');ManifestPath=(Join-Path $work 'latest.json');SignaturePath=(Join-Path $work 'latest.sig');ParentPid=$old.Id;ParentStartTicks=$old.StartTime.ToUniversalTime().Ticks;Id=$id} | ConvertTo-Json | Set-Content $job -Encoding utf8NoBOM
 $helper=Start-Process (Join-Path $work 'updater.exe') -ArgumentList @('--job',('"'+$job+'"')) -PassThru;$started+=$helper
 if(-not $old.CloseMainWindow() -or -not $old.WaitForExit(30000)){throw 'Old application did not close cleanly'}
 if(-not $helper.WaitForExit(90000) -or $helper.ExitCode -ne 0){throw 'Real Windows update failed; see result.txt'}
 if((Get-Content (Join-Path $work 'healthy') -Raw) -ne '0.19.1'){throw 'New WPF window failed health check'}
 if((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Kachalka').DisplayVersion -ne '0.19.1'){throw 'Registered version not updated'}
 if(-not(Test-Path (Join-Path $state 'settings.json')) -or -not(Test-Path $folder)){throw 'User state lost'}
 'PASS: signed 0.19.0 -> 0.19.1 update, WPF startup, registry version and settings preserved' | Add-Content (Join-Path $evidence 'checks.txt')
 Get-Process Kachalka -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $install 'Kachalka.exe')} | ForEach-Object {if(-not $_.CloseMainWindow() -or -not $_.WaitForExit(30000)){throw 'Updated application did not close'}}
 $uninstaller=Start-Process (Join-Path $env:LOCALAPPDATA 'Kachalka/installation/Uninstall.exe') -ArgumentList '/S' -PassThru
 if(-not $uninstaller.WaitForExit(60000)){throw 'Uninstaller timeout'}
 $limit=(Get-Date).AddSeconds(20)
 while((Test-Path (Join-Path $install 'Kachalka.exe')) -and (Get-Date) -lt $limit){Start-Sleep -Milliseconds 250}
 if(Test-Path (Join-Path $install 'Kachalka.exe')){throw 'Application was not removed'}
 if(-not(Test-Path (Join-Path $state 'settings.json'))){throw 'Uninstaller removed settings'}
 'PASS: uninstall removes executable and preserves user state' | Add-Content (Join-Path $evidence 'checks.txt')
 Get-Content (Join-Path $evidence 'checks.txt')
}finally{
 [System.IO.File]::WriteAllText($publicKeyPath,$originalPublicKey)
 foreach($p in $started){if(-not $p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
 Get-Process Kachalka -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $env:LOCALAPPDATA 'Programs/Kachalka/Kachalka.exe')} | Stop-Process
 Remove-Item $keyPath -ErrorAction SilentlyContinue
 # Do not upload binaries or generated private test keys as evidence.
 Remove-Item (Join-Path $evidence '*.exe') -ErrorAction SilentlyContinue
}
