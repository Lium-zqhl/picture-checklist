$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo 'artifacts\tests'
# Leave room for the product's staging directory and content-hash image names under MAX_PATH.
do { $run = Join-Path $testRoot ('t-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }
while (Test-Path -LiteralPath $run)
$snapshot = Join-Path $run 'source'
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    throw 'Install the Windows .NET Framework 4.8 developer/runtime components before running these tests.'
}
$wpf = Join-Path $framework 'WPF'
$inputs = @(Get-ChildItem -LiteralPath (Join-Path $repo 'src') -File)
$inputs += @(Get-ChildItem -LiteralPath (Join-Path $repo 'tests') -Filter '*.cs' -File)
$hashes = foreach ($file in $inputs) {
    $destination = Join-Path $snapshot $file.Name
    if (Test-Path -LiteralPath $destination) { throw ('Duplicate source filename: ' + $file.Name) }
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    [PSCustomObject]@{ Name = $file.Name; SHA256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source-hashes.json') -Encoding UTF8
$refs = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | ForEach-Object FullName)
$exe = Join-Path $run 'PublicChecks.exe'
$compilerArgs = @('/nologo', '/target:exe', '/main:PictureChecklist.PublicChecks', '/platform:anycpu', '/optimize+', '/codepage:65001', '/langversion:5', '/warn:4', ('/win32manifest:' + (Join-Path $snapshot 'app.manifest')), ('/out:' + $exe))
[IO.File]::WriteAllText((Join-Path $run 'compile.txt'), '', [Text.UTF8Encoding]::new($false))
& (Join-Path $framework 'csc.exe') @compilerArgs @refs @sources 2>&1 | Tee-Object -FilePath (Join-Path $run 'compile.txt')
if ($LASTEXITCODE -ne 0) { throw ('Test compilation failed. Evidence: ' + $run) }
Copy-Item -LiteralPath (Join-Path $snapshot 'app.config') -Destination ($exe + '.config')

$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $exe
$start.Arguments = '"' + $run + '"'
$start.WorkingDirectory = $run
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = [Text.Encoding]::UTF8
$start.StandardErrorEncoding = [Text.Encoding]::UTF8
$process = New-Object Diagnostics.Process
$process.StartInfo = $start
$null = $process.Start()
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(90000)) {
    throw ('Test timed out after 90 seconds; isolated test process PID ' + $process.Id + ' was not killed. Evidence: ' + $run)
}
$code = $process.ExitCode
[IO.File]::WriteAllText((Join-Path $run 'stdout.txt'), $stdout.Result, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $run 'stderr.txt'), $stderr.Result, [Text.UTF8Encoding]::new($false))
$process.Dispose()
Get-Content -LiteralPath (Join-Path $run 'stdout.txt') -Encoding UTF8
if ((Get-Item -LiteralPath (Join-Path $run 'stderr.txt')).Length -gt 0) { Get-Content -LiteralPath (Join-Path $run 'stderr.txt') -Encoding UTF8 }
Write-Output ('EVIDENCE ' + $run)
if ($code -ne 0) { throw ('Tests failed with exit code ' + $code + '. Evidence: ' + $run) }
