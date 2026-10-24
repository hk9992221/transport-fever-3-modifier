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
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $InstallerCompiler = $command.Source }
    else { $InstallerCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $InstallerCompiler -PathType Leaf)) { throw '未找到 Inno Setup 6；请安装后重试，或选择 -Package Portable。' }
$compilerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($InstallerCompiler)
if ($compilerInfo.FileMajorPart -lt 6 -or ($compilerInfo.FileMajorPart -eq 6 -and $compilerInfo.FileMinorPart -lt 3)) { throw 'Setup 构建需要 Inno Setup 6.3 或以上版本。' }
Write-Host ('[构建] Inno Setup：' + $compilerInfo.FileVersion)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments = @('/Qp', ('/DAppVersion=' + $Version), ('/DPackageRoot=' + $PackageDirectory),
    ('/DOutputRoot=' + $OutputDirectory), ('/DMainProgram=' + $MainProgram), (Join-Path $PSScriptRoot 'windows-setup.iss'))
& $InstallerCompiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Setup 安装包编译失败。' }
$setupPath = Join-Path $OutputDirectory ("TransportFever3Modifier-v$Version-windows-x64-setup.exe")
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw '安装编译器未生成预期的 Setup EXE。' }
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($setupPath)
if ($info.FileVersion -ne "$Version.0" -or $info.ProductName -ne '狂热运输3修改器') { throw 'Setup 版本或产品信息不一致。' }
Write-Host ('[构建] Setup 已生成：' + $setupPath)
