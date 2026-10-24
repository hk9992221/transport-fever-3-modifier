param([string]$Destination)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path (Split-Path $src -Parent) 'build/package' }
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$versionMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $src 'Common.cs')), '\bVersion\s*=\s*"(?<value>\d+\.\d+\.\d+)"')
if (-not $versionMatch.Success) { throw '无法从 Common.cs 读取程序版本。' }
$version = $versionMatch.Groups['value'].Value
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) { throw '未找到 .NET Framework 4.8 C# 编译器。' }
foreach ($name in @('app.manifest', 'app.config', 'Features.json', 'assets/App.png', 'assets/App.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $src $name) -PathType Leaf)) { throw ('缺少编译输入：' + $name) }
}
$files = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
$output = Join-Path $Destination ("狂热运输3修改器-v$version.exe")
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    ('/win32manifest:' + (Join-Path $src 'app.manifest')) `
    ('/win32icon:' + (Join-Path $src 'assets/App.ico')) ('/out:' + $output) `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    ('/resource:' + (Join-Path $src 'Features.json') + ',TransportFever3Trainer.Features.json') `
    ('/resource:' + (Join-Path $src 'assets/App.png') + ',TransportFever3Trainer.App.png') `
    ('/resource:' + (Join-Path $src 'assets/App.ico') + ',TransportFever3Trainer.App.ico') $files
if ($LASTEXITCODE -ne 0) { throw '主程序编译失败。' }
Copy-Item -LiteralPath (Join-Path $src 'app.config') -Destination ($output + '.config')
