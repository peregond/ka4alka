param([Parameter(Mandatory)][string]$ApplicationDirectory,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$root=(Get-Item -LiteralPath $ApplicationDirectory).FullName
$instructions=[System.Collections.Generic.List[string]]::new()
foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File){
 $relative=[System.IO.Path]::GetRelativePath($root,$file.FullName).Replace('/','\')
 if($relative.Contains('$') -or $relative.Contains('"') -or ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)){throw 'Unsafe release filename'}
 $instructions.Add('Delete "$INSTDIR\'+$relative+'"')
}
foreach($directory in Get-ChildItem -LiteralPath $root -Recurse -Directory | Sort-Object {$_.FullName.Length} -Descending){
 $relative=[System.IO.Path]::GetRelativePath($root,$directory.FullName).Replace('/','\')
 if($relative.Contains('$') -or $relative.Contains('"') -or ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint)){throw 'Unsafe release directory'}
 $instructions.Add('RMDir "$INSTDIR\'+$relative+'"')
}
$instructions.Add('RMDir "$INSTDIR"')
$instructions | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
