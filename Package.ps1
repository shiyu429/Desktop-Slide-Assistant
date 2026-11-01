param([string]$OutputDirectory = 'releases\1.3.3\portable', [switch]$SourceOnly)
$ErrorActionPreference = 'Stop'
$releaseVersion = '1.3.3'
$projectPrefix = $PSScriptRoot.TrimEnd('\')+'\'
function Project-Path([string]$relative) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (!$path.StartsWith($projectPrefix,[System.StringComparison]::OrdinalIgnoreCase)) { throw "路径超出项目：$relative" }
    return $path
}
$packageRoot = Project-Path $OutputDirectory
$releaseRoot = Project-Path "releases\$releaseVersion"
$qaRoot = Project-Path 'build\qa'
New-Item -ItemType Directory -Path $releaseRoot,$qaRoot -Force | Out-Null
$exePath = Join-Path $packageRoot 'DesktopSwitcher.exe'
$portableZip = Join-Path $releaseRoot "DesktopSwitcher-portable-v$releaseVersion.zip"
$sourceZip = Join-Path $releaseRoot "DesktopSwitcher-source-v$releaseVersion.zip"
$docs = @('使用说明.md','更新说明.md','验证记录.md')
if (!$SourceOnly) {
    if (!(Test-Path -LiteralPath $exePath)) { throw '交付程序不存在，未生成便携包。可用 -SourceOnly 仅交付源码。' }
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
    if ($version -ne "$releaseVersion.0") { throw "交付版本不符：$version" }
    foreach ($doc in $docs) { Copy-Item -LiteralPath (Project-Path "docs\$doc") -Destination $packageRoot }
    $buildInfo = Get-Content -LiteralPath (Join-Path $packageRoot 'build-info.json') -Raw | ConvertFrom-Json
    $fileHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
    if ($buildInfo.Version -ne $version -or $buildInfo.Executable.SHA256 -ne $fileHash) { throw '构建清单与交付程序不符。' }
    foreach ($record in $buildInfo.Sources) {
        if ($record.SHA256 -ne (Get-FileHash -LiteralPath (Project-Path $record.Name) -Algorithm SHA256).Hash) { throw "源码已在构建后改变：$($record.Name)" }
    }
    $portablePaths = @('DesktopSwitcher.exe','DesktopSwitcher.exe.config','build-info.json') + $docs |
        ForEach-Object { Join-Path $packageRoot $_ }
    Compress-Archive -LiteralPath $portablePaths -DestinationPath $portableZip -Force
}
# Preserve source directory structure so extracting the archive gives a rebuildable project.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceNames = @('README.md','README.en.md','.gitignore','Build.ps1','Package.ps1') +
    @('AssemblyInfo.cs','Program.cs','Lifetime.cs','Native.cs','DesktopState.cs','Settings.cs','CoursewareMode.cs','Docking.cs','DockMotion.cs','MenuDismissal.cs','FirstUseDialog.cs','SpotlightGuide.cs','WidgetWindow.cs','app.manifest','DesktopSwitcher.exe.config','DesktopSwitcher.ico' | ForEach-Object { "src/$_" }) +
    @('tests/Tests.cs','tests/WidgetFeatureTests.cs','tests/LifetimeTests.cs','tests/FullProductCheck.ps1','tests/fixtures/courseware-test.pptx','tests/fixtures/make_fixture.py') +
    @($docs | ForEach-Object { "docs/$_" }) +
    @('docs/index.html','docs/style.css','docs/app.js','docs/.nojekyll','docs/assets/overview.svg','tests/WebsiteCheck.mjs')
$sourceStream = [System.IO.File]::Create($sourceZip)
$sourceArchive = [System.IO.Compression.ZipArchive]::new($sourceStream,[System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $sourceNames) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive,(Project-Path $name),$name) | Out-Null
    }
} finally { $sourceArchive.Dispose(); $sourceStream.Dispose() }
if ($SourceOnly) {
    @("Source version=$releaseVersion",'Portable package=not generated',"Source ZIP SHA256=$((Get-FileHash -LiteralPath $sourceZip).Hash)") |
        Set-Content -LiteralPath (Join-Path $qaRoot "package-checks-v$releaseVersion-source.txt") -Encoding UTF8
    Write-Output "$releaseVersion 源码包已生成。"
    return
}
$zip = [System.IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    $entry = $zip.GetEntry('DesktopSwitcher.exe')
    if ($null -eq $entry -or $entry.Length -lt 10000) { throw '便携包缺少有效程序。' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = $entry.Open()
    try { $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose(); $sha.Dispose() }
    if ($archiveHash -ne $fileHash) { throw '压缩包程序校验失败。' }
} finally { $zip.Dispose() }
@("Version=$version","DesktopSwitcher.exe SHA256=$fileHash","Portable ZIP SHA256=$((Get-FileHash -LiteralPath $portableZip).Hash)","Source ZIP SHA256=$((Get-FileHash -LiteralPath $sourceZip).Hash)") |
    Set-Content -LiteralPath (Join-Path $qaRoot "package-checks-v$releaseVersion.txt") -Encoding UTF8
Write-Output "$releaseVersion 便携包与源码包已生成，版本、源码及压缩包程序校验一致。"
