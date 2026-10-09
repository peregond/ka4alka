$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$output=Join-Path $PWD '.tools/stable-updater'
function Assert-NoPortableDebug([string]$path){
 $stream=[System.IO.File]::OpenRead($path)
 $reader=[System.Reflection.PortableExecutable.PEReader]::new($stream)
 try{
  # A deterministic Reproducible marker is harmless. PDB GUIDs/checksums
  # inherit SourceLink's actual repository commit, even without a version suffix.
  $records=@($reader.ReadDebugDirectory() | Where-Object {$_.Type.ToString() -in @('CodeView','PdbChecksum','EmbeddedPortablePdb')})
  if($records.Count -gt 0){throw ('Release update component contains commit-dependent debug metadata: '+$path)}
 }finally{$reader.Dispose();$stream.Dispose()}
}
$core=Join-Path $PWD 'update-core/bin/Release/net10.0/Kachalka.UpdateCore.dll'
$helper=Join-Path $PWD 'updater/bin/Release/net10.0/win-x64/Kachalka.Updater.dll'
try{
 # Reproduce the normal test build that previously primed a portable-PDB core
 # for publication. No DebugType/DebugSymbols command-line overrides are allowed.
 dotnet build update-core/Kachalka.UpdateCore.csproj -c Release --no-incremental
 if($LASTEXITCODE -ne 0){throw 'Ordinary Release component build failed'}
 Assert-NoPortableDebug $core
 Write-Output 'PASS: ordinary Release core build contains no portable debug metadata'
 $hashes=@()
 foreach($revision in @('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')){
  $target=Join-Path $output $revision
  dotnet publish updater/Kachalka.Updater.csproj -c Release -r win-x64 --self-contained true ('-p:SourceRevisionId='+$revision) -o $target
  if($LASTEXITCODE -ne 0){throw 'Stable updater publish failed'}
  Assert-NoPortableDebug $core;Assert-NoPortableDebug $helper
  $hashes+=@{Core=(Get-FileHash $core -Algorithm SHA256).Hash;Helper=(Get-FileHash (Join-Path $target 'Kachalka.Updater.exe') -Algorithm SHA256).Hash}
 }
 if($hashes[0].Core -ne $hashes[1].Core -or $hashes[0].Helper -ne $hashes[1].Helper){throw 'Unchanged update components differ solely because of source revision'}
 Write-Output 'PASS: core and updater inputs have no portable debug metadata; binaries are identical across different source revisions'
}finally{Remove-Item -Recurse -Force $output -ErrorAction SilentlyContinue}
