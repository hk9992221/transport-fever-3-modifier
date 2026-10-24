[CmdletBinding()]
param(
    [string]$Compiler = '',
    [string]$OutputDirectory = '',
    [switch]$SkipZip
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
$folderPath = Join-Path $OutputDirectory $packageName
if ($SkipZip -and (Test-Path -LiteralPath $folderPath)) {
    throw '同名文件夹已存在；请指定新的 -OutputDirectory。'
}
if (-not $SkipZip -and ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath ($zipPath + '.sha256')))) {
    throw '同名 ZIP 或校验文件已存在；请移走旧产物或指定新的 -OutputDirectory。'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $OutputDirectory ('stage-' + [Guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $stage 'compiled'
$packageRoot = Join-Path $stage $packageName
$stagedZipPath = Join-Path $stage ($packageName + '.zip')
$phase = '初始化'
Write-Host ('[构建] v' + $version + ' / PowerShell ' + $PSVersionTable.PSVersion + ' / SkipZip=' + [bool]$SkipZip)

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
    $phase = '编译'; Write-Host ('[构建] ' + $phase)
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

    $phase = '离线布局检查'; Write-Host ('[构建] ' + $phase)
    # Run a copy of the GUI without any runtime DLL or Lua next to it. Both an
    # environment marker and the entry argument enforce offline mode in-process.
    $validationRoot = Join-Path $stage 'layout-check'
    New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
    $validationMain = Join-Path $validationRoot $mainName
    Copy-Item -LiteralPath $mainPath -Destination $validationMain
    Copy-Item -LiteralPath ($mainPath + '.config') -Destination ($validationMain + '.config')
    $layoutReport = Join-Path $validationRoot 'layout-check.json'
    $check = New-Object Diagnostics.Process
    $check.StartInfo.FileName = $validationMain
    $check.StartInfo.Arguments = '--layout-check "' + $layoutReport + '"'
    $check.StartInfo.UseShellExecute = $false
    $check.StartInfo.CreateNoWindow = $true
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_BUILD_CHECK'] = '1'
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_LAYOUT_REPORT'] = $layoutReport
    if (-not $check.Start()) { throw '离线布局检查进程启动失败。' }
    try {
        Write-Host ('[构建] 离线检查 PID：' + $check.Id + '；运行模块未复制到检查目录。')
        if (-not $check.WaitForExit(45000)) {
            $check.Kill(); $check.WaitForExit(5000) | Out-Null
            throw '离线界面布局检查超时。'
        }
        if ($check.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $layoutReport)) {
            if (Test-Path -LiteralPath $layoutReport) { Write-Host ([IO.File]::ReadAllText($layoutReport)) }
            throw '离线界面布局检查未通过。'
        }
        # Windows PowerShell 5.1 emits a JSON array as one pipeline object.
        # Enumerate its rows before counting or checking individual results.
        $layoutChecks = @([IO.File]::ReadAllText($layoutReport) | ConvertFrom-Json | ForEach-Object { $_ })
        if ($layoutChecks.Count -eq 0 -or @($layoutChecks | Where-Object { $_.pass -ne $true }).Count -gt 0) {
            throw '离线布局报告为空或包含失败项。'
        }
        Write-Host ('离线布局检查通过：' + $layoutChecks.Count + ' 项；未连接游戏。')
    } finally { $check.Dispose() }

    $phase = '整理便携文件'; Write-Host ('[构建] ' + $phase)
    Copy-PackageFile $mainPath $mainName
    Copy-PackageFile ($mainPath + '.config') ($mainName + '.config')
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
    $verification = '完成源码编译、x64 PE、版本、Lua 一致性、离线界面布局与 ZIP 校验；未连接游戏，不代表游戏功能已实测。'
    if ($SkipZip) { $verification = '完成源码编译、x64 PE、版本、Lua 一致性、离线界面布局与文件清单校验；跳过 ZIP；未连接游戏。' }
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

    $expected = @($records) + @([ordered]@{
        path = '发布清单.json'
        bytes = (Get-Item -LiteralPath $manifestPath).Length
        sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    })
    if ($SkipZip) {
        $phase = '校验并输出文件夹'; Write-Host ('[构建] ' + $phase)
        foreach ($record in $expected) {
            $file = Get-Item -LiteralPath (Join-Path $packageRoot $record.path)
            if ($file.Length -ne $record.bytes -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $record.sha256) {
                throw ('文件夹校验失败：' + $record.path)
            }
        }
        Move-Item -LiteralPath $packageRoot -Destination $folderPath
        Write-Host ('可直接运行的文件夹：' + $folderPath)
        Write-Host '已跳过 ZIP 压缩。'
        return
    }
    $phase = '创建并校验 ZIP'; Write-Host ('[构建] ' + $phase)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # Explicit entry names avoid CreateFromDirectory's legacy backslash paths
    # under Windows PowerShell 5.1 / .NET Framework compatibility settings.
    $archive = [IO.Compression.ZipFile]::Open(
        $stagedZipPath, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
    try {
        foreach ($record in $expected) {
            $entryName = $packageName + '/' + $record.path
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, (Join-Path $packageRoot $record.path), $entryName,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }

    $archive = [IO.Compression.ZipFile]::OpenRead($stagedZipPath)
    try {
        if ($archive.Entries.Count -ne $expected.Count) {
            throw ('ZIP 文件数量不一致：应为 {0}，实际为 {1}。' -f $expected.Count, $archive.Entries.Count)
        }
        foreach ($record in $expected) {
            $entryName = $packageName + '/' + $record.path
            $entry = $archive.GetEntry($entryName)
            if ($null -eq $entry) { throw ('ZIP 缺少文件：' + $entryName) }
            if ($entry.Length -ne $record.bytes) {
                throw ('ZIP 文件大小不一致：{0}；应为 {1} 字节，实际为 {2} 字节。' -f $entryName, $record.bytes, $entry.Length)
            }
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

    $zipHash = (Get-FileHash -LiteralPath $stagedZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($stagedZipPath + '.sha256'), ($zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + "`n"), [Text.Encoding]::ASCII)
    # Failed checks leave no ZIP in the output directory. Only publish the
    # validated archive and its checksum; the temporary stage is always removed.
    Move-Item -LiteralPath $stagedZipPath -Destination $zipPath
    Move-Item -LiteralPath ($stagedZipPath + '.sha256') -Destination ($zipPath + '.sha256')
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
            '便携包与校验文件已生成；工作流将在下一步发布到 GitHub Releases。'
        ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
} catch {
    Write-Host ('[构建失败阶段] ' + $phase)
    throw
} finally {
    # Cleanup is best-effort: a locked file must not replace the original error,
    # or turn a successfully verified package into a failed build.
    $cleanupError = $null
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction Stop }
            $cleanupError = $null; break
        } catch {
            $cleanupError = $_.Exception.Message
            if ($attempt -lt 3) { Start-Sleep -Milliseconds 400 }
        }
    }
    if ($cleanupError) {
        Write-Warning ('临时目录未完全清理：' + $stage + '；' + $cleanupError) -WarningAction Continue
        try {
            foreach ($game in @(Get-Process -Name 'TransportFever3' -ErrorAction SilentlyContinue)) {
                foreach ($loaded in $game.Modules) {
                    if ($loaded.FileName.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                        Write-Warning ('占用来源：游戏 PID ' + $game.Id + ' 仍加载了 ' + $loaded.FileName + '。请正常退出游戏后再删除临时目录。') -WarningAction Continue
                    }
                }
            }
        } catch { Write-Warning '无法读取占用进程的模块信息；请关闭占用程序后手动删除上述临时目录。' -WarningAction Continue }
    }
}
