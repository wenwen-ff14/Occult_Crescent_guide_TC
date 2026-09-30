param([string]$SourceDirectory = '.references')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskCommit = 'aa4f6efce52d78c3c3992e88d2bcd7bb218904f2'
foreach ($taskZone in @(@('SouthHorn',1252),@('NorthHorn',1346))) {
    $taskName = $taskZone[0]
    $taskText = Get-Content -LiteralPath (Join-Path $SourceDirectory "$taskName.cs") -Raw
    $taskPrimary = [regex]::Match($taskText, '(?s)GetPotChestData\(\).*?=>.*?new\(\)\s*\{(.*?)\n\s*\};').Groups[1].Value
    $taskBonus = [regex]::Match($taskText, '(?s)GetRerollPotChestData\(\).*?=>\s*\[(.*?)\];').Groups[1].Value
    if (!$taskPrimary -or !$taskBonus) { throw "Cannot find both pot pools in $taskName" }
    $taskRows = [System.Collections.Generic.List[object]]::new()
    $taskPattern = 'new\(new\((-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\),\s*\d+\)'
    $taskPools = @([regex]::Matches($taskPrimary, '(?s)(\d{4}),\s*\[(.*?)\]') | ForEach-Object { @{ fate=[int]$_.Groups[1].Value; bonus=$false; text=$_.Groups[2].Value } })
    $taskPools += @{ fate=0; bonus=$true; text=$taskBonus }
    foreach ($taskPool in $taskPools) {
        foreach ($taskMatch in [regex]::Matches($taskPool.text, $taskPattern)) {
            $taskRows.Add([ordered]@{ id=$taskRows.Count+1; fateId=$taskPool.fate; bonus=$taskPool.bonus
                x=[double]::Parse($taskMatch.Groups[1].Value,[cultureinfo]::InvariantCulture)
                y=[double]::Parse($taskMatch.Groups[2].Value,[cultureinfo]::InvariantCulture)
                z=[double]::Parse($taskMatch.Groups[3].Value,[cultureinfo]::InvariantCulture) })
        }
    }
    $taskExpected = if ($taskName -eq 'SouthHorn') { 80 } else { 83 }
    if ($taskRows.Count -ne $taskExpected) { throw "Expected $taskExpected source pot entries for $taskName, got $($taskRows.Count)" }
    [ordered]@{ schemaVersion=1; territoryId=$taskZone[1]; sourceCommit=$taskCommit
        sourcePath="BOCCHI.Common/Data/Zones/Implementations/$taskName/$taskName.cs"; candidates=$taskRows } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRoot "CrescentCompass/Data/$taskName/pot_candidates.json") -Encoding utf8
    Write-Output "$taskName : $($taskRows.Count) pot candidates"
}
