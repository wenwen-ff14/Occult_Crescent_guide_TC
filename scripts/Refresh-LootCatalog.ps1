#Requires -Version 7.0
param([Parameter(Mandatory)][string]$SqPack)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    New-Item -ItemType Directory -Force .references | Out-Null
    $taskHtml = (Invoke-WebRequest 'https://xivstats.com/occult').Content
    $taskMatches = [regex]::Matches($taskHtml, '<script[^>]*data-url="([^"]+)"[^>]*>(.*?)</script>', 'Singleline')
    $taskFound = 0
    foreach ($taskMatch in $taskMatches) {
        $taskUrl = $taskMatch.Groups[1].Value
        if ($taskUrl -notin @('/data/OccultTreasuresV2.json', '/website/mappings/Items.json.gz')) { continue }
        $taskData = $taskMatch.Groups[2].Value | ConvertFrom-Json
        if ($taskData.status -ne 200) { throw "Failed source: $taskUrl" }
        if ($taskUrl.EndsWith('.gz')) {
            $taskStream = [IO.Compression.GZipStream]::new([IO.MemoryStream]::new([Convert]::FromBase64String($taskData.body)), [IO.Compression.CompressionMode]::Decompress)
            $taskReader = [IO.StreamReader]::new($taskStream)
            try { Set-Content .references/loot-items.json $taskReader.ReadToEnd() -Encoding utf8 } finally { $taskReader.Dispose() }
        } else { Set-Content .references/loot-drops.json $taskData.body -Encoding utf8 }
        $taskFound++
    }
    if ($taskFound -ne 2) { throw 'Source format changed; do not replace the catalog.' }
    dotnet run --project tools/LootAudit -c Release -- $SqPack .references/loot-drops.json .references/loot-items.json CrescentCompass/Data/loot_catalog.json
    if ($LASTEXITCODE -ne 0) { throw 'Loot audit failed.' }
    Write-Host 'Review catalog differences and update LOOT_CLEANUP.md and the UI snapshot date before releasing.'
} finally { Pop-Location }
