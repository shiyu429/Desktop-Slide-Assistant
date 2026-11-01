param([string]$Version='1.3.3')
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$projectPath=(Resolve-Path -LiteralPath $projectPath).Path
$releasePath=Join-Path $projectPath "releases\$Version\portable"
$sourceExe=Join-Path $releasePath 'DesktopSwitcher.exe'
$sourceHash=(Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash
$root=[System.IO.Path]::GetFullPath((Join-Path $projectPath "build\product-check-$Version-$([Guid]::NewGuid().ToString('N'))"))
if(!$root.StartsWith($projectPath+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '测试目录越界' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$log=Join-Path $projectPath "build\qa\full-product-v$Version.txt"
@("Product version=$Version","Exact delivered EXE SHA256=$sourceHash",'Runs the real DesktopSwitcher.exe entry point; does not use Tests.exe as the product') | Set-Content -LiteralPath $log -Encoding UTF8
function Record([string]$line) { Add-Content -LiteralPath $log -Value $line -Encoding UTF8; Write-Output $line }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DesktopSwitcherThreadProbe {
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenThread(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern uint SuspendThread(IntPtr thread);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
foreach($case in @('Desktop','Courseware','BlockedUI')) {
  $directory=Join-Path $root $case
  $profile=Join-Path $directory 'profile'
  New-Item -ItemType Directory -Path $profile -Force | Out-Null
  $exe=Join-Path $directory 'DesktopSwitcher.exe'
  Copy-Item -LiteralPath $sourceExe -Destination $exe
  Copy-Item -LiteralPath (Join-Path $releasePath 'DesktopSwitcher.exe.config') -Destination $directory
  if((Get-FileHash -LiteralPath $exe).Hash -ne $sourceHash) { throw '测试副本与交付 EXE 不同' }
  $mode=if($case -eq 'Desktop') {'Desktop'} else {'Courseware'}
  "<Settings><Mode>$mode</Mode><SetupCompleted>true</SetupCompleted><CoursewareStartupAsked>true</CoursewareStartupAsked><SuppressCoursewareTips>true</SuppressCoursewareTips><DefaultPosition>true</DefaultPosition></Settings>" |
    Set-Content -LiteralPath (Join-Path $profile 'settings.xml') -Encoding UTF8
  foreach($round in 1..2) {
    $process=$null; $thread=[IntPtr]::Zero; $suspended=$false
    try {
      $process=Start-Process -FilePath $exe -ArgumentList @('--data-dir',('"'+$profile+'"')) -WindowStyle Hidden -PassThru
      $ready=$false; $trace=Join-Path $profile 'diagnostics.log'; $deadline=[DateTime]::UtcNow.AddSeconds(10)
      while([DateTime]::UtcNow -lt $deadline) {
        if($process.HasExited) { throw "产品启动后提前退出：$($process.ExitCode)" }
        if((Test-Path -LiteralPath $trace) -and (Get-Content -LiteralPath $trace -Raw) -match "Ready PID=$($process.Id)\b") { $ready=$true; break }
        Start-Sleep -Milliseconds 80
      }
      if(!$ready) { throw '真实产品未进入 Ready 状态' }
      Record "PASS actual product ready: case=$case round=$round PID=$($process.Id)"
      if($case -eq 'BlockedUI') {
        $content=Get-Content -LiteralPath $trace -Raw
        $matchesForThread=[regex]::Matches($content,"Start PID=$($process.Id) TID=(\d+)")
        if($matchesForThread.Count -ne 1) { throw '无法确认本次测试进程 UI 线程' }
        $threadId=[uint32]$matchesForThread[0].Groups[1].Value
        $thread=[DesktopSwitcherThreadProbe]::OpenThread(2,$false,$threadId)
        if($thread -eq [IntPtr]::Zero) { throw '无法取得测试 UI 线程' }
        if([DesktopSwitcherThreadProbe]::SuspendThread($thread) -eq [uint32]::MaxValue) { throw '无法模拟测试 UI 线程阻塞' }
        $suspended=$true
      }
      $timer=[Diagnostics.Stopwatch]::StartNew()
      $quit=Start-Process -FilePath $exe -ArgumentList @('--exit','--data-dir',('"'+$profile+'"')) -WindowStyle Hidden -PassThru
      if(!$quit.WaitForExit(5000) -or $quit.ExitCode -ne 0) { throw '产品退出命令未成功' }
      $quit.Dispose()
      if(!$process.WaitForExit(5000)) { throw '真实产品退出后仍残留' }
      Record "PASS actual product exited: case=$case round=$round elapsedMs=$($timer.ElapsedMilliseconds) code=$($process.ExitCode)"
    } finally {
      if($suspended -and $thread -ne [IntPtr]::Zero) { [DesktopSwitcherThreadProbe]::ResumeThread($thread) | Out-Null }
      if($thread -ne [IntPtr]::Zero) { [DesktopSwitcherThreadProbe]::CloseHandle($thread) | Out-Null }
      if($null -ne $process) {
        if(!$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(); Record 'FAIL fixture process had to be stopped by the test harness' }
        $process.Dispose()
      }
    }
  }
  if(!$exe.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '删除目标越界' }
  Remove-Item -LiteralPath $exe -ErrorAction Stop
  Remove-Item -LiteralPath (Join-Path $directory 'DesktopSwitcher.exe.config') -ErrorAction Stop
  if(Test-Path -LiteralPath $exe) { throw '退出后测试副本仍无法删除' }
  Record "PASS actual EXE restart and deletion: case=$case"
}
Record 'PASS full product launch / quit / relaunch / delete chain completed for all cases'
Record 'Only generated fixture copies were deleted. No user startup entry or normal settings were changed.'
