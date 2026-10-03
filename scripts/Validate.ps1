#Requires -Version 7.0
param([switch]$Preview, [switch]$Package)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    dotnet build CrescentCompass/CrescentCompass.csproj -c Release -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet run --project tests/CrescentCompass.Validation/CrescentCompass.Validation.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core validation failed.' }
    if ($Preview) {
        dotnet run --project tools/UiPreview/UiPreview.csproj -c Release -- artifacts/ui-preview
        if ($LASTEXITCODE -ne 0) { throw 'UI validation failed.' }
        # Refresh documented snapshots; other generated scenarios remain local artifacts.
        $taskSnapshots = @((Get-ChildItem docs/previews -Filter '*.png' -File).Name) + @('cards-navigation.png', 'cards-navigation-scaled.png', 'phantom-jobs.png')
        foreach ($taskName in ($taskSnapshots | Select-Object -Unique)) {
            $taskImage = Join-Path 'artifacts/ui-preview' $taskName
            if (Test-Path -LiteralPath $taskImage) { Copy-Item -LiteralPath $taskImage -Destination (Join-Path 'docs/previews' $taskName) }
        }
    }
    & ./scripts/Verify-PluginRepository.ps1
    if ($Package) { & ./scripts/Package.ps1 }
} finally { Pop-Location }
