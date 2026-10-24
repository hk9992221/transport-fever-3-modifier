param([string]$Destination, [string]$BrandSource='')
$ErrorActionPreference='Stop'
$src=$PSScriptRoot
if(-not $Destination){$Destination=Join-Path (Split-Path $src -Parent) 'build/package'}
$Destination=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
# 默认使用仓库中的品牌资源；只有显式指定时才重新生成图标。
if($BrandSource){
 Add-Type -AssemblyName System.Drawing
 $original=[Drawing.Image]::FromFile([IO.Path]::GetFullPath($BrandSource))
 $images=New-Object 'Collections.Generic.List[byte[]]'
 $sizes=@(16,32,48,64,128,256)
 try{
  foreach($size in $sizes){
   $bitmap=New-Object Drawing.Bitmap($size,$size)
   $graphics=[Drawing.Graphics]::FromImage($bitmap)
   $stream=New-Object IO.MemoryStream
   try{
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $ratio=[Math]::Min($size/[double]$original.Width,$size/[double]$original.Height)
    $width=[int][Math]::Round($original.Width*$ratio);$height=[int][Math]::Round($original.Height*$ratio)
    $graphics.DrawImage($original,[int](($size-$width)/2),[int](($size-$height)/2),$width,$height)
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    [void]$images.Add($stream.ToArray())
    if($size -eq 256){[IO.File]::WriteAllBytes((Join-Path $src 'assets/Brand.png'),$stream.ToArray())}
   }finally{$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
  }
 }finally{$original.Dispose()}
 $iconStream=New-Object IO.MemoryStream
 $writer=New-Object IO.BinaryWriter($iconStream)
 try{
  $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$sizes.Count)
  $offset=6+16*$sizes.Count
  for($i=0;$i -lt $sizes.Count;$i++){$s=$sizes[$i];$writer.Write([byte]$(if($s -eq 256){0}else{$s}));$writer.Write([byte]$(if($s -eq 256){0}else{$s}));$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$images[$i].Length);$writer.Write([UInt32]$offset);$offset+=$images[$i].Length}
  foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
  $writer.Flush();[IO.File]::WriteAllBytes((Join-Path $src 'assets/Brand.ico'),$iconStream.ToArray())
 }finally{$writer.Dispose();$iconStream.Dispose()}
}
$versionMatch=[regex]::Match([IO.File]::ReadAllText((Join-Path $src 'Common.cs')),'\bVersion\s*=\s*"(?<value>\d+\.\d+\.\d+)"')
if(-not $versionMatch.Success){throw '无法从 Common.cs 读取程序版本。'}
$version=$versionMatch.Groups['value'].Value
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not(Test-Path -LiteralPath $csc -PathType Leaf)){throw '未找到系统 .NET Framework 4.x 编译器。'}
$files=@('Common.cs','Bridge.cs','Theme.cs','ThemedNumericUpDown.cs','ThemedComboBox.cs','Appearance.cs','Branding.cs','ListScrolling.cs','Widgets.cs','Program.cs','AssemblyInfo.cs') | ForEach-Object {Join-Path $src $_}
foreach($file in $files){if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw ('缺少编译源码：'+$file)}}
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/win32manifest:'+ (Join-Path $src 'app.manifest')) ('/win32icon:'+ (Join-Path $src 'assets/Brand.ico')) ('/out:'+(Join-Path $Destination ("狂热运输3修改器-v"+$version+'.exe'))) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ('/resource:'+(Join-Path $src 'Features.json')+',TransportFever3Trainer.Features.json') ('/resource:'+(Join-Path $src 'assets/Brand.png')+',TransportFever3Trainer.Brand.png') ('/resource:'+(Join-Path $src 'assets/Brand.ico')+',TransportFever3Trainer.Brand.ico') $files
if($LASTEXITCODE -ne 0){throw '编译失败'}
