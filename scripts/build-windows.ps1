[CmdletBinding()]
param(
    [string]$Compiler = '',
    [string]$OutputDirectory = '',
    [ValidateSet('Portable', 'Setup')][string]$Package = 'Portable',
    [switch]$VerifySetup,
    [Alias('SkipZip')][switch]$Folder
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
if (-not $versionMatch.Success -or -not $protocolMatch.Success) { throw '无法从 Common.cs 读取版本或通信协议。' }
$version = $versionMatch.Groups['value'].Value
$protocol = [int]$protocolMatch.Groups['value'].Value
$mainName = "狂热运输3修改器-v$version.exe"
$runtimeName = "TransportFever3Runtime_v$protocol.dll"
$luaName = "功能接口v$protocol.lua"
$packageName = "TransportFever3Modifier-v$version-windows-x64"
$portableName = $packageName + '-portable.exe'
$outputName = if ($Package -eq 'Setup') { $packageName + '-setup.exe' } else { $portableName }
$outputPath = Join-Path $OutputDirectory $outputName
$folderPath = Join-Path $OutputDirectory $packageName
if ($Folder -and (Test-Path -LiteralPath $folderPath)) { throw '同名文件夹已存在；请指定新的 -OutputDirectory。' }
if (-not $Folder -and ((Test-Path -LiteralPath $outputPath) -or (Test-Path -LiteralPath ($outputPath + '.sha256')))) {
    throw '同名发布 EXE 或校验文件已存在；请移走旧产物或指定新的 -OutputDirectory。'
}
if ($VerifySetup -and $env:GITHUB_ACTIONS -ne 'true') { throw '安装与卸载自测仅在隔离的 GitHub Actions runner 中执行；本地构建请移除 -VerifySetup。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $OutputDirectory ('stage-' + [Guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $stage 'compiled'
$packageRoot = Join-Path $stage $packageName
$installedRoot = ''
$phase = '初始化'
Write-Host ('[构建] v' + $version + ' / PowerShell ' + $PSVersionTable.PSVersion + ' / Package=' + $Package + ' / Folder=' + [bool]$Folder)
if ($env:GITHUB_SHA) { Write-Host ('[构建] 源码提交：' + $env:GITHUB_SHA) }

function Assert-X64Pe([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 77 -or $bytes[1] -ne 90) { throw ('不是有效的 Windows PE 文件：' + $Path) }
    $offset = [BitConverter]::ToInt32($bytes, 60)
    if ($offset -lt 0 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 17744 -or
        [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 34404) { throw ('构建产物必须是有效的 x64 PE：' + $Path) }
}
function Assert-AppVersion([string]$Path) {
    $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($Path)
    if ($fileInfo.FileVersion -ne "$version.0" -or $assembly.Version.ToString() -ne "$version.0" -or
        $fileInfo.ProductName -ne '狂热运输3修改器') { throw ('程序版本或产品名称与源码不一致：' + $Path) }
}
function Copy-PackageFile([string]$From, [string]$RelativePath) {
    if (-not (Test-Path -LiteralPath $From -PathType Leaf)) { throw ('缺少打包输入：' + $From) }
    $target = Join-Path $packageRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $From -Destination $target
}
function Assert-PackageFiles([string]$Root) {
    $actual = @(Get-ChildItem -LiteralPath $Root -File -Recurse)
    if ($actual.Count -ne $expected.Count) { throw ('资源文件数量不一致：' + $Root) }
    foreach ($record in $expected) {
        $path = Join-Path $Root $record.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('缺少资源：' + $record.path) }
        if ((Get-Item -LiteralPath $path).Length -ne $record.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record.sha256) { throw ('资源校验失败：' + $record.path) }
    }
}
function Invoke-SetupCommand([string]$Executable, [string]$Arguments) {
    $process = New-Object Diagnostics.Process
    $process.StartInfo.FileName = $Executable
    $process.StartInfo.Arguments = $Arguments
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    try {
        if (-not $process.Start()) { throw '安装包检查进程启动失败。' }
        if (-not $process.WaitForExit(90000)) { $process.Kill(); throw '安装包检查超时。' }
        if ($process.ExitCode -ne 0) { throw ('安装包检查返回：' + $process.ExitCode) }
    } finally { $process.Dispose() }
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
    Assert-AppVersion $mainPath
    if ((Get-FileHash -LiteralPath $luaPath -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $sourceRoot $luaName) -Algorithm SHA256).Hash) { throw '运行时 Lua 脚本与源码不一致。' }

    $phase = '整理发布文件'; Write-Host ('[构建] ' + $phase)
    Copy-PackageFile $mainPath $mainName
    Copy-PackageFile ($mainPath + '.config') ($mainName + '.config')
    Copy-PackageFile $runtimePath ('运行模块/' + $runtimeName)
    Copy-PackageFile $luaPath ('运行模块/' + $luaName)
    Copy-PackageFile (Join-Path $repositoryRoot 'docs/使用说明.txt') '使用说明.txt'
    Copy-PackageFile (Join-Path $repositoryRoot 'docs/第三方说明.txt') '许可/第三方说明.txt'
    Copy-PackageFile (Join-Path $sourceRoot 'minhook/LICENSE.txt') '许可/MinHook-LICENSE.txt'
    $records = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $verification = '完成源码编译、x64 PE、版本、Lua 一致性、资源 SHA-256 与离线界面布局检查；未连接游戏，不代表游戏功能已实测。'
    $manifest = [ordered]@{
        product = '狂热运输3修改器'; version = $version; mainProgram = $mainName
        architecture = 'x64'; runtimeProtocol = $protocol; sourceCommit = $env:GITHUB_SHA
        createdUtc = [DateTime]::UtcNow.ToString('o'); verification = $verification
        privateConfigurationExcluded = $true; filesExcludeManifest = $true
        fileCountIncludingManifest = $records.Count + 1; files = $records
    }
    $manifestPath = Join-Path $packageRoot '发布清单.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($true)))
    $expected = @($records) + @([ordered]@{
        path = '发布清单.json'; bytes = (Get-Item -LiteralPath $manifestPath).Length
        sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    })
    Assert-PackageFiles $packageRoot

    # Portable checks its real launcher. Setup checks the installed GUI on CI;
    # local Setup builds check the same compiled GUI without installing it.
    $validationRoot = Join-Path $stage '便携检查 with spaces'
    New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
    $portableCache = Join-Path $validationRoot 'cache'
    $indexPath = Join-Path $stage 'portable-index.json'
    $stagedPortable = Join-Path $stage $portableName
    if (-not $Folder -and $Package -eq 'Setup') {
        $phase = '生成标准 Setup 安装包'; Write-Host ('[构建] ' + $phase)
        & (Join-Path $repositoryRoot 'packaging/build-setup.ps1') -PackageDirectory $packageRoot `
            -OutputDirectory $stage -MainProgram $mainName -Version $version
        $stagedDistribution = Join-Path $stage $outputName
    }
    if (-not $Folder -and $Package -eq 'Setup' -and $VerifySetup) {
        $phase = '隔离安装自测'; Write-Host ('[构建] ' + $phase)
        $installedRoot = Join-Path $validationRoot 'installed'
        Invoke-SetupCommand $stagedDistribution ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /TASKS="" /DIR="' + $installedRoot + '" /LOG="' + (Join-Path $validationRoot 'install.log') + '"')
        $installedPayload = Join-Path $installedRoot ("versions/v$version")
        Assert-PackageFiles $installedPayload
        $validationMain = Join-Path $installedPayload $mainName
    } elseif ($Folder -or $Package -eq 'Setup') {
        $validationMain = Join-Path $validationRoot $mainName
        Copy-Item -LiteralPath $mainPath -Destination $validationMain
        Copy-Item -LiteralPath ($mainPath + '.config') -Destination ($validationMain + '.config')
    } else {
        $phase = '生成单文件便携 EXE'; Write-Host ('[构建] ' + $phase)
        & (Join-Path $repositoryRoot 'packaging/build-portable.ps1') -PackageDirectory $packageRoot `
            -Destination $stagedPortable -IndexPath $indexPath -MainProgram $mainName -Version $version
        Assert-X64Pe $stagedPortable
        Assert-AppVersion $stagedPortable
        $validationMain = Join-Path $validationRoot $portableName
        Copy-Item -LiteralPath $stagedPortable -Destination $validationMain
        $stagedDistribution = $stagedPortable
    }
    $phase = '离线界面与资源检查'; Write-Host ('[构建] ' + $phase)
    $layoutReport = Join-Path $validationRoot 'layout-check.json'
    $check = New-Object Diagnostics.Process
    $check.StartInfo.FileName = $validationMain
    $check.StartInfo.Arguments = '--layout-check "' + $layoutReport + '"'
    $check.StartInfo.UseShellExecute = $false
    $check.StartInfo.CreateNoWindow = $true
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_BUILD_CHECK'] = '1'
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_LAYOUT_REPORT'] = $layoutReport
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_PORTABLE_CACHE'] = $portableCache
    $check.StartInfo.EnvironmentVariables['TF3_MODIFIER_PORTABLE_HOME'] = (Join-Path $validationRoot 'profile')
    try {
        if (-not $check.Start()) { throw '离线布局检查进程启动失败。' }
        Write-Host ('[构建] 离线检查 PID：' + $check.Id + '；禁止连接游戏。')
        if (-not $check.WaitForExit(60000)) { $check.Kill(); $check.WaitForExit(5000) | Out-Null; throw '离线界面布局检查超时。' }
        if ($check.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $layoutReport)) {
            if (Test-Path -LiteralPath $layoutReport) { Write-Host ([IO.File]::ReadAllText($layoutReport)) }
            throw '离线界面布局检查未通过。'
        }
        $layoutChecks = @([IO.File]::ReadAllText($layoutReport) | ConvertFrom-Json | ForEach-Object { $_ })
        if ($layoutChecks.Count -eq 0 -or @($layoutChecks | Where-Object { $_.pass -ne $true }).Count -gt 0) { throw '离线布局报告为空或包含失败项。' }
        Write-Host ('离线布局检查通过：' + $layoutChecks.Count + ' 项；未连接游戏。')
    } finally { $check.Dispose() }
    if (-not $Folder -and $Package -eq 'Portable') {
        $indexHash = (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-PackageFiles (Join-Path $portableCache $indexHash)
        Write-Host ('便携资源校验通过：' + $expected.Count + ' 个内嵌文件。')
    }
    if ($installedRoot) {
        $phase = '卸载自测'; Write-Host ('[构建] ' + $phase)
        Invoke-SetupCommand (Join-Path $installedRoot 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
        if (Test-Path -LiteralPath $validationMain) { throw '卸载后安装的主程序仍然存在。' }
        $installedRoot = ''
    }

    $phase = '输出已验证产物'; Write-Host ('[构建] ' + $phase)
    if ($Folder) {
        Move-Item -LiteralPath $packageRoot -Destination $folderPath
        Write-Host ('可直接运行的文件夹：' + $folderPath)
        return
    }
    $distributionHash = (Get-FileHash -LiteralPath $stagedDistribution -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($stagedDistribution + '.sha256'), ($distributionHash + '  ' + $outputName + "`n"), [Text.Encoding]::ASCII)
    Move-Item -LiteralPath $stagedDistribution -Destination $outputPath
    Move-Item -LiteralPath ($stagedDistribution + '.sha256') -Destination ($outputPath + '.sha256')
    Write-Host ('发布程序：' + $outputPath)
    Write-Host ('SHA-256：' + $distributionHash)
    $signatureStatus = (Get-AuthenticodeSignature -LiteralPath $outputPath).Status
    Write-Host ('Authenticode 状态：' + $signatureStatus + '；哈希校验不代表杀毒检测通过。')
    if ($env:GITHUB_STEP_SUMMARY) {
        @("## Windows x64 v$version", '', ('产物：`' + $outputName + '`'), '', ('SHA-256：`' + $distributionHash + '`'), '',
            ('Authenticode：' + $signatureStatus), '', $verification, '', '发布 EXE 与校验文件已生成；下一步发布到 GitHub Releases。') |
            Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
} catch {
    Write-Host ('[构建失败阶段] ' + $phase)
    throw
} finally {
    if ($installedRoot -and (Test-Path -LiteralPath (Join-Path $installedRoot 'unins000.exe'))) {
        try { Invoke-SetupCommand (Join-Path $installedRoot 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' }
        catch { Write-Warning ('隔离安装目录卸载失败：' + $_.Exception.Message) -WarningAction Continue }
    }
    $cleanupError = $null
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction Stop }
            $cleanupError = $null; break
        } catch { $cleanupError = $_.Exception.Message; if ($attempt -lt 3) { Start-Sleep -Milliseconds 400 } }
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
