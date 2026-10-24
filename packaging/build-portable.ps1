[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$IndexPath,
    [Parameter(Mandatory = $true)][string]$MainProgram,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'src'
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) { throw '未找到 .NET Framework C# 编译器。' }
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + $Destination), ('/win32manifest:' + (Join-Path $source 'app.manifest')),
    ('/win32icon:' + (Join-Path $source 'assets/App.ico')),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll')
$files = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse | Sort-Object FullName)
$records = @()
for ($i = 0; $i -lt $files.Count; $i++) {
    $file = $files[$i]
    $resource = 'TransportFever3Modifier.Portable.File.' + $i
    $records += [ordered]@{
        path = $file.FullName.Substring($PackageDirectory.Length + 1).Replace('\', '/')
        resource = $resource
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $arguments += '/resource:' + $file.FullName + ',' + $resource
}
if ($records.Count -eq 0 -or -not (Test-Path -LiteralPath (Join-Path $PackageDirectory $MainProgram) -PathType Leaf)) {
    throw '便携输入缺少主程序。'
}
$index = [ordered]@{ format = 1; version = $Version; mainProgram = $MainProgram; files = $records }
[IO.File]::WriteAllText($IndexPath, ($index | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
$arguments += '/resource:' + $IndexPath + ',TransportFever3Modifier.Portable.Index.json'
$arguments += (Join-Path $PSScriptRoot 'PortableLauncher.cs'), (Join-Path $source 'AssemblyInfo.cs')
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw '单文件便携程序编译失败。' }
