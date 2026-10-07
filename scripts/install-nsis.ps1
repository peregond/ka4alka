$ErrorActionPreference='Stop'
$compiler='C:/Program Files (x86)/NSIS/makensis.exe'
for($attempt=1;$attempt -le 3;$attempt++){
 if(Test-Path -LiteralPath $compiler){& $compiler /VERSION;exit 0}
 choco install nsis --yes --no-progress
 if(Test-Path -LiteralPath $compiler){& $compiler /VERSION;exit 0}
 if($attempt -lt 3){Write-Output ('NSIS package feed unavailable; retry '+$attempt);Start-Sleep -Seconds 5}
}
throw 'NSIS installation failed after three attempts; compiler not found.'
