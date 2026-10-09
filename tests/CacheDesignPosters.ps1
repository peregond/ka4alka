$ErrorActionPreference='Stop'
$coverFolder=Join-Path $env:KACHALKA_DATA 'covers'
New-Item -ItemType Directory -Force $coverFolder | Out-Null
$rows=Get-Content (Join-Path $PSScriptRoot '../web-index/app/data/seed.json') -Raw | ConvertFrom-Json
$posters=@(@($rows | Where-Object {$_.section -eq 'movies'} | Select-Object -First 40) + @($rows | Where-Object {$_.section -eq 'series'} | Select-Object -First 20) | ForEach-Object {$_.poster} | Where-Object {$_} | Select-Object -Unique)
$results=@($posters | ForEach-Object -Parallel {
 $url=$_
 $uri=[Uri]$url
 if($uri.Scheme -ne 'https' -or -not($uri.Host.EndsWith('.zonapic.com') -or $uri.Host -eq 'image.tmdb.org')){return $false}
 $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($url)))
 $path=Join-Path $using:coverFolder ($hash+'.img')
 try{Invoke-WebRequest $url -OutFile $path -TimeoutSec 8 -ErrorAction Stop;return $true}
 catch{Remove-Item $path -ErrorAction SilentlyContinue;return $false}
} -ThrottleLimit 6)
$loaded=@($results | Where-Object {$_ -eq $true}).Count
Write-Output ('Design preview: '+$loaded+' of '+$posters.Count+' public posters cached; unavailable images retain their normal fallback.')
