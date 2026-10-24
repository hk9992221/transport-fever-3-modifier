# Transport Fever 3 Modifier

《狂热运输3》Windows x64 修改器，当前版本 **v1.0.5**，通信协议 **v6**。桌面界面使用 C# / Windows Forms，游戏内运行模块使用 C 和 MinHook，功能由 Lua 脚本实现。

## 下载 Windows 便携包

本仓库只在**手动操作**时构建。上传代码、创建分支、提交 PR 或标签都不会触发打包。

1. 打开仓库的 **Actions** 页面。
2. 选择 **Windows x64 手动打包**。
3. 点击 **Run workflow**，选择分支并再次点击 **Run workflow**。
4. 构建成功后，在该次运行的 **Artifacts** 中下载 `TransportFever3Modifier-Windows-x64`。
5. 解开下载文件，再解开其中的便携 ZIP；完整保留程序与“运行模块”的相对位置。

产物包括便携 ZIP 和 `.sha256` 校验文件，保留 **7 天**。不会自动创建 GitHub Release，也不会在仓库中提交 EXE、DLL 或个人设置。首次运行会在本机创建“配置”目录。

工作流使用 `windows-2022`、系统 .NET Framework C# 编译器和固定版本 Zig 0.14.1；下载 Zig 时核对 SHA-256。构建仅检查编译、文件版本、x64 架构、脚本一致性和压缩包完整性，不会运行游戏或自动修改存档。

## 使用

需要 Windows 10/11 x64 和 .NET Framework 4.x。先打开游戏并进入地图，再运行 `狂热运输3修改器-v1.0.5.exe`。游戏和修改器使用相同权限；更新运行模块后应完全重启游戏。

功能包括公司财务、产业生产、载具参数、日历与模拟速度等 19 项常用功能，以及货物添加、城镇与产业管理、镜头定位和镜头书签。详细说明见 [使用说明](docs/使用说明.txt)。

资金、经验、日期、货物和土地容量等一次性修改会影响存档；关闭持续功能不会撤销这些操作。

## 本地构建

安装 Zig 0.14.1，将其加入 PATH，或通过 `-Compiler` 指定 `zig.exe`。在 Windows PowerShell 或 PowerShell 7 中，从仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1
```

显式指定编译器及新的输出目录：

```powershell
.\scripts\build-windows.ps1 -Compiler 'C:\Tools\zig\zig.exe' -OutputDirectory '.\dist-local'
```

默认产物在 `dist` 中。已有同名 ZIP 时，先移走旧产物，或通过 `-OutputDirectory` 指定新目录。

## 文件范围

- `src/`：C#、C、Lua、资源、MinHook 源码和底层构建脚本。
- `scripts/build-windows.ps1`：统一构建、校验及便携打包入口。
- `.github/workflows/windows-package.yml`：仅由 `workflow_dispatch` 触发的 Windows 工作流。
- `docs/`：使用说明及第三方说明。

从交付包导入时保留编译必需文件；未导入 AKE 会话记录、日志、个人配置、旧产物发布清单及绑定旧哈希的发布计划。便携包根据每次实际构建重新生成“发布清单.json”。

第三方 MinHook 许可保存在 [src/minhook/LICENSE.txt](src/minhook/LICENSE.txt)，并随便携包分发。原有项目与品牌信息保留。
