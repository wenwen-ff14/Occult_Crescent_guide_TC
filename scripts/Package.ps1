param([string]$Version = '', [string]$BuildOutput = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (!$Version) {
    $taskProject = [xml](Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'CrescentCompass/CrescentCompass.csproj'))
    $Version = [string]$taskProject.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a semantic version such as 0.1.0.' }
$taskOutput = if ($BuildOutput) { $BuildOutput } else { Join-Path $taskRoot 'CrescentCompass/bin/Release' }
$taskDist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $taskDist | Out-Null
Add-Type -AssemblyName System.IO.Compression

function Write-TaskArchive([string]$ArchivePath, [System.Collections.IDictionary]$Entries) {
    $taskStream = [System.IO.File]::Open($ArchivePath, [System.IO.FileMode]::Create)
    $taskArchive = [System.IO.Compression.ZipArchive]::new($taskStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskEntry in $Entries.GetEnumerator()) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskEntry.Value, $taskEntry.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $taskArchive.Dispose(); $taskStream.Dispose() }
}

$taskRuntime = [ordered]@{}
foreach ($taskName in @('CrescentCompass.dll','CrescentCompass.Core.dll','CrescentCompass.deps.json','CrescentCompass.json')) {
    $taskFile = Join-Path $taskOutput $taskName
    if (!(Test-Path -LiteralPath $taskFile)) { throw "Build Release before packaging: $taskFile" }
    $taskRuntime[$taskName] = $taskFile
}
$taskManifest = Get-Content -Raw -LiteralPath $taskRuntime['CrescentCompass.json'] | ConvertFrom-Json
$taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskRuntime['CrescentCompass.dll']).Version.ToString()
if ($taskManifest.InternalName -ne 'CrescentCompass' -or $taskManifest.DalamudApiLevel -ne 13 -or
    $taskManifest.AssemblyVersion -ne "$Version.0" -or $taskAssemblyVersion -ne "$Version.0") {
    throw 'Package version, built manifest or assembly version mismatch. Build the requested version first.'
}
foreach ($taskName in @('README.md','LICENSE.txt','third-party/NOTICE.md','third-party/BOCCHI-LICENSE.txt','third-party/BOCCHI-commit.txt','docs/IN_GAME_CHECKS.md','docs/COVERAGE_AUDIT.md','docs/EXPLORATION_AND_SURVEY.md','docs/POT_FATE_TIMERS.md','docs/WALKING_ROUTES.md','docs/previews/wide.png','docs/previews/pot.png','docs/previews/exploration.png','docs/previews/auto-next.png','docs/previews/fate-active.png','docs/previews/walking-route.png','docs/previews/teleport-paused.png','docs/previews/teleport-resumed.png')) {
    $taskRuntime[$taskName] = Join-Path $taskRoot $taskName
}
$taskRuntime['docs/PLAYER_VISIBILITY.md'] = Join-Path $taskRoot 'docs/PLAYER_VISIBILITY.md'
$taskRuntime['docs/audit/ce-map.txt'] = Join-Path $taskRoot 'docs/audit/ce-map.txt'
$taskRuntime['docs/CHEST_CHART.md'] = Join-Path $taskRoot 'docs/CHEST_CHART.md'
$taskRuntime['docs/CE_COOLDOWNS.md'] = Join-Path $taskRoot 'docs/CE_COOLDOWNS.md'
$taskRuntime['docs/previews/ce-cooldowns.png'] = Join-Path $taskRoot 'docs/previews/ce-cooldowns.png'
$taskRuntime['docs/previews/ce-zoomed.png'] = Join-Path $taskRoot 'docs/previews/ce-zoomed.png'
$taskRuntime['docs/previews/pot-overlay-active.png'] = Join-Path $taskRoot 'docs/previews/pot-overlay-active.png'
$taskRuntime['docs/PATROL_OVERLAY.md'] = Join-Path $taskRoot 'docs/PATROL_OVERLAY.md'
$taskRuntime['docs/previews/patrol-overlay-both.png'] = Join-Path $taskRoot 'docs/previews/patrol-overlay-both.png'
$taskRuntime['docs/previews/menu-settings.png'] = Join-Path $taskRoot 'docs/previews/menu-settings.png'
$taskRuntime['docs/previews/menu-ce-open.png'] = Join-Path $taskRoot 'docs/previews/menu-ce-open.png'
$taskRuntime['docs/FATE_AUTO_FLAGS.md'] = Join-Path $taskRoot 'docs/FATE_AUTO_FLAGS.md'
$taskRuntime['docs/WAYMARKS.md'] = Join-Path $taskRoot 'docs/WAYMARKS.md'
$taskRuntime['docs/AUTO_CHESTS.md'] = Join-Path $taskRoot 'docs/AUTO_CHESTS.md'
$taskRuntime['docs/LOOT_CLEANUP.md'] = Join-Path $taskRoot 'docs/LOOT_CLEANUP.md'
$taskRuntime['docs/PLAYER_VISIBILITY.md'] = Join-Path $taskRoot 'docs/PLAYER_VISIBILITY.md'
$taskRuntime['docs/PHANTOM_JOBS.md'] = Join-Path $taskRoot 'docs/PHANTOM_JOBS.md'
$taskRuntime['docs/audit/phantom-macro-icons.txt'] = Join-Path $taskRoot 'docs/audit/phantom-macro-icons.txt'
$taskRuntime['docs/previews/phantom-jobs.png'] = Join-Path $taskRoot 'docs/previews/phantom-jobs.png'
$taskRuntime['docs/WAYMARK_PREVIEW.md'] = Join-Path $taskRoot 'docs/WAYMARK_PREVIEW.md'
foreach ($taskNumber in 1..4) {
    $taskName = "docs/previews/waymarks-preview-$taskNumber.png"
    $taskRuntime[$taskName] = Join-Path $taskRoot $taskName
}
$taskRuntime['docs/previews/auto-chests-settings.png'] = Join-Path $taskRoot 'docs/previews/auto-chests-settings.png'
$taskRuntime['docs/previews/waymarks.png'] = Join-Path $taskRoot 'docs/previews/waymarks.png'
$taskRuntime['docs/previews/waymarks-import.png'] = Join-Path $taskRoot 'docs/previews/waymarks-import.png'
$taskRuntime['docs/previews/waymarks-distance.png'] = Join-Path $taskRoot 'docs/previews/waymarks-distance.png'
$taskRuntime['docs/previews/general-fates.png'] = Join-Path $taskRoot 'docs/previews/general-fates.png'
$taskRuntime['docs/audit/ce-trigger-names.txt'] = Join-Path $taskRoot 'docs/audit/ce-trigger-names.txt'
$taskRuntime['docs/audit/chest-chart-mapping.json'] = Join-Path $taskRoot 'docs/audit/chest-chart-mapping.json'
$taskRuntime['docs/audit/pot-hint-templates.txt'] = Join-Path $taskRoot 'docs/audit/pot-hint-templates.txt'
$taskRuntime['docs/previews/chart-route.png'] = Join-Path $taskRoot 'docs/previews/chart-route.png'
$taskRuntime['docs/previews/chart-settings.png'] = Join-Path $taskRoot 'docs/previews/chart-settings.png'
$taskRuntime['docs/previews/chart-zoomed.png'] = Join-Path $taskRoot 'docs/previews/chart-zoomed.png'
$taskRuntime['docs/previews/player-visibility.png'] = Join-Path $taskRoot 'docs/previews/player-visibility.png'
foreach ($taskName in @('USER_GUIDE.md','BUILDING.md','CHANGELOG.md','docs/README.md','docs/previews/cards-navigation.png','docs/previews/cards-navigation-scaled.png')) {
    $taskRuntime[$taskName] = Join-Path $taskRoot $taskName
}
foreach ($taskFile in $taskRuntime.Values) {
    if (!(Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw "Missing package input: $taskFile" }
}
Write-TaskArchive (Join-Path $taskDist "CrescentCompass-$Version-api13.zip") $taskRuntime

$taskSources = [ordered]@{}
foreach ($taskDirectory in @('CrescentCompass','CrescentCompass.Core','tests','tools','scripts','docs','third-party')) {
    Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskDirectory) -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { $taskSources[[System.IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\','/')] = $_.FullName }
}
foreach ($taskName in @('README.md','USER_GUIDE.md','BUILDING.md','CHANGELOG.md','LICENSE.txt','global.json','.gitignore','.gitattributes')) { $taskSources[$taskName] = Join-Path $taskRoot $taskName }
if (Test-Path -LiteralPath (Join-Path $taskRoot 'pluginmaster.json')) { $taskSources['pluginmaster.json'] = Join-Path $taskRoot 'pluginmaster.json' }
Write-TaskArchive (Join-Path $taskDist "CrescentCompass-$Version-source.zip") $taskSources
Get-ChildItem -LiteralPath $taskDist -Filter "CrescentCompass-$Version-*.zip" | Get-FileHash -Algorithm SHA256 | Format-Table -AutoSize
