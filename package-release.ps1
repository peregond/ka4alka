param(
 [string]$Version='0.19.0',
 [string]$ArchivePath='dist/Kachalka-0.19.zip',
 [string]$Repository='peregond/ka4alka'
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must have three numeric components'}
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid repository name'}
$sourcePath=if([System.IO.Path]::IsPathRooted($ArchivePath)){$ArchivePath}else{Join-Path $PSScriptRoot $ArchivePath}
$archiveFile=Get-Item -LiteralPath $sourcePath
if($archiveFile.Extension -ne '.zip'){throw 'Release archive must be a ZIP'}
$zip=[System.IO.Compression.ZipFile]::OpenRead($archiveFile.FullName)
try{
 $executable=@($zip.Entries | Where-Object {$_.FullName -match '^[^/]+/Kachalka\.exe$'})
 $library=@($zip.Entries | Where-Object {$_.FullName -match '^[^/]+/Kachalka\.dll$'})
 if($executable.Count -ne 1 -or $library.Count -ne 1){throw 'Archive must contain exactly one application folder'}
 if(($executable[0].FullName -replace '/Kachalka\.exe$','') -ne ($library[0].FullName -replace '/Kachalka\.dll$','')){throw 'Executable and assembly must be in the same folder'}
 if($zip.Entries.FullName -match 'Kachalka.Tests'){throw 'Test executable must not be distributed'}
 $assemblyBytes=[System.IO.MemoryStream]::new()
 $entryStream=$library[0].Open()
 try{$entryStream.CopyTo($assemblyBytes)}finally{$entryStream.Dispose()}
 $assemblyBytes.Position=0
 $reader=[System.Reflection.PortableExecutable.PEReader]::new($assemblyBytes)
 try{
  $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
  if($metadata.GetAssemblyDefinition().Version.ToString(3) -ne $Version){throw 'Archive assembly version differs from requested release'}
 }finally{$reader.Dispose()}
}finally{$zip.Dispose()}
$releaseDirectory=Join-Path $PSScriptRoot ('dist/release-'+$Version)
New-Item -ItemType Directory -Force $releaseDirectory | Out-Null
$packageFile=Join-Path $releaseDirectory 'Kachalka-win-x64.zip'
Copy-Item -LiteralPath $archiveFile.FullName -Destination $packageFile -Force
$hash=(Get-FileHash -LiteralPath $packageFile -Algorithm SHA256).Hash.ToLowerInvariant()
$tag='v'+$Version
$manifest=[ordered]@{
 schemaVersion=1;appId='kachalka';channel='stable';version=$Version;platform='win-x64';minimumWindows='10'
 package=[ordered]@{url=('https://github.com/'+$Repository+'/releases/download/'+$tag+'/Kachalka-win-x64.zip');fileName='Kachalka-win-x64.zip';size=(Get-Item -LiteralPath $packageFile).Length;sha256=$hash;format='portable-zip'}
 releaseNotesUrl=('https://github.com/'+$Repository+'/releases/tag/'+$tag)
 automaticInstallationAvailable=$false
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'latest.json') -Encoding utf8NoBOM
($hash+'  Kachalka-win-x64.zip') | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{Version=$Version;Directory=$releaseDirectory;Archive=$packageFile;Manifest=(Join-Path $releaseDirectory 'latest.json');Checksum=(Join-Path $releaseDirectory 'SHA256SUMS.txt');SHA256=$hash} | ConvertTo-Json
