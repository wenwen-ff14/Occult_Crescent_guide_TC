#Requires -Version 7.0
param([string]$Changelog = '')

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot 'CrescentCompass/CrescentCompass.csproj'
$taskProjectXml = [xml](Get-Content -Raw -LiteralPath $taskProject)
$taskVersion = [string]$taskProjectXml.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project version.' }
$taskAssemblyVersion = "$taskVersion.0"
$taskPackageRelative = "packages/CrescentCompass/$taskAssemblyVersion.zip"
$taskPackagePath = Join-Path $taskRoot $taskPackageRelative
if (Test-Path -LiteralPath $taskPackagePath) {
    throw "Version $taskAssemblyVersion is already packaged. Increase the project version before publishing an update."
}

# Keep the running development plugin's files untouched while preparing a release.
$taskBuildOutput = Join-Path $taskRoot "CrescentCompass/bin/Repository/$taskVersion-$([Guid]::NewGuid().ToString('N'))/"
& dotnet build $taskProject -c Release -p:RestoreLockedMode=true "-p:OutputPath=$taskBuildOutput"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
$taskManifest = Get-Content -Raw -LiteralPath (Join-Path $taskBuildOutput 'CrescentCompass.json') | ConvertFrom-Json -AsHashtable
$taskBuiltVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $taskBuildOutput 'CrescentCompass.dll')).Version.ToString()
if ($taskManifest.InternalName -ne 'CrescentCompass' -or $taskManifest.DalamudApiLevel -ne 13 -or
    $taskManifest.AssemblyVersion -ne $taskAssemblyVersion -or $taskBuiltVersion -ne $taskAssemblyVersion) {
    throw 'Manifest, assembly version or Dalamud API level mismatch.'
}

$taskRepoUrl = 'https://github.com/wenwen-ff14/Occult_Crescent_guide_TC'
$taskRawBase = 'https://raw.githubusercontent.com/wenwen-ff14/Occult_Crescent_guide_TC/main'
$taskManifest.RepoUrl = $taskRepoUrl
$taskManifest.DownloadLinkInstall = "$taskRawBase/$taskPackageRelative"
$taskManifest.DownloadLinkUpdate = $taskManifest.DownloadLinkInstall
$taskManifest.DownloadLinkTesting = $taskManifest.DownloadLinkInstall
$taskManifest.IsHide = $false
$taskManifest.IsTestingExclusive = $false
$taskManifest.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$taskManifest.ImageUrls = @("$taskRawBase/docs/previews/chart-route.png", "$taskRawBase/docs/previews/ce-cooldowns.png", "$taskRawBase/docs/previews/pot.png")
if ($Changelog) { $taskManifest.Changelog = $Changelog }
$taskRepositoryJson = ConvertTo-Json -InputObject @($taskManifest) -Depth 10
[IO.File]::WriteAllText((Join-Path $taskRoot 'pluginmaster.json'), $taskRepositoryJson + "`n", [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'Package.ps1') -Version $taskVersion -BuildOutput $taskBuildOutput
$taskRuntimeZip = Join-Path $taskRoot "dist/CrescentCompass-$taskVersion-api13.zip"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskPackagePath) | Out-Null
Copy-Item -LiteralPath $taskRuntimeZip -Destination $taskPackagePath
& (Join-Path $PSScriptRoot 'Verify-PluginRepository.ps1')
Write-Host "Prepared $taskPackageRelative and pluginmaster.json. Commit and push both with the matching source."
