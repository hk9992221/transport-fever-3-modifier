[CmdletBinding()]
param(
    [string]$Compiler = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw '此构建入口需要 Windows x64。' }

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repositoryRoot 'src'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'dist' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$protocolSource = [IO.File]::ReadAllText((Join-Path $sourceRoot 'Common.cs'))
$versionMatch = [regex]::Match($protocolSource, '\bVersion\s*=\s*"(?<value>\d+\.\d+\.\d+)"')
$protocolMatch = [regex]::Match($protocolSource, '\bRuntimeProtocol\s*=\s*(?<value>\d+)')
if (-not $versionMatch.Success -or -not $protocolMatch.Success) {
    throw '无法从 Common.cs 读取版本或通信协议。'
}
$version = $versionMatch.Groups['value'].Value
$protocol = [int]$protocolMatch.Groups['value'].Value
$mainName = "狂热运输3修改器-v$version.exe"
$runtimeName = "TransportFever3Runtime_v$protocol.dll"
$luaName = "功能接口v$protocol.lua"
$packageName = "TransportFever3Modifier-v$version-windows-x64"
$zipPath = Join-Path $OutputDirectory ($packageName + '.zip')
if (Test-Path -LiteralPath $zipPath) {
    throw '同名 ZIP 已存在；请移走旧产物或指定新的 -OutputDirectory。'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $OutputDirectory ('stage-' + [Guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $stage 'compiled'
$packageRoot = Join-Path $stage $packageName

function Assert-X64Pe([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 77 -or $bytes[1] -ne 90) {
        throw ('不是有效的 Windows PE 文件：' + $Path)
    }
    $offset = [BitConverter]::ToInt32($bytes, 60)
    if ($offset -lt 0 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 17744 -or
        [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 34404) {
        throw ('构建产物必须是有效的 x64 PE：' + $Path)
    }
}

function Copy-PackageFile([string]$From, [string]$RelativePath) {
    if (-not (Test-Path -LiteralPath $From -PathType Leaf)) {
        throw ('缺少打包输入：' + $From)
    }
    $target = Join-Path $packageRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $From -Destination $target
}

try {
    New-Item -ItemType Directory -Path $buildRoot, $packageRoot -Force | Out-Null
    $runtimeRoot = Join-Path $buildRoot '运行模块'
    & (Join-Path $sourceRoot 'build-native.ps1') -Compiler $Compiler -Destination $runtimeRoot
    & (Join-Path $sourceRoot 'build.ps1') -Destination $buildRoot

    $mainPath = Join-Path $buildRoot $mainName
    $runtimePath = Join-Path $runtimeRoot $runtimeName
    $luaPath = Join-Path $runtimeRoot $luaName
    Assert-X64Pe $mainPath
    Assert-X64Pe $runtimePath
    $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($mainPath)
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($mainPath)
    if ($fileInfo.FileVersion -ne "$version.0" -or
        $assembly.Version.ToString() -ne "$version.0" -or
        $fileInfo.ProductName -ne '狂热运输3修改器') {
        throw '主程序版本或产品名称与源码不一致。'
    }
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $luaName) -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $luaPath -Algorithm SHA256).Hash -ne $sourceHash) {
        throw '运行时 Lua 脚本与源码不一致。'
    }

    Copy-PackageFile $mainPath $mainName
    Copy-PackageFile $runtimePath ('运行模块/' + $runtimeName)
    Copy-PackageFile $luaPath ('运行模块/' + $luaName)
    Copy-PackageFile (Join-Path $repositoryRoot 'docs/使用说明.txt') '使用说明.txt'
    Copy-PackageFile (Join-Path $repositoryRoot 'docs/第三方说明.txt') '许可/第三方说明.txt'
    Copy-PackageFile (Join-Path $sourceRoot 'minhook/LICENSE.txt') '许可/MinHook-LICENSE.txt'

    $records = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path = $_.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
    $verification = '完成源码编译、x64 PE、版本、Lua 一致性与 ZIP 文件校验；未运行游戏，不代表游戏功能已实测。'
    $manifest = [ordered]@{
        product = '狂热运输3修改器'
        version = $version
        mainProgram = $mainName
        architecture = 'x64'
        runtimeProtocol = $protocol
        sourceCommit = $env:GITHUB_SHA
        createdUtc = [DateTime]::UtcNow.ToString('o')
        verification = $verification
        privateConfigurationExcluded = $true
        filesExcludeManifest = $true
        fileCountIncludingManifest = $records.Count + 1
        files = $records
    }
    $utf8 = New-Object Text.UTF8Encoding($true)
    $manifestPath = Join-Path $packageRoot '发布清单.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), $utf8)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $packageRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true, [Text.Encoding]::UTF8)
    $expected = @($records) + @([ordered]@{
        path = '发布清单.json'
        bytes = (Get-Item -LiteralPath $manifestPath).Length
        sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    })
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -ne $expected.Count) { throw 'ZIP 文件数量不一致。' }
        foreach ($record in $expected) {
            $entry = $archive.GetEntry($packageName + '/' + $record.path)
            if ($null -eq $entry -or $entry.Length -ne $record.bytes) { throw 'ZIP 内容不完整。' }
            $inputStream = $entry.Open()
            $hasher = [Security.Cryptography.SHA256]::Create()
            try {
                $hash = [BitConverter]::ToString($hasher.ComputeHash($inputStream)).Replace('-', '')
                if ($hash -ne $record.sha256) { throw ('ZIP 校验失败：' + $record.path) }
            } finally {
                $hasher.Dispose()
                $inputStream.Dispose()
            }
        }
    } finally { $archive.Dispose() }

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zipPath + '.sha256'), ($zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + "`n"), [Text.Encoding]::ASCII)
    Write-Host ('便携包：' + $zipPath)
    Write-Host ('SHA-256：' + $zipHash)
    if ($env:GITHUB_STEP_SUMMARY) {
        @(
            "## Windows x64 v$version"
            ''
            ('产物：`' + [IO.Path]::GetFileName($zipPath) + '`')
            ''
            ('SHA-256：`' + $zipHash + '`')
            ''
            $verification
            ''
            '请在本次运行的 Artifacts 中下载；产物保留 7 天。'
        ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
