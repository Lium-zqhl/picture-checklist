param([string]$OutputDirectory, [switch]$Tests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    throw '需要 Windows 的 .NET Framework 4.8 或更高版本及系统 C# 编译器。'
}
$runtime = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
if ($null -eq $runtime -or $runtime.Release -lt 528040) {
    throw '构建需要 .NET Framework 4.8 或更高版本。请先运行 scripts\check-environment.ps1。'
}
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw -Encoding UTF8).Trim()
$sourceVersion = Get-Content -LiteralPath (Join-Path $root 'src\Program.cs') -Raw -Encoding UTF8
if ($sourceVersion -notmatch ('AssemblyVersion\("' + [regex]::Escape($version) + '\.0"\)')) {
    throw 'VERSION 与 AssemblyVersion 不一致。'
}
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $root 'artifacts\app' } else { [IO.Path]::GetFullPath($OutputDirectory) }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$wpf = Join-Path $framework 'WPF'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001','/langversion:5','/warn:4',('/win32manifest:' + (Join-Path $root 'src\app.manifest')),('/out:' + (Join-Path $output 'PictureChecklist.exe')))
& (Join-Path $framework 'csc.exe') @compilerArgs @refs @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $root 'src\app.config') -Destination (Join-Path $output 'PictureChecklist.exe.config') -Force
if ($Tests) { & (Join-Path $PSScriptRoot 'run-tests.ps1'); if ($LASTEXITCODE -ne 0) { throw '测试失败。' } }
Write-Output (Join-Path $output 'PictureChecklist.exe')
