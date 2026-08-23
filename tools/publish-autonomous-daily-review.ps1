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

Get-ChildItem -LiteralPath $fullPublishRoot -File | Copy-Item -Destination $reviewRoot -Force
Get-ChildItem -LiteralPath $fullPublishRoot -Directory |
    Where-Object { $_.Name -ne "WukongAssets" } |
    Copy-Item -Destination $reviewRoot -Recurse -Force

$batchNames = @(
    "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2",
    "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1",
    "WK-INTERACTION-PRONE-TOUCH-v4-1"
)
$sourceBatchRoot = Join-Path $fullPublishRoot "WukongAssets\action-batches"
$targetBatchRoot = Join-Path $reviewRoot "WukongAssets\action-batches"
New-Item -ItemType Directory -Path $targetBatchRoot -Force | Out-Null
foreach ($batchName in $batchNames) {
    $source = Join-Path $sourceBatchRoot $batchName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required review batch is missing from publish output: $batchName"
    }
    Copy-Item -LiteralPath $source -Destination $targetBatchRoot -Recurse -Force
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
Write-Host "Included: 6 autonomous daily candidates / 59 frames + prone-touch candidate + approved lifecycle microloops"
