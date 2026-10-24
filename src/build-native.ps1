param([string]$Compiler,[string]$Destination)
$ErrorActionPreference='Stop'
$src=$PSScriptRoot
if(-not $Compiler){$zig=Get-Command zig -ErrorAction SilentlyContinue;if($zig){$Compiler=$zig.Source}else{throw '未找到 Zig 编译器，请使用 -Compiler 指定本机 zig.exe 路径。'}}
if(-not(Test-Path -LiteralPath $Compiler -PathType Leaf)){throw '指定的 Zig 编译器不存在。'}
if(-not $Destination){$Destination=Join-Path (Split-Path $src -Parent) 'build/package/运行模块'}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$runtime=Join-Path $src 'Runtime.c'
& $Compiler cc -target x86_64-windows-gnu -shared -O2 -DUNICODE -D_UNICODE -I (Join-Path $src 'minhook/include') $runtime (Join-Path $src 'minhook/src/buffer.c') (Join-Path $src 'minhook/src/hook.c') (Join-Path $src 'minhook/src/trampoline.c') (Join-Path $src 'minhook/src/hde/hde64.c') -o (Join-Path $Destination 'TransportFever3Runtime_v6.dll') -luser32
if($LASTEXITCODE -ne 0){throw '运行模块编译失败'}
$lua=Join-Path $src '功能接口v6.lua'
Copy-Item -LiteralPath $lua -Destination $Destination
