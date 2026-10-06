$ErrorActionPreference='Stop'
$output=Join-Path (Split-Path $PSScriptRoot) '.tools/stable-updater'
$hashes=@()
foreach($revision in @('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')){
 $target=Join-Path $output $revision
 dotnet publish updater/Kachalka.Updater.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false ('-p:SourceRevisionId='+$revision) -o $target
 if($LASTEXITCODE -ne 0){throw 'Stable updater publish failed'}
 $hashes+=(Get-FileHash (Join-Path $target 'Kachalka.Updater.exe') -Algorithm SHA256).Hash
}
if($hashes[0] -ne $hashes[1]){throw 'Unchanged updater differs solely because of source revision'}
Write-Output 'PASS: updater binary identical across different source revisions'
Remove-Item -Recurse -Force $output
