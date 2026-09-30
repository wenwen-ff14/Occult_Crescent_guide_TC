#Requires -Version 7.0
param([switch]$Online)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskRawBase = 'https://raw.githubusercontent.com/wenwen-ff14/Occult_Crescent_guide_TC/main'
$taskJson = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'pluginmaster.json')
if ($taskJson.TrimStart()[0] -ne '[') { throw 'Dalamud repository must be a JSON array.' }
$taskEntries = @($taskJson | ConvertFrom-Json)
if ($taskEntries.Count -ne 1) { throw 'Expected one CrescentCompass repository entry.' }
$taskEntry = $taskEntries[0]
if ($taskEntry.InternalName -ne 'CrescentCompass' -or $taskEntry.DalamudApiLevel -ne 13 -or
    $taskEntry.AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.0$' -or
    $taskEntry.IsHide -ne $false -or $taskEntry.IsTestingExclusive -ne $false) {
    throw 'Invalid plugin repository identity, API level or visibility.'
}
$taskPackageRelative = "packages/CrescentCompass/$($taskEntry.AssemblyVersion).zip"
$taskPackage = Join-Path $taskRoot $taskPackageRelative
foreach ($taskKey in @('DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting')) {
    if ($taskEntry.$taskKey -ne "$taskRawBase/$taskPackageRelative") { throw "Invalid $taskKey." }
}
$taskZip = [IO.Compression.ZipFile]::OpenRead($taskPackage)
try {
    foreach ($taskName in @('CrescentCompass.dll','CrescentCompass.Core.dll','CrescentCompass.deps.json','CrescentCompass.json','LICENSE.txt','third-party/NOTICE.md')) {
        if ($null -eq $taskZip.GetEntry($taskName)) { throw "Missing root-relative package entry: $taskName" }
    }
    foreach ($taskFile in $taskZip.Entries) {
        if ($taskFile.FullName -match '(^|/)(\.sdk|\.references|\.git)/|\.pdb$' -or
            ($taskFile.FullName -match '\.dll$' -and $taskFile.FullName -notin @('CrescentCompass.dll','CrescentCompass.Core.dll'))) {
            throw "Unexpected development file in runtime archive: $($taskFile.FullName)"
        }
    }
    $taskReader = [IO.StreamReader]::new($taskZip.GetEntry('CrescentCompass.json').Open())
    try { $taskPacked = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
    if ($taskPacked.InternalName -ne $taskEntry.InternalName -or
        $taskPacked.AssemblyVersion -ne $taskEntry.AssemblyVersion -or
        $taskPacked.DalamudApiLevel -ne $taskEntry.DalamudApiLevel -or
        $taskPacked.RepoUrl -ne $taskEntry.RepoUrl) { throw 'Packaged manifest differs from repository entry.' }
} finally { $taskZip.Dispose() }

if ($Online) {
    $taskResponse = Invoke-WebRequest -Uri "$taskRawBase/pluginmaster.json" -Headers @{ 'Cache-Control' = 'no-cache' }
    $taskRemoteJson = [string]$taskResponse.Content
    if ($taskRemoteJson.TrimStart()[0] -ne '[') { throw 'Public URL did not return a repository JSON array.' }
    $taskRemote = @($taskRemoteJson | ConvertFrom-Json)
    if ($taskRemote.Count -ne 1 -or $taskRemote[0].AssemblyVersion -ne $taskEntry.AssemblyVersion -or
        $taskRemote[0].DownloadLinkInstall -ne $taskEntry.DownloadLinkInstall -or
        $taskRemote[0].DownloadLinkUpdate -ne $taskEntry.DownloadLinkUpdate -or
        $taskRemote[0].DalamudApiLevel -ne 13) { throw 'Public repository does not match local release.' }
    $taskHttp = [Net.Http.HttpClient]::new()
    try {
        $taskBytes = $taskHttp.GetByteArrayAsync($taskRemote[0].DownloadLinkInstall).GetAwaiter().GetResult()
        $taskRemoteHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskBytes))
        $taskLocalHash = (Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash
        if ($taskRemoteHash -ne $taskLocalHash) { throw 'Public ZIP hash differs from the validated local package.' }
        Write-Host "Public JSON and ZIP accessible without authentication; SHA256 $taskRemoteHash"
    } finally { $taskHttp.Dispose() }
}
Write-Host "Validated CrescentCompass $($taskEntry.AssemblyVersion), Dalamud API 13, installation/update links and runtime ZIP."
