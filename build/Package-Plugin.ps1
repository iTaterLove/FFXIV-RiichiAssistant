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
$packageRoot = Join-Path $artifactsDir "package\$internalName"
$zipPath = Join-Path $artifactsDir "$internalName.zip"
$manifestPath = Join-Path $packageRoot "$internalName.json"

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

if (-not $SkipBuild) {
    & $dotnet publish $pluginProject -c $Configuration -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path $publishDir)) {
    throw "Publish output not found at $publishDir"
}

if (Test-Path $packageRoot) {
    Remove-Item -Recurse -Force $packageRoot
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageRoot -Recurse -Force

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
Write-Output "Created $zipPath"