param([string]$Snapshot = '.references/local-1252-treasures.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskScene = Get-Content -LiteralPath $Snapshot -Raw | ConvertFrom-Json
$taskRows = (Get-Content -LiteralPath (Join-Path $taskRoot 'CrescentCompass/Data/SouthHorn/treasure_locations.json') -Raw | ConvertFrom-Json).treasures
$taskField = @($taskScene.treasures | Where-Object layer -eq 'Field_Treasure')
$taskMatches = [System.Collections.Generic.HashSet[int]]::new()
$taskLargest = 0.0
foreach ($taskPoint in $taskField) {
    $taskNear = @($taskRows | Where-Object {
        $taskDistance = [math]::Sqrt([math]::Pow($_.x-$taskPoint.x,2)+[math]::Pow($_.y-$taskPoint.y,2)+[math]::Pow($_.z-$taskPoint.z,2))
        $taskDistance -le 0.01
    })
    if ($taskNear.Count -ne 1 -or !$taskMatches.Add([int]$taskNear[0].id)) { throw 'Field treasure mismatch or non-bijective match.' }
    $taskMatch = $taskNear[0]
    $taskLargest = [math]::Max($taskLargest,[math]::Sqrt([math]::Pow($taskMatch.x-$taskPoint.x,2)+[math]::Pow($taskMatch.y-$taskPoint.y,2)+[math]::Pow($taskMatch.z-$taskPoint.z,2)))
}
if ($taskField.Count -ne 68 -or $taskRows.Count -ne 68 -or $taskMatches.Count -ne 68) { throw 'Unexpected field treasure count.' }
$taskTower = @($taskScene.treasures | Where-Object layer -eq 'BA_treasure')
if ($taskTower.Count -ne 14) { throw 'Unexpected tower reward count.' }
$taskTowerRows = @($taskTower | ForEach-Object { [ordered]@{ id=$_.instanceId; dataId=0; category='tower'; x=$_.x; y=$_.y; z=$_.z } })
if (@($taskTowerRows.id | Sort-Object -Unique).Count -ne 14) { throw 'Scene instance IDs required for tower snapshot.' }
if (!$taskScene.clientVersion) { throw 'Client version required for provenance.' }
[ordered]@{ schemaVersion=1; territoryId=1252; source="TC $($taskScene.clientVersion) planmap.lgb BA_treasure"; treasures=$taskTowerRows } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRoot 'CrescentCompass/Data/SouthHorn/tower_locations.json') -Encoding utf8
Write-Output "PASS: Field_Treasure 68/68, one-to-one position match, maximum delta=$taskLargest world units; BA_treasure 14 separate placements. Scene BaseId is unavailable in this Lumina reader; ID/model checks are a separate Excel-sheet audit."
