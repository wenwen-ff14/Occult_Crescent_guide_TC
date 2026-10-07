#Requires -Version 7.0
param([string]$Version = '', [string]$BuildOutput = '')

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (!$Version) {
    $taskProject = [xml](Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'CrescentCompass/CrescentCompass.csproj'))
    $Version = [string]$taskProject.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project version.' }
$taskOutput = if ($BuildOutput) { $BuildOutput } else { Join-Path $taskRoot 'CrescentCompass/bin/Release' }
$taskRuntimePath = Join-Path $taskRoot "dist/CrescentCompass-$Version-api13.zip"
$taskSourcePath = Join-Path $taskRoot "dist/CrescentCompass-$Version-source.zip"
$taskExpected = [ordered]@{}
foreach ($taskName in @('CrescentCompass.dll','CrescentCompass.Core.dll','CrescentCompass.deps.json','CrescentCompass.json','images/icon.png')) {
    $taskExpected[$taskName] = Join-Path $taskOutput $taskName
}
foreach ($taskName in @('LICENSE.txt','third-party/NOTICE.md','third-party/BOCCHI-LICENSE.txt','third-party/BOCCHI-commit.txt')) {
    $taskExpected[$taskName] = Join-Path $taskRoot $taskName
}
$taskExpected['README.md'] = Join-Path $taskRoot 'docs/INSTALL_PACKAGE.md'

function Test-TaskEntry([IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Source) {
    $taskEntry = $Archive.GetEntry($Name)
    if ($null -eq $taskEntry) { throw "Missing package entry: $Name" }
    $taskStream = $taskEntry.Open()
    try { $taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream)) }
    finally { $taskStream.Dispose() }
    if ($taskHash -ne (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash) { throw "Package content differs from source: $Name" }
}

$taskZip = [IO.Compression.ZipFile]::OpenRead($taskRuntimePath)
try {
    if ($taskZip.Entries.Count -ne $taskExpected.Count) { throw 'Runtime ZIP must contain only the ten installation files.' }
    $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($taskEntry in $taskZip.Entries) {
        if ($taskEntry.FullName -cnotin @($taskExpected.Keys) -or !$taskSeen.Add($taskEntry.FullName) -or $taskEntry.Length -eq 0) {
            throw "Unexpected, duplicate or empty runtime entry: $($taskEntry.FullName)"
        }
        Test-TaskEntry $taskZip $taskEntry.FullName $taskExpected[$taskEntry.FullName]
    }
    $taskReader = [IO.StreamReader]::new($taskZip.GetEntry('CrescentCompass.json').Open())
    try { $taskManifest = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
    $taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskExpected['CrescentCompass.dll']).Version.ToString()
    if ($taskManifest.InternalName -ne 'CrescentCompass' -or $taskManifest.DalamudApiLevel -ne 13 -or
        $taskManifest.AssemblyVersion -ne "$Version.0" -or $taskAssemblyVersion -ne "$Version.0") {
        throw 'Runtime manifest, assembly or expected version mismatch.'
    }
} finally { $taskZip.Dispose() }

# All local documentation and previews must remain available in the separate source archive.
$taskDocs = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -File -Recurse)
$taskSources = [IO.Compression.ZipFile]::OpenRead($taskSourcePath)
try {
    foreach ($taskDoc in $taskDocs) {
        $taskRelative = [IO.Path]::GetRelativePath($taskRoot, $taskDoc.FullName).Replace('\','/')
        Test-TaskEntry $taskSources $taskRelative $taskDoc.FullName
    }
    foreach ($taskName in @('README.md','USER_GUIDE.md','BUILDING.md','CHANGELOG.md','LICENSE.txt','CrescentCompass/CrescentCompass.csproj')) {
        Test-TaskEntry $taskSources $taskName (Join-Path $taskRoot $taskName)
    }
} finally { $taskSources.Dispose() }
Write-Host "Validated slim runtime: $($taskExpected.Count) files, $((Get-Item -LiteralPath $taskRuntimePath).Length) bytes; $($taskDocs.Count) documentation files retained in source ZIP."
