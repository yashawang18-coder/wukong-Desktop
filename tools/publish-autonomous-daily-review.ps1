[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishCheckRoot = Join-Path $repoRoot ".publish-check"
$reviewRoot = Join-Path $publishCheckRoot "lifecycle-v3r1-v4-review"
$desktopProject = Join-Path $repoRoot "src\Wukong.Desktop\Wukong.Desktop.csproj"
$officialNuGetSource = "https://api.nuget.org/v3/index.json"

foreach ($path in @($reviewRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

dotnet publish $desktopProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --source $officialNuGetSource `
    --output $reviewRoot `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$requiredManifestDirectories = @(
    "WukongAssets\action-batches\WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2",
    "WukongAssets\action-batches\WK-AUTONOMOUS-DAILY-BEHAVIORS-v1",
    "WukongAssets\action-batches\WK-INTERACTION-PRONE-TOUCH-v4-1",
    "WukongAssets\action-batches\WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED",
    "WukongAssets\action-batches\WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4",
    "WukongAssets\action-mocks\WK-COMMAND-PRODUCTION-CANDIDATES-v4",
    "WukongAssets\action-batches\WK-COMMAND-ACTION-CANDIDATES-v3",
    "WukongAssets\action-batches\WK-MAGIC-SPECIALS-CANDIDATE-v1",
    "WukongAssets\action-batches\WK-INTERACTION-CAR-RIDE-CANDIDATE-v8"
)
$targetBatchRoot = Join-Path $reviewRoot "WukongAssets\action-batches"

# The project copies runtime PNG/JSON/GIF content. Copy both candidate source
# packages in full so the review build also carries README, QA, and SHA evidence.
foreach ($batch in @(
    "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED",
    "WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4"
)) {
    $source = Join-Path (Join-Path $repoRoot "assets\action-batches") $batch
    $destination = Join-Path $targetBatchRoot $batch
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Candidate source package is missing: $batch"
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force |
        Copy-Item -Destination $destination -Recurse -Force
}

foreach ($relativeDirectory in $requiredManifestDirectories) {
    $manifest = Join-Path (Join-Path $reviewRoot $relativeDirectory) "manifest.json"
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        throw "Required runtime asset is missing from review package: $relativeDirectory"
    }
}

foreach ($batch in @(
    "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED",
    "WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4"
)) {
    $candidateRoot = Join-Path $targetBatchRoot $batch
    foreach ($fileName in @("asset.json", "manifest.json", "runtime-review-manifest.json", "SHA256SUMS")) {
        if (-not (Test-Path -LiteralPath (Join-Path $candidateRoot $fileName) -PathType Leaf)) {
            throw "Candidate review evidence is missing: $batch/$fileName"
        }
    }
}

$guide = Join-Path $repoRoot "docs\review\LIFECYCLE_V3R1_PRONE_FRONT_V4_REVIEW_GUIDE.md"
Copy-Item -LiteralPath $guide -Destination (Join-Path $reviewRoot "README-REVIEW.md") -Force

$checksumPath = Join-Path $reviewRoot "PACKAGE-SHA256.txt"
$pathSeparator = [System.IO.Path]::DirectorySeparatorChar
$reviewRootPrefix = $reviewRoot.TrimEnd([char[]]@($pathSeparator)) + $pathSeparator
$checksums = Get-ChildItem -LiteralPath $reviewRoot -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        # Windows PowerShell 5.1 runs on .NET Framework, which does not expose
        # the newer relative-path helper. Every enumerated file is below reviewRoot,
        # so a prefix-safe substring keeps the script compatible with powershell.exe.
        if (-not $_.FullName.StartsWith($reviewRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package file is outside the review root: $($_.FullName)"
        }
        $relative = $_.FullName.Substring($reviewRootPrefix.Length).Replace($pathSeparator, [char]'/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
Set-Content -LiteralPath $checksumPath -Value $checksums -Encoding utf8

$autonomousBatchRoot = Join-Path $targetBatchRoot "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1"
$duplicateFrameCount = Get-ChildItem -LiteralPath $autonomousBatchRoot -Filter "*.png" -Recurse -File | Measure-Object | Select-Object -ExpandProperty Count
if ($duplicateFrameCount -ne 0) {
    throw "Autonomous daily review must reference source frames; found $duplicateFrameCount duplicate PNGs"
}

Write-Host "Review package ready: $reviewRoot"
Write-Host "Run: $(Join-Path $reviewRoot 'Wukong.Desktop.exe')"
Write-Host "Included: full current runtime library + V3R1 recovered lifecycle review + independent V4 forward-prone review"
