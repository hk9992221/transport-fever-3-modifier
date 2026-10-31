[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$MainProgram,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$InstallerCompiler = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or [IO.Path]::GetFileName($MainProgram) -ne $MainProgram) { throw '安装包版本或主程序名称无效。' }
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $MainProgram) -PathType Leaf)) { throw '安装包缺少主程序。' }
if (-not $InstallerCompiler) {
    # Prefer an installed compiler over a PATH proxy (for example a package-
    # manager shim). A proxy's PE version is not the Inno compiler version.
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $InstallerCompiler = $candidate; break }
    }
    if (-not $InstallerCompiler) {
        $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { $InstallerCompiler = $command.Source }
    }
}
if (-not $InstallerCompiler -or -not (Test-Path -LiteralPath $InstallerCompiler -PathType Leaf)) { throw '未找到 Inno Setup 6；请安装后重试，或选择 -Package Portable。' }
$InstallerCompiler = (Resolve-Path -LiteralPath $InstallerCompiler).ProviderPath
Write-Host ('[构建] Inno Setup 编译器：' + $InstallerCompiler)
# windows-setup.iss checks PREPROCVER inside the actual compiler engine.
# Keep the native banner/status output so its engine version appears in CI logs.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments = @(('/DAppVersion=' + $Version), ('/DPackageRoot=' + $PackageDirectory),
    ('/DOutputRoot=' + $OutputDirectory), ('/DMainProgram=' + $MainProgram), (Join-Path $PSScriptRoot 'windows-setup.iss'))
& $InstallerCompiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Setup 安装包编译失败。' }
$setupPath = Join-Path $OutputDirectory ("TransportFever3Modifier-v$Version-windows-x64-setup.exe")
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw '安装编译器未生成预期的 Setup EXE。' }
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($setupPath)
# FileVersion is a display string; compare the fixed four-part PE version.
$actualVersion = [Version]::new($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
$expectedVersion = [Version]("$Version.0")
# Inno Setup pads string resources to their reserved size. Strip only the
# trailing space/NUL padding; keep rejecting a genuinely different product.
$actualProductName = ([string]$info.ProductName).TrimEnd([char[]]@([char]0, [char]32))
$metadata = [ordered]@{
    binaryVersion = $actualVersion.ToString()
    fileVersionText = $info.FileVersion
    productVersion = $info.ProductVersion
    productName = $actualProductName
    productNameRaw = $info.ProductName
}
$metadataJson = $metadata | ConvertTo-Json -Compress
Write-Host ('[构建] Setup 版本信息：' + $metadataJson)
if ($actualVersion -ne $expectedVersion -or $actualProductName -cne '狂热运输3修改器') {
    throw ('Setup 版本或产品信息不一致。预期版本：' + $expectedVersion + '；预期产品：狂热运输3修改器；实际：' + $metadataJson)
}
Write-Host ('[构建] Setup 已生成：' + $setupPath)

