param([switch]$Json)
$ErrorActionPreference = 'Stop'
$release = 0
try { $release = [int](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release).Release } catch { }
$windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$build = [int]$windows.CurrentBuildNumber
$arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
$isArm = $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64'
if ($isArm) { $arch = 'ARM64' }
$result = [PSCustomObject]@{
    WindowsBuild = $build
    Architecture = $arch
    DotNetFrameworkRelease = $release
    RuntimeReady = ($release -ge 528040)
    TargetWindowsVersion = ($build -ge 18362)
    ArchitectureInTarget = (-not $isArm)
    TargetEnvironmentReady = ($release -ge 528040 -and $build -ge 18362 -and -not $isArm)
}
if ($Json) { $result | ConvertTo-Json; exit 0 }
Write-Host '图文修改清单 · 环境检查（仅检查，不安装或修改系统）'
Write-Host ('Windows 内部版本：' + $build + '；架构：' + $arch)
Write-Host ('.NET Framework Release：' + $release)
if ($result.TargetEnvironmentReady) {
    Write-Host '环境符合目标条件。解压整个便携包后，可双击 PictureChecklist.exe。'
    Write-Host '此检查不等于该电脑全部功能已验收；请先用演示清单试用。'
} else {
    if (-not $result.RuntimeReady) { Write-Host '缺少 .NET Framework 4.8 或更高版本。官方下载：https://dotnet.microsoft.com/download/dotnet-framework/net48' }
    if (-not $result.TargetWindowsVersion) { Write-Host '目标系统为 Windows 10 1903 及以上、Windows 11；此系统不在当前支持目标内。' }
    if ($isArm) { Write-Host 'ARM64 尚未验收，不在当前承诺的架构范围内。' }
    exit 1
}
