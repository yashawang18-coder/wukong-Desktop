param(
    [string]$Version = '0.1.0-preview.20261009',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$safeVersion = $Version -replace '[^0-9A-Za-z._-]', '-'
$publish = Join-Path $repo ".publish-check\wukong-desktop-$safeVersion-$Runtime"
$installerOutput = Join-Path $repo '.publish-check\installers'
$archive = Join-Path $installerOutput "Wukong-Desktop-$safeVersion-$Runtime-portable.zip"
$project = Join-Path $repo 'src\Wukong.Desktop\Wukong.Desktop.csproj'
$script = Join-Path $repo 'installer\Wukong.Desktop.iss'

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish,$installerOutput -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publish `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -m:1 `
    -nr:false `
    -v:minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$required = @(
    'Wukong.Desktop.exe',
    'Wukong.Desktop.dll',
    'WukongAssets',
    'WukongDefaults'
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing publish item: $name" }
}

$forbidden = @('.git','.asset-staging','tests','reference','WukongData')
foreach ($name in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $publish $name)) { throw "Forbidden publish item: $name" }
}
Get-ChildItem -LiteralPath $publish -Filter '*.enabled' -File | ForEach-Object {
    throw "Local candidate marker must not enter release: $($_.Name)"
}

$sourceSha = (git -C $repo rev-parse HEAD).Trim()
$releaseManifest = [ordered]@{
    product = 'Wukong Desktop'
    version = $Version
    runtime = $Runtime
    source_commit = $sourceSha
    self_contained = $true
    signed = $false
    user_data_policy = 'WukongData is created on first launch and preserved by uninstall.'
    generated_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
}
$releaseManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $publish 'RELEASE-MANIFEST.json') -Encoding UTF8

$hashLines = Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($publish.Length + 1).Replace('\','/')
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$hashLines | Set-Content -LiteralPath (Join-Path $publish 'SHA256SUMS.txt') -Encoding ASCII

if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $known = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $known | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Inno Setup 6 compiler was not found. Install it or pass -InnoCompiler.'
}

& $InnoCompiler "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$installerOutput" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }

$setup = Join-Path $installerOutput "Wukong-Desktop-Setup-$safeVersion-$Runtime.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer was not created: $setup" }

[pscustomobject]@{
    Version = $Version
    SourceCommit = $sourceSha
    PublishDirectory = $publish
    PortableArchive = $archive
    PortableArchiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Installer = $setup
    InstallerSha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json -Depth 4
