param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $repoRoot "src\FFXIV.RiichiAssistant.Plugin\FFXIV.RiichiAssistant.Plugin.csproj"
$repoJsonPath = Join-Path $repoRoot "repo.json"
$artifactsDir = Join-Path $repoRoot "artifacts"
$localDotnet = Join-Path $HOME ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }

if (-not (Test-Path $repoJsonPath)) {
    throw "repo.json not found at $repoJsonPath"
}

$repoEntry = Get-Content $repoJsonPath | ConvertFrom-Json | Select-Object -First 1
$internalName = [string]$repoEntry.InternalName
$resolvedVersion = if ([string]::IsNullOrWhiteSpace($Version)) { [string]$repoEntry.AssemblyVersion } else { $Version }

if ([string]::IsNullOrWhiteSpace($internalName)) {
    throw "repo.json is missing InternalName"
}

if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
    throw "No plugin version was supplied and repo.json is missing AssemblyVersion"
}

$publishDir = Join-Path $artifactsDir "publish\$internalName"
$stagingPublishDir = Join-Path $artifactsDir "staging\$internalName"
$packageRoot = Join-Path $artifactsDir "package\$internalName"
$zipPath = Join-Path $artifactsDir "$internalName.zip"
$manifestPath = Join-Path $packageRoot "$internalName.json"
$sourcePublishDir = $publishDir

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

if (-not $SkipBuild) {
    if (Test-Path $stagingPublishDir) {
        Remove-Item -Recurse -Force $stagingPublishDir
    }

    & $dotnet publish $pluginProject -c $Configuration -o $stagingPublishDir /p:Version=$resolvedVersion /p:AssemblyVersion=$resolvedVersion /p:FileVersion=$resolvedVersion
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $sourcePublishDir = $stagingPublishDir
}

if (-not (Test-Path $sourcePublishDir)) {
    throw "Publish output not found at $sourcePublishDir"
}

if (Test-Path $packageRoot) {
    Remove-Item -Recurse -Force $packageRoot
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -Path (Join-Path $sourcePublishDir "*") -Destination $packageRoot -Recurse -Force

$manifest = [ordered]@{
    Author = [string]$repoEntry.Author
    Name = [string]$repoEntry.Name
    InternalName = $internalName
    AssemblyVersion = $resolvedVersion
    Description = [string]$repoEntry.Description
    DalamudApiLevel = $repoEntry.DalamudApiLevel
    Punchline = [string]$repoEntry.Punchline
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath

if (-not $SkipBuild) {
    try {
        if (Test-Path $publishDir) {
            Remove-Item -Recurse -Force $publishDir
        }

        Copy-Item -Path $stagingPublishDir -Destination $publishDir -Recurse -Force
    }
    catch {
        Write-Warning "Publish cache at $publishDir could not be refreshed: $($_.Exception.Message)"
    }
}

Write-Output "Created $zipPath"