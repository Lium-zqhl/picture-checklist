param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw -Encoding UTF8).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION 格式无效。' }
$destination = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $root 'dist' } else { [IO.Path]::GetFullPath($OutputDirectory) }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$prefix = 'picture-checklist-' + $version
$appZip = Join-Path $destination ($prefix + '-windows.zip')
$sourceZip = Join-Path $destination ($prefix + '-source.zip')
foreach ($path in @($appZip,$sourceZip)) { if (Test-Path -LiteralPath $path) { throw ('文件已存在，请选择新的输出目录：' + $path) } }
$run = Join-Path $root ('artifacts\package\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$portable = Join-Path $run ($prefix + '-windows')
$source = Join-Path $run ($prefix + '-source')
New-Item -ItemType Directory -Path $portable,$source -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $portable | Out-Host

foreach ($required in @('LICENSE','README.md','VERSION','docs\使用说明.txt','docs\兼容性.md','examples\getting-started\checklist.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw ('缺少公开交付文件：' + $required) }
}
foreach ($name in @('LICENSE','VERSION')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $portable $name) }
foreach ($name in @('使用说明.txt','兼容性.md')) { Copy-Item -LiteralPath (Join-Path $root ('docs\' + $name)) -Destination (Join-Path $portable $name) }
foreach ($name in @('check-environment.ps1','Check-Environment.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $portable $name) }
New-Item -ItemType Directory -Path (Join-Path $portable '示例清单') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'examples\getting-started') -Destination (Join-Path $portable '示例清单\入门示例') -Recurse

# Explicit source allowlist: never package the workspace, artifacts, or local history.
foreach ($name in @('README.md','LICENSE','VERSION','CONTRIBUTING.md','CHANGELOG.md','.gitignore','.gitattributes')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $source $name)
}
foreach ($name in @('src','scripts','tests','docs','.github','examples')) {
    $directory = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $directory)) { throw ('缺少源码目录：' + $name) }
    $links = @(Get-ChildItem -LiteralPath $directory -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($links.Count -gt 0) { throw '公开源码目录不得含符号链接或目录联接。' }
    Copy-Item -LiteralPath $directory -Destination (Join-Path $source $name) -Recurse
}

function Assert-PublicFiles([string]$folder) {
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -Force -File) {
        $relative = $file.FullName.Substring($folder.Length + 1).Replace('\','/')
        if ($file.Name -match '^(preferences|progress|tasks|library-visibility)\.json' -or $file.Extension -in @('.lock','.pdb','.log','.tmp') -or $relative -match '(^|/)(migration|backups|verification|fixtures|prototypes|\.git)(/|$)') {
            throw ('交付目录含不应公开的文件：' + $relative)
        }
        if ($file.Extension -in @('.cs','.ps1','.cmd','.md','.txt','.json','.yml','.yaml','.config','.manifest') -or $file.Name -in @('LICENSE','VERSION')) {
            $text = [IO.File]::ReadAllText($file.FullName)
            if ($text -match '(?i)[A-Z]:[\\/](Users|Codex)[\\/]|gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----') {
                throw ('交付文本含个人绝对路径或疑似凭据：' + $relative)
            }
        }
    }
}
Assert-PublicFiles $portable
Assert-PublicFiles $source

# The public example is a reproducible synthetic fixture, not a user checklist.
$example = Get-Content -LiteralPath (Join-Path $portable '示例清单\入门示例\checklist.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($example.Items.Count -ne 3) { throw '公开演示清单应为3条。' }
foreach ($asset in $example.Assets) {
    $path = Join-Path (Join-Path $portable '示例清单\入门示例') $asset.RelativePath
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.Sha256) { throw '演示图片哈希不一致。' }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Archive-And-Verify([string]$folder,[string]$archive) {
    $parent = Split-Path -Parent $folder
    $files = @(Get-ChildItem -LiteralPath $folder -Recurse -Force -File)
    $zip = [IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($parent.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    # Keep verification paths short enough for Windows PowerShell 5.1 / MAX_PATH.
    $extractName = if ($archive -eq $appZip) { 'verify-win' } else { 'verify-src' }
    $extract = Join-Path $run $extractName
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$extract)
    $hashes = foreach ($file in $files) {
        $relative = $file.FullName.Substring($parent.Length + 1)
        $sha = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath (Join-Path $extract $relative) -Algorithm SHA256).Hash -ne $sha) { throw ('解压文件哈希不一致：' + $relative) }
        [PSCustomObject]@{ Path=$relative.Replace('\','/'); Bytes=$file.Length; SHA256=$sha }
    }
    if (@(Get-ChildItem -LiteralPath $extract -Recurse -Force -File).Count -ne $files.Count) { throw '解压文件数量不一致。' }
    [PSCustomObject]@{ File=(Split-Path -Leaf $archive); Bytes=(Get-Item -LiteralPath $archive).Length; SHA256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash; FileCount=$files.Count; Files=$hashes }
}
$packages = @(Archive-And-Verify $portable $appZip; Archive-And-Verify $source $sourceZip)
$manifest = [PSCustomObject]@{ Version=$version; CreatedUtc=[DateTime]::UtcNow.ToString('o'); License='MIT'; Packages=$packages }
$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $destination ($prefix + '-manifest.json')) -Encoding UTF8
$packages | ForEach-Object { $_.SHA256.ToLowerInvariant() + '  ' + $_.File } | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding ASCII
$packages | Select-Object File,Bytes,FileCount,SHA256 | Format-Table -AutoSize
Write-Output ('已解包逐文件核对，输出目录：' + $destination)
