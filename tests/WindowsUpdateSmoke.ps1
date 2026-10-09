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
$version=([xml](Get-Content native/Kachalka.csproj)).Project.PropertyGroup.Version
$nextVersion=([Version]$version).Major.ToString()+'.'+([Version]$version).Minor+'.'+(([Version]$version).Build+1)
$short=([Version]$version).ToString(2)
try{
 dotnet run --project lan-tests/Kachalka.LanTests.csproj -c Release
 if($LASTEXITCODE -ne 0){throw 'Local-network pairing and download transfer tests failed'}
 dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release
 if($LASTEXITCODE -ne 0){throw 'Updater tests failed'}
 ./tests/StableUpdateComponents.ps1
 ./build.ps1 -Test -OutputDir ('dist/Kachalka-'+$short)
 foreach($scenario in @('dpi','close','cache','download-reliability','person-performance','cover-viewport','catalog-navigation','release-freshness','interface-polish','bug-report','lan')){
   $scenarioDirectory=if($scenario -in @('dpi','close','cache')){$scenario+'-reliability'}elseif($scenario -eq 'lan'){'lan-ui'}else{$scenario}
   $scenarioEvidence=Join-Path $root ('test-output/'+$scenarioDirectory)
   $scenarioUi=Start-Process (Join-Path $root ('dist/Kachalka-'+$short+'/Kachalka.exe')) -ArgumentList @('--'+$scenario+'-smoke-test',('"'+$scenarioEvidence+'"')) -PassThru
   if(-not $scenarioUi.WaitForExit(45000)){$scenarioUi.Kill($true);throw ($scenario+' UI smoke timed out')}
   if(Test-Path (Join-Path $scenarioEvidence 'error.txt')){throw (Get-Content (Join-Path $scenarioEvidence 'error.txt') -Raw)}
   $scenarioFile=if($scenario -eq 'dpi'){'dpi-viewports.json'}elseif($scenario -eq 'close'){'closed.json'}else{'checks.json'}
   if(-not(Test-Path (Join-Path $scenarioEvidence $scenarioFile))){throw ($scenario+' UI evidence missing')}
   if($scenario -eq 'close'){
     $closeResult=Get-Content (Join-Path $scenarioEvidence $scenarioFile) -Raw | ConvertFrom-Json
     if(-not $closeResult.QueueSaved -or $closeResult.ElapsedSeconds -gt 10){throw 'Native window close delayed or lost queue'}
     # This unfinished task belongs only to the close regression fixture.
     '[]' | Set-Content (Join-Path $state 'queue.json') -Encoding utf8NoBOM
   }
   Get-Content (Join-Path $scenarioEvidence $scenarioFile)
 }
 $cinemaEvidence=Join-Path $root 'test-output/cinema-ui'
 $cinemaUi=Start-Process (Join-Path $root ('dist/Kachalka-'+$short+'/Kachalka.exe')) -ArgumentList @('--cinema-smoke-test',('"'+$cinemaEvidence+'"')) -PassThru
 if(-not $cinemaUi.WaitForExit(45000)){$cinemaUi.Kill($true);throw 'Cinema UI smoke timed out'}
 if(Test-Path (Join-Path $cinemaEvidence 'error.txt')){throw (Get-Content (Join-Path $cinemaEvidence 'error.txt') -Raw)}
 if(-not(Test-Path (Join-Path $cinemaEvidence 'checks.json'))){throw 'Cinema UI evidence missing'}
 ./tests/CacheDesignPosters.ps1
 $designEvidence=Join-Path $root 'test-output/design-modern'
 $designUi=Start-Process (Join-Path $root ('dist/Kachalka-'+$short+'/Kachalka.exe')) -ArgumentList @('--design-smoke-test',('"'+$designEvidence+'"')) -PassThru
 if(-not $designUi.WaitForExit(120000)){ $designUi.Kill($true);throw 'Modern design UI smoke timed out' }
 if(Test-Path (Join-Path $designEvidence 'error.txt')){throw (Get-Content (Join-Path $designEvidence 'error.txt') -Raw)}
 if(-not(Test-Path (Join-Path $designEvidence 'design.json'))){throw 'Modern design UI evidence missing'}
 Get-Content (Join-Path $designEvidence 'design.json')
 $catalogEvidence=Join-Path $root 'test-output/catalog-paging'
 $ui=Start-Process (Join-Path $root ('dist/Kachalka-'+$short+'/Kachalka.exe')) -ArgumentList @('--catalog-paging-smoke-test',('"'+$catalogEvidence+'"')) -PassThru
 if(-not $ui.WaitForExit(60000)){ $ui.Kill($true);throw 'Catalog UI smoke timed out' }
 if(Test-Path (Join-Path $catalogEvidence 'error.txt')){if(Test-Path (Join-Path $catalogEvidence 'checks.txt')){Get-Content (Join-Path $catalogEvidence 'checks.txt') | Write-Output};throw (Get-Content (Join-Path $catalogEvidence 'error.txt') -Raw)}
 if(-not(Test-Path (Join-Path $catalogEvidence 'checks.txt'))){throw 'Catalog UI evidence missing'}
 Get-Content (Join-Path $catalogEvidence 'checks.txt')
 $downloadEvidence=Join-Path $root 'test-output/download-end-to-end'
 $downloadUi=Start-Process (Join-Path $root ('dist/Kachalka-'+$short+'/Kachalka.exe')) -ArgumentList @('--end-to-end-smoke-test',('"'+$downloadEvidence+'"')) -PassThru
 if(-not $downloadUi.WaitForExit(60000)){ $downloadUi.Kill($true);throw 'Download and poster UI smoke timed out' }
 if(Test-Path (Join-Path $downloadEvidence 'error.txt')){throw (Get-Content (Join-Path $downloadEvidence 'error.txt') -Raw)}
 if(-not(Test-Path (Join-Path $downloadEvidence 'end-to-end.json'))){if(Test-Path (Join-Path $state 'error.log')){Get-Content (Join-Path $state 'error.log')};throw ('Download and poster UI evidence missing; exit code '+$downloadUi.ExitCode)}
 Get-Content (Join-Path $downloadEvidence 'end-to-end.json')
 $uninstall=Join-Path $root '.tools/uninstall-files.nsh'
 ./installer/write-uninstall.ps1 -ApplicationDirectory ('dist/Kachalka-'+$short) -OutputPath $uninstall
 $setup=Join-Path $evidence ('Kachalka-Setup-'+$version+'.exe')
 $appDirectory=[System.IO.Path]::GetFullPath((Join-Path $root ('dist/Kachalka-'+$short)))
 & 'C:/Program Files (x86)/NSIS/makensis.exe' '/V2' '/INPUTCHARSET' 'UTF8' ('/DVERSION='+$version) ('/DAPP_DIR='+$appDirectory) ('/DAPP_GLOB='+$appDirectory+'\*') ('/DUNINSTALL_SCRIPT='+$uninstall) ('/DOUTPUT='+$setup) installer/Kachalka.nsi
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
 if((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Kachalka').DisplayVersion -ne $version){throw 'Wrong installed version'}
 ./tests/WindowsInstallerFinishSmoke.ps1 -Setup $setup -Output (Join-Path $root 'test-output/design-modern')
 ('PASS: EXE installation, shortcut, registered version '+$version) | Add-Content (Join-Path $evidence 'checks.txt')

 dotnet publish native/Kachalka.csproj -c Release -r win-x64 --self-contained true ("-p:Version="+$nextVersion) -p:DebugType=None -p:DebugSymbols=false -o ("dist/Kachalka-"+$nextVersion)
 if($LASTEXITCODE -ne 0){throw 'Next-version fixture build failed'}
 Copy-Item '.tools/updater-publish/Kachalka.Updater.exe' ('dist/Kachalka-'+$nextVersion+'/')
 Compress-Archive -LiteralPath ("dist/Kachalka-"+$nextVersion) -DestinationPath ("dist/Kachalka-"+$nextVersion+".zip") -Force
 ./package-release.ps1 -Version $nextVersion -ArchivePath ("dist/Kachalka-"+$nextVersion+".zip") -SigningKeyPath $keyPath
 dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release -- --verify-release ("dist/release-"+$nextVersion)
 if($LASTEXITCODE -ne 0){throw 'Signed fixture verification failed'}
 $old=Start-Process (Join-Path $install 'Kachalka.exe') -PassThru;$started+= $old
 $limit=(Get-Date).AddSeconds(30)
 while(-not $old.HasExited -and $old.MainWindowHandle -eq 0 -and (Get-Date) -lt $limit){Start-Sleep -Milliseconds 250;$old.Refresh()}
 if($old.HasExited -or $old.MainWindowHandle -eq 0){throw 'Installed WPF window did not open'}
 $id=[Guid]::NewGuid().ToString('N');$work=Join-Path $state ('updates/'+$id);New-Item -ItemType Directory -Force $work | Out-Null
 # Real component client with local HTTP Range fixture; never stage the full package.
 [System.IO.File]::WriteAllText((Join-Path $install 'obsolete-component.txt'),'old component')
 dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release -- --prepare-components $install ("dist/release-"+$nextVersion) $work
 if($LASTEXITCODE -ne 0){throw 'Component preparation failed'}
 $metrics=Get-Content (Join-Path $work 'metrics.json') -Raw | ConvertFrom-Json
 if($metrics.Downloaded -ge $metrics.Package/4 -or $metrics.Reused -lt 100){throw 'Unchanged components were downloaded'}
 ('PASS: component download '+$metrics.Downloaded+' bytes vs full ZIP '+$metrics.Package+' bytes; '+$metrics.Reused+' files reused') | Add-Content (Join-Path $evidence 'checks.txt')
 Copy-Item ("dist/release-"+$nextVersion+"/latest.json") $work;Copy-Item ("dist/release-"+$nextVersion+"/latest.sig") $work
 Copy-Item (Join-Path $install 'Kachalka.Updater.exe') (Join-Path $work 'updater.exe')
 $job=Join-Path $work 'job.json'
 @{InstallDirectory=$install;ArchivePath=(Join-Path $work 'package.zip');ManifestPath=(Join-Path $work 'latest.json');SignaturePath=(Join-Path $work 'latest.sig');ComponentsPath=(Join-Path $work 'components.json');ComponentsDirectory=(Join-Path $work 'components');ParentPid=$old.Id;ParentStartTicks=$old.StartTime.ToUniversalTime().Ticks;Id=$id} | ConvertTo-Json | Set-Content $job -Encoding utf8NoBOM
 $helper=Start-Process (Join-Path $work 'updater.exe') -ArgumentList @('--job',('"'+$job+'"')) -PassThru;$started+=$helper
 if(-not $old.CloseMainWindow() -or -not $old.WaitForExit(30000)){throw 'Old application did not close cleanly'}
 if(-not $helper.WaitForExit(90000) -or $helper.ExitCode -ne 0){throw 'Real Windows update failed; see result.txt'}
 if(Test-Path (Join-Path $install 'obsolete-component.txt')){throw 'Obsolete component survived update'}
 if(Test-Path (Join-Path $work 'package.zip')){throw 'Component update downloaded full archive'}
 if((Get-Content (Join-Path $work 'healthy') -Raw) -ne $nextVersion){throw 'New WPF window failed health check'}
 if((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Kachalka').DisplayVersion -ne $nextVersion){throw 'Registered version not updated'}
 if(-not(Test-Path (Join-Path $state 'settings.json')) -or -not(Test-Path $folder)){throw 'User state lost'}
 ('PASS: signed '+$version+' -> '+$nextVersion+' update, WPF startup, registry version and settings preserved') | Add-Content (Join-Path $evidence 'checks.txt')
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
