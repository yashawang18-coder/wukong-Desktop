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
$fullPublishRoot = Join-Path $publishCheckRoot "autonomous-daily-review-full"
$reviewRoot = Join-Path $publishCheckRoot "autonomous-daily-review-v1"
$desktopProject = Join-Path $repoRoot "src\Wukong.Desktop\Wukong.Desktop.csproj"
$officialNuGetSource = "https://api.nuget.org/v3/index.json"

foreach ($path in @($fullPublishRoot, $reviewRoot)) {
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
    --output $fullPublishRoot `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $fullPublishRoot -Force | Copy-Item -Destination $reviewRoot -Recurse -Force

$requiredManifestDirectories = @(
    "WukongAssets\action-batches\WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2",
    "WukongAssets\action-batches\WK-AUTONOMOUS-DAILY-BEHAVIORS-v1",
    "WukongAssets\action-batches\WK-INTERACTION-PRONE-TOUCH-v4-1",
    "WukongAssets\action-mocks\WK-COMMAND-PRODUCTION-CANDIDATES-v4",
    "WukongAssets\action-batches\WK-COMMAND-ACTION-CANDIDATES-v3",
    "WukongAssets\action-batches\WK-MAGIC-SPECIALS-CANDIDATE-v1",
    "WukongAssets\action-batches\WK-INTERACTION-CAR-RIDE-CANDIDATE-v8"
)
$targetBatchRoot = Join-Path $reviewRoot "WukongAssets\action-batches"
foreach ($relativeDirectory in $requiredManifestDirectories) {
    $manifest = Join-Path (Join-Path $reviewRoot $relativeDirectory) "manifest.json"
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        throw "Required runtime asset is missing from review package: $relativeDirectory"
    }
}

$guide = Join-Path $repoRoot "docs\review\AUTONOMOUS_DAILY_REVIEW_GUIDE.md"
Copy-Item -LiteralPath $guide -Destination (Join-Path $reviewRoot "README-REVIEW.md") -Force

$checksumPath = Join-Path $reviewRoot "PACKAGE-SHA256.txt"
$pathSeparator = [System.IO.Path]::DirectorySeparatorChar
$reviewRootPrefix = $reviewRoot.TrimEnd([char[]]@($pathSeparator)) + $pathSeparator
$checksums = Get-ChildItem -LiteralPath $reviewRoot -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        # Windows PowerShell 5.1 runs on .NET Framework, which does not expose
        # System.IO.Path.GetRelativePath. Every enumerated file is below reviewRoot,
        # so a prefix-safe substring keeps the script compatible with powershell.exe.
        if (-not $_.FullName.StartsWith($reviewRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package file is outside the review root: $($_.FullName)"
        }
        $relative = $_.FullName.Substring($reviewRootPrefix.Length).Replace($pathSeparator, [char]'/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
Set-Content -LiteralPath $checksumPath -Value $checksums -Encoding utf8

$frameCount = Get-ChildItem -LiteralPath (Join-Path $targetBatchRoot "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1\frames") -Filter "*.png" -Recurse -File | Measure-Object | Select-Object -ExpandProperty Count
if ($frameCount -ne 59) {
    throw "Expected 59 autonomous daily review frames, found $frameCount"
}

Write-Host "Review package ready: $reviewRoot"
Write-Host "Run: $(Join-Path $reviewRoot 'Wukong.Desktop.exe')"
Write-Host "Included: full current runtime asset library + 6 autonomous daily candidates / 59 frames + prone-touch candidate"
