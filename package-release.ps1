param(
 [string]$Version='0.32.0',
 [string]$ArchivePath='dist/Kachalka-0.32.zip',
 [string]$Repository='peregond/ka4alka',
 [string]$SigningKeyPath=$env:KACHALKA_SIGNING_KEY
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must have three numeric components'}
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid repository name'}
if($Repository -ne 'peregond/ka4alka'){throw 'Repository differs from the update trust policy'}
if(-not $SigningKeyPath){throw 'Supply -SigningKeyPath with the private release-signing PEM file'}
$signer=[System.Security.Cryptography.RSA]::Create()
try{
 $signer.ImportFromPem([System.IO.File]::ReadAllText($SigningKeyPath))
 $verifier=[System.Security.Cryptography.RSA]::Create()
 try{
  $verifier.ImportFromPem([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'update-core/update-public.pem')))
  $challenge=[System.Text.Encoding]::UTF8.GetBytes('Kachalka release key check')
  $proof=$signer.SignData($challenge,[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
  if(-not $verifier.VerifyData($challenge,$proof,[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Signing key does not match embedded public key'}
 }finally{$verifier.Dispose()}
}catch{$signer.Dispose();throw}
$sourcePath=if([System.IO.Path]::IsPathRooted($ArchivePath)){$ArchivePath}else{Join-Path $PSScriptRoot $ArchivePath}
$archiveFile=Get-Item -LiteralPath $sourcePath
if($archiveFile.Extension -ne '.zip'){throw 'Release archive must be a ZIP'}
$zip=[System.IO.Compression.ZipFile]::OpenRead($archiveFile.FullName)
try{
 $executable=@($zip.Entries | Where-Object {$_.FullName -match '^[^/]+/Kachalka\.exe$'})
 $library=@($zip.Entries | Where-Object {$_.FullName -match '^[^/]+/Kachalka\.dll$'})
 if($executable.Count -ne 1 -or $library.Count -ne 1){throw 'Archive must contain exactly one application folder'}
 if(@($zip.Entries | Where-Object {$_.FullName -match '^[^/]+/Kachalka\.Updater\.exe$'}).Count -ne 1){throw 'Archive must include the updater'}
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
$indexFile=Join-Path $releaseDirectory 'components.json'
$sdk=Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
& $sdk run --project (Join-Path $PSScriptRoot 'update-tests/Kachalka.UpdateTests.csproj') -c Release -- --create-component-index $packageFile $Version $indexFile
if($LASTEXITCODE -ne 0){throw 'Component inventory generation failed'}
$manifest=[ordered]@{
 schemaVersion=1;appId='kachalka';channel='stable';version=$Version;platform='win-x64';minimumWindows='10'
 package=[ordered]@{url=('https://github.com/'+$Repository+'/releases/download/'+$tag+'/Kachalka-win-x64.zip');fileName='Kachalka-win-x64.zip';size=(Get-Item -LiteralPath $packageFile).Length;sha256=$hash;format='portable-zip'}
 releaseNotesUrl=('https://github.com/'+$Repository+'/releases/tag/'+$tag)
 automaticInstallationAvailable=$true
 components=[ordered]@{url=('https://github.com/'+$Repository+'/releases/download/'+$tag+'/components.json');size=(Get-Item -LiteralPath $indexFile).Length;sha256=(Get-FileHash -LiteralPath $indexFile -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'latest.json') -Encoding utf8NoBOM
try{
 $manifestBytes=[System.IO.File]::ReadAllBytes((Join-Path $releaseDirectory 'latest.json'))
 $signature=$signer.SignData($manifestBytes,[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
 [System.IO.File]::WriteAllBytes((Join-Path $releaseDirectory 'latest.sig'),$signature)
}finally{$signer.Dispose()}
($hash+'  Kachalka-win-x64.zip') | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{Version=$Version;Directory=$releaseDirectory;Archive=$packageFile;Manifest=(Join-Path $releaseDirectory 'latest.json');Checksum=(Join-Path $releaseDirectory 'SHA256SUMS.txt');SHA256=$hash} | ConvertTo-Json
