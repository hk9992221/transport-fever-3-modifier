# Transport Fever 3 Modifier

《狂热运输3》Windows x64 修改器，当前版本 **v1.1.2**，通信协议 **v6**。桌面界面使用 C# / Windows Forms，游戏内运行模块使用 C 和 MinHook，功能由 Lua 脚本实现。

v1.1.2 使用浅色界面和交通主题图标。主页面导航固定在左侧，连接与关闭功能按钮位于侧栏底部；右侧内容独立滚动。窗口按屏幕工作区域和 DPI 调整，功能卡片、目录和镜头页面会在窄窗口下切换布局，内容可滚动访问。

货物添加按“选择货物 → 目标库存 → 每种数量 → 执行”操作，执行按钮固定在页面底部。搜索与切换筛选会保留勾选；地图切换时清除旧选择。刷新也会保留未提交的名称输入。

目标库存优先推荐离当前镜头中心最近、接收全部所选货物且还有空位的仓库或产业，显示平面距离。移动镜头后可点“使用推荐”切换，手动选定的目标会保留。空间不足时自动按剩余容量添加，兼容的多个槽位会共同参与分配；已满库存不再添加。界面显示预计数量，游戏执行后显示实际数量。位置或镜头接口不可用时可手动选择库存。

游戏名称优先通过 `getEntityName` 读取，其次读取名称组件，缺省名称不再附加实体编号。游戏支持改名时可“保存到游戏”，并等待名称回读确认；无法读取游戏名时可设置“显示名称”，仅用于当前地图会话。显示名称不会覆盖可读的游戏名称，也不会跨游戏重启或其他地图套用。

## 从 Releases 下载 Windows 便携包

打开仓库的 [Releases](https://github.com/hk9992221/transport-fever-3-modifier/releases)，在对应版本的 Assets 中下载 `TransportFever3Modifier-v1.1.2-windows-x64.zip`，可同时下载 `.sha256` 校验文件。完整解压 ZIP，保留程序、同名 `.exe.config` 与“运行模块”的相对位置；首次运行会在本机创建“配置”目录。

## 手动构建并发布 Release

本仓库只在**手动操作**时构建和发布。上传代码、创建分支、提交 PR 或标签都不会触发工作流。

1. 打开仓库的 **Actions** 页面。
2. 选择 **Windows x64 手动发布**。
3. 点击 **Run workflow**，保持 `main` 分支并再次点击 **Run workflow**。
4. 构建成功后，工作流会按源码版本号自动创建标签及 Release，例如 `v1.1.2`，并上传便携 ZIP 和 `.sha256` 校验文件。
5. 在构建摘要中打开 Release 链接，或直接进入仓库的 **Releases** 下载。

发布文件保存在 Releases 中，不使用临时 Actions Artifacts 保存安装包。工作流使用仓库自动提供的 `GITHUB_TOKEN`，无需额外配置个人访问令牌。发布前先创建草稿并上传附件，完成后正式发布；同一提交重复运行可补传或更新附件。版本标签已绑定其他提交时会停止发布，需先升级版本；不会移动已有版本标签。

工作流使用 `windows-2022`、系统 .NET Framework C# 编译器和固定版本 Zig 0.14.1；下载 Zig 时核对 SHA-256。构建检查编译、文件版本、x64 架构、脚本一致性、离线布局和压缩包完整性。离线布局模式不会连接游戏。

## 使用

需要 Windows 10/11 x64 和 .NET Framework 4.8。先打开游戏并进入地图，再运行 `狂热运输3修改器-v1.1.2.exe`。游戏和修改器使用相同权限；更新运行模块后应完全重启游戏。

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

只生成可直接运行的文件夹，跳过 ZIP 压缩：

```powershell
.\scripts\build-windows.ps1 -Compiler 'C:\Tools\zig\zig.exe' -OutputDirectory '.\dist-folder' -SkipZip
```

输出位于 `dist-folder/TransportFever3Modifier-v1.1.2-windows-x64/`，可直接运行其中的主程序。GitHub Actions 默认仍生成 ZIP 并发布到 Releases。

构建会输出当前阶段。布局检查在单独目录运行，该目录只包含主程序与配置，并通过离线模式阻止游戏连接和模块注入。临时目录清理会短暂重试；仍有文件被占用时只单独告警，保留原始构建错误。能够读取进程模块时，会指出加载了临时 DLL 的游戏 PID。已被游戏加载的旧 DLL 需在正常退出游戏后才能删除。

默认产物在 `dist` 中。已有同名 ZIP 时，先移走旧产物，或通过 `-OutputDirectory` 指定新目录。

## 文件范围

- `src/`：C#、C、Lua、资源、MinHook 源码和底层构建脚本。
- `scripts/build-windows.ps1`：统一构建、校验及便携打包入口。
- `.github/workflows/windows-package.yml`：仅由 `workflow_dispatch` 触发的 Windows 构建及 Release 发布工作流。
- `docs/`：使用说明及第三方说明。

从交付包导入时保留编译必需文件；未导入 AKE 会话记录、日志、个人配置、旧产物发布清单及绑定旧哈希的发布计划。便携包根据每次实际构建重新生成“发布清单.json”。

第三方 MinHook 许可保存在 [src/minhook/LICENSE.txt](src/minhook/LICENSE.txt)，并随便携包分发。
