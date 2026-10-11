$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$base='https://ka4alka-online-new.peregon.chatgpt.site/'
# The endpoint must be the one compiled into the application being released.
if(-not (Get-Content native/OnlineIndexClient.cs -Raw).Contains('new("'+$base+'")')){throw 'Published site probe differs from the application endpoint'}
function Read-Api([string]$path){
 $response=Invoke-WebRequest ($base+$path) -TimeoutSec 60 -Headers @{'User-Agent'='Kachalka/0.11'}
 if($response.StatusCode -ne 200 -or $response.Headers.'Content-Type' -notmatch 'application/json'){throw ('Public API did not return JSON: '+$path)}
 return $response.Content | ConvertFrom-Json
}
$first=Read-Api 'api/catalog?section=movies&page=1'
$distant=Read-Api 'api/catalog?section=movies&page=50'
$series=Read-Api 'api/catalog?section=series&page=1'
foreach($page in @($first,$distant,$series)){if(@($page.items).Count -ne 40){throw 'Public catalog page is incomplete'}}
if($first.indexCount -lt 2000 -or $series.indexCount -lt 300){throw 'Public library is incomplete'}
if(@($distant.items | Where-Object {$_.id -in $first.items.id}).Count){throw 'Catalog page 50 repeats the first page'}
$year=[int]::MaxValue
foreach($item in $first.items){if([int]$item.year -gt $year){throw 'Newer releases are not shown first'};$year=[int]$item.year}
$id=$first.items[0].id
$detail=Read-Api ('api/media?id='+[Uri]::EscapeDataString($id))
if($detail.item.id -ne $id -or [string]::IsNullOrWhiteSpace($detail.item.title)){throw 'Public card metadata is unavailable'}
$releases=Read-Api ('api/releases?id='+[Uri]::EscapeDataString($id))
if('items' -notin $releases.PSObject.Properties.Name){throw 'Public release API is unavailable'}
# A desktop release must not ship before its new server route and both providers work.
$titles=@(
 @{Imdb='tt8910922';Section='series';Source='TheTVDB';Id=375892},
 @{Imdb='tt1160419';Section='movies';Source='TMDB';Id=438631}
)
foreach($title in $titles){
 $item=(Read-Api ('api/title?imdb='+$title.Imdb+'&section='+$title.Section)).item
 if($null -eq $item -or $item.imdbId -ne $title.Imdb -or $item.section -ne $title.Section -or $item.source -ne $title.Source -or $item.sourceId -ne $title.Id){throw ('Published title identity or provider differs: '+$title.Imdb)}
 if([string]::IsNullOrWhiteSpace($item.overview) -or @($item.actors).Count -eq 0 -or @($item.genres).Count -eq 0){throw ('Published title description, genres or cast are missing: '+$title.Imdb)}
}
$evidence='test-output/published-site';New-Item -ItemType Directory -Force $evidence | Out-Null
@{Site=$base;Movies=$first.indexCount;Series=$series.indexCount;Page50=@($distant.items).Count;Card=$detail.item.title;Releases=@($releases.items).Count;TitleProviders=@('TheTVDB','TMDB');PublicApi=$true} | ConvertTo-Json | Set-Content (Join-Path $evidence 'checks.json') -Encoding utf8NoBOM
Get-Content (Join-Path $evidence 'checks.json')
'PASS: public replacement site serves both libraries, distinct page 50, newest-first cards, releases and TheTVDB/TMDB title descriptions.'
