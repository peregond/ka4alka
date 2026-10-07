$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$root=$PWD.Path
$evidence=Join-Path $root 'test-output/shell-compatibility';New-Item -ItemType Directory -Force $evidence | Out-Null
$env:KACHALKA_DATA=Join-Path $evidence 'data';New-Item -ItemType Directory -Force $env:KACHALKA_DATA | Out-Null
$folder=Join-Path $env:KACHALKA_DATA 'downloads';New-Item -ItemType Directory -Force $folder | Out-Null
@{Folder=$folder;FolderConfigured=$true;AutoResumeDownloads=$false;CheckForUpdates=$false;AutoUpdate=$false} | ConvertTo-Json | Set-Content (Join-Path $env:KACHALKA_DATA 'settings.json') -Encoding utf8NoBOM
$setup=Join-Path $env:RUNNER_TEMP 'OpenShellSetup_4_4_198.exe'
Invoke-WebRequest 'https://github.com/Open-Shell/Open-Shell-Menu/releases/download/v4.4.198/OpenShellSetup_4_4_198.exe' -OutFile $setup -TimeoutSec 90
$openInstall=Start-Process $setup -ArgumentList '/qn','/norestart','ADDLOCAL=StartMenu' -PassThru
if(-not $openInstall.WaitForExit(90000) -or $openInstall.ExitCode -notin @(0,3010)){throw ('Open-Shell fixture installation failed: '+$openInstall.ExitCode)}
$shellDirectory=Join-Path $env:ProgramFiles 'Open-Shell'
$shellExe=Join-Path $shellDirectory 'StartMenu.exe'
if(-not(Test-Path $shellExe)){throw 'Open-Shell fixture executable missing'}
$null=Start-Process $shellExe -ArgumentList '-startup' -PassThru
Start-Sleep -Seconds 5
$shellProcesses=@(Get-Process StartMenu -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $shellExe})
if($shellProcesses.Count -eq 0){throw 'Open-Shell did not remain running on this Windows runner; no coexistence result can be claimed'}
$beforeExplorer=@(Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object {$_.Id})
$settingsKeys=@('HKCU:/Software/IvoSoft/ClassicStartMenu/Settings','HKCU:/Software/OpenShell/StartMenu/Settings','HKLM:/Software/IvoSoft/ClassicStartMenu/Settings')
function SettingsSnapshot {
 $snapshot=[ordered]@{}
 foreach($key in $settingsKeys){
   $values=[ordered]@{}
   if(Test-Path $key){$item=Get-Item $key;foreach($name in @('MenuStyle','EnableSettings','EnableStartButton','StartButtonType','SkinW7','Language')){$values[$name]=$item.GetValue($name,$null)}}
   $snapshot[$key]=$values
 }
 return ($snapshot | ConvertTo-Json -Depth 5 -Compress)
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellMenuProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 public static string Activate() {
   var button=FindWindow("OpenShell.CStartButton",null);
   if(button!=IntPtr.Zero && IsWindowVisible(button) && GetWindowRect(button,out var rect)) {
     SetCursorPos((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2);
     mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);return "Open-Shell start button click";
   }
   keybd_event(0x5B,0,0,UIntPtr.Zero);keybd_event(0x5B,0,2,UIntPtr.Zero);return "Windows key";
 }
 public static void Escape() {keybd_event(0x1B,0,0,UIntPtr.Zero);keybd_event(0x1B,0,2,UIntPtr.Zero);}
}
'@
function TestMenuActivation {
 $method=[ShellMenuProbe]::Activate();Start-Sleep -Milliseconds 600
 $menu=[ShellMenuProbe]::FindWindow('OpenShell.CMenuContainer',$null)
 $visible=$menu -ne [IntPtr]::Zero -and [ShellMenuProbe]::IsWindowVisible($menu)
 [ShellMenuProbe]::Escape();Start-Sleep -Milliseconds 300
 Write-Output ([pscustomobject]@{Visible=$visible;Method=$method})
}
$baselineMenu=TestMenuActivation
Write-Output ('Open-Shell activation before installing Kachalka: '+($baselineMenu | ConvertTo-Json -Compress))
$beforeSettings=SettingsSnapshot
$beforeFiles=@{}
Get-ChildItem $shellDirectory -Filter '*.dll' | ForEach-Object {$beforeFiles[$_.FullName]=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
$beforeFiles[$shellExe]=(Get-FileHash $shellExe -Algorithm SHA256).Hash
function AssertShell([string]$stage) {
 foreach($process in $shellProcesses){$process.Refresh();if($process.HasExited){throw ('Open-Shell exited at '+$stage)}}
 if((SettingsSnapshot) -ne $beforeSettings){throw ('Open-Shell settings changed at '+$stage)}
 foreach($path in $beforeFiles.Keys){if(-not(Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $beforeFiles[$path]){throw ('Open-Shell executable or DLL changed at '+$stage)}}
 foreach($id in $beforeExplorer){if(-not(Get-Process -Id $id -ErrorAction SilentlyContinue)){throw ('Existing Explorer process exited at '+$stage)}}
 $activation=TestMenuActivation
 if($baselineMenu.Visible -and -not $activation.Visible){throw ('Start activation changed to the system menu at '+$stage)}
 Write-Output ('PASS: Open-Shell stays running, settings and module hashes unchanged at '+$stage+'; classic menu visible='+$activation.Visible)
}
./build.ps1 -OutputDir 'dist/shell-app'
if($LASTEXITCODE -ne 0){throw 'Compatibility application build failed'}
$uninstall=Join-Path $env:RUNNER_TEMP 'shell-uninstall.nsh';./installer/write-uninstall.ps1 -ApplicationDirectory 'dist/shell-app' -OutputPath $uninstall
$version=([xml](Get-Content native/Kachalka.csproj)).Project.PropertyGroup.Version
$ourSetup=Join-Path $env:RUNNER_TEMP 'Kachalka-shell-test.exe'
$app=[System.IO.Path]::GetFullPath('dist/shell-app')
& 'C:/Program Files (x86)/NSIS/makensis.exe' '/V2' '/INPUTCHARSET' 'UTF8' ('/DVERSION='+$version) ('/DAPP_DIR='+$app) ('/DAPP_GLOB='+$app+'\*') ('/DUNINSTALL_SCRIPT='+$uninstall) ('/DOUTPUT='+$ourSetup) installer/Kachalka.nsi
if($LASTEXITCODE -ne 0){throw 'Compatibility installer compilation failed'}
$installer=Start-Process $ourSetup -ArgumentList '/S' -PassThru
if(-not $installer.WaitForExit(60000) -or $installer.ExitCode -ne 0){throw 'Compatibility installation failed'}
AssertShell 'installation'
$installed=Join-Path $env:LOCALAPPDATA 'Programs/Kachalka/Kachalka.exe'
$closeEvidence=Join-Path $evidence 'close'
$appProcess=Start-Process $installed -ArgumentList @('--close-smoke-test',('"'+$closeEvidence+'"')) -PassThru
if(-not $appProcess.WaitForExit(20000)){$appProcess.Kill($true);throw 'Installed application did not close with Open-Shell running'}
if(Test-Path (Join-Path $closeEvidence 'error.txt')){throw (Get-Content (Join-Path $closeEvidence 'error.txt') -Raw)}
$closeResult=Get-Content (Join-Path $closeEvidence 'closed.json') -Raw | ConvertFrom-Json
if(-not $closeResult.QueueSaved -or $closeResult.ElapsedSeconds -gt 10){throw 'Closing did not preserve queue promptly'}
AssertShell 'application startup and native window close'
$uninstaller=Start-Process (Join-Path $env:LOCALAPPDATA 'Kachalka/installation/Uninstall.exe') -ArgumentList '/S' -PassThru
if(-not $uninstaller.WaitForExit(60000)){throw 'Compatibility uninstall timed out'}
Start-Sleep -Seconds 2
AssertShell 'uninstall'
@{OS=[Environment]::OSVersion.VersionString;RunnerImage=$env:ImageOS;OpenShell='4.4.198';MenuActivationVerified=$baselineMenu.Visible;ActivationMethod=$baselineMenu.Method;ShellRemainsRunning=$true;ShellConfigurationUnchanged=$true;ShellFilesUnchanged=$true;ExistingExplorerProcessesPreserved=$true;NativeCloseSeconds=$closeResult.ElapsedSeconds;QueueSaved=$closeResult.QueueSaved} | ConvertTo-Json | Set-Content (Join-Path $evidence 'checks.json') -Encoding utf8NoBOM
Get-Content (Join-Path $evidence 'checks.json')
