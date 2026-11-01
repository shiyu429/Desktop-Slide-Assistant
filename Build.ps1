param([string]$OutputDirectory = 'build\app')
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot 'src'
$docsRoot = Join-Path $PSScriptRoot 'docs'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$compilerSignature = Get-AuthenticodeSignature -LiteralPath $compilerPath
if ($compilerSignature.Status -ne 'Valid' -or $compilerSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
  throw '系统编译器的微软数字签名未通过核验，构建已停止。'
}
$frameworkPath = Split-Path -Parent $compilerPath
$wpfPath = Join-Path $frameworkPath 'WPF'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory))
if (!$outputPath.StartsWith($PSScriptRoot.TrimEnd('\')+'\',[System.StringComparison]::OrdinalIgnoreCase)) { throw '输出目录必须位于本项目内。' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

# Generate the transparent vector-style application icon without external packages.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$shape = New-Object System.Drawing.Drawing2D.GraphicsPath
$shape.AddArc(1,1,26,26,180,90)
$shape.AddArc(37,1,26,26,270,90)
$shape.AddArc(37,37,26,26,0,90)
$shape.AddArc(1,37,26,26,90,90)
$shape.CloseFigure()
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235,43,48,56))
$graphics.FillPath($brush,$shape)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(244,246,250)),3
$pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLines($pen,[System.Drawing.Point[]]@([System.Drawing.Point]::new(25,22),[System.Drawing.Point]::new(15,32),[System.Drawing.Point]::new(25,42)))
$graphics.DrawLines($pen,[System.Drawing.Point[]]@([System.Drawing.Point]::new(39,22),[System.Drawing.Point]::new(49,32),[System.Drawing.Point]::new(39,42)))
$pngStream = New-Object System.IO.MemoryStream
$bitmap.Save($pngStream,[System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$iconPath = Join-Path $sourceRoot 'DesktopSwitcher.ico'
$iconStream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $iconStream
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([Byte]64); $writer.Write([Byte]64); $writer.Write([Byte]0); $writer.Write([Byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$pngBytes.Length); $writer.Write([UInt32]22)
$writer.Write($pngBytes)
$writer.Dispose(); $pngStream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $brush.Dispose(); $shape.Dispose()

$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') |
  ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') |
  ForEach-Object { '/reference:' + (Join-Path $wpfPath $_) }
$sourcePaths = @('AssemblyInfo.cs','Program.cs','Lifetime.cs','Native.cs','DesktopState.cs','Settings.cs','CoursewareMode.cs','Docking.cs','DockMotion.cs','MenuDismissal.cs','FirstUseDialog.cs','SpotlightGuide.cs','WidgetWindow.cs') |
  ForEach-Object { Join-Path $sourceRoot $_ }
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$outputPath\DesktopSwitcher.exe" "/win32manifest:$sourceRoot\app.manifest" "/win32icon:$iconPath" @references @sourcePaths
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'DesktopSwitcher.exe.config') -Destination $outputPath
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'tests\Tests.cs')) {
  $testPath = Join-Path $PSScriptRoot 'build\qa'
  New-Item -ItemType Directory -Path $testPath -Force | Out-Null
  $testSources = @('Tests.cs','WidgetFeatureTests.cs','LifetimeTests.cs' | ForEach-Object { Join-Path $PSScriptRoot "tests\$_" }) + @('Lifetime.cs','Native.cs','DesktopState.cs','Settings.cs','CoursewareMode.cs','Docking.cs','DockMotion.cs','MenuDismissal.cs','FirstUseDialog.cs','SpotlightGuide.cs','WidgetWindow.cs' |
    ForEach-Object { Join-Path $sourceRoot $_ })
  & $compilerPath /nologo /target:exe /platform:anycpu /optimize+ /utf8output "/out:$testPath\Tests.exe" "/win32manifest:$sourceRoot\app.manifest" @references @testSources
  if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败' }
}
foreach ($doc in @('使用说明.md','更新说明.md','验证记录.md')) {
  if (Test-Path -LiteralPath (Join-Path $docsRoot $doc)) {
    Copy-Item -LiteralPath (Join-Path $docsRoot $doc) -Destination $outputPath
  }
}
$builtExe = Join-Path $outputPath 'DesktopSwitcher.exe'
if (!(Test-Path -LiteralPath $builtExe)) { throw '新 EXE 已不可用，可能被安全软件处理；不自动重复构建。' }
$sourceRecords = @($sourcePaths + @('Build.ps1','Package.ps1','tests\Tests.cs','tests\WidgetFeatureTests.cs','tests\LifetimeTests.cs','src\app.manifest','src\DesktopSwitcher.exe.config','src\DesktopSwitcher.ico' | ForEach-Object { Join-Path $PSScriptRoot $_ }) | ForEach-Object {
  [ordered]@{ Name=$_.Substring($PSScriptRoot.Length+1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
})
$buildInfo = [ordered]@{
  Version=[System.Diagnostics.FileVersionInfo]::GetVersionInfo($builtExe).FileVersion
  BuiltAtUtc=[DateTime]::UtcNow.ToString('o')
  Compiler=[ordered]@{ Name='Microsoft .NET Framework C# compiler'; SHA256=(Get-FileHash -LiteralPath $compilerPath -Algorithm SHA256).Hash; SignatureStatus=$compilerSignature.Status.ToString(); Signer=$compilerSignature.SignerCertificate.Subject }
  Sources=$sourceRecords
  Executable=[ordered]@{ Name='DesktopSwitcher.exe'; SHA256=(Get-FileHash -LiteralPath $builtExe -Algorithm SHA256).Hash; SignatureStatus=(Get-AuthenticodeSignature -LiteralPath $builtExe).Status.ToString() }
}
$buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'build-info.json') -Encoding UTF8
Write-Output "已生成 $outputPath\DesktopSwitcher.exe"
