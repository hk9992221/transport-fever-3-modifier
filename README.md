# Transport Fever 3 Modifier

《狂热运输3》Windows x64 修改器，当前版本 **v1.1.5**，通信协议 **v6**。桌面界面使用 C# / Windows Forms，游戏内运行模块使用 C 和 MinHook，功能由 Lua 脚本实现。

v1.1.5 使用浅色界面和交通主题图标。主页面导航固定在左侧，连接与关闭功能按钮位于侧栏底部；右侧内容独立滚动。普通窗口可拖动边缘或角落自由缩放，内容比例保持 16:10；支持最大化和还原。最大化时内容等比居中，周围按屏幕比例留白。初始尺寸以 1280×800 为基准适配屏幕工作区域和 DPI。按钮按实际字体测量，说明文字换行并参与滚动，空间较小时自动切换布局。

货物添加按“选择货物 → 目标库存 → 每种数量 → 执行”操作，执行按钮固定在页面底部。搜索与切换筛选会保留勾选；地图切换时清除旧选择。刷新也会保留未提交的名称输入。

选好货物后点击“读取镜头并推荐”：工具主动读取游戏此刻的镜头中心和库存位置，再选择最近、接收全部所选货物且还有空位的仓库或产业，并显示平面距离。无需先到镜头页面读取；移动镜头后可再次点击。平时刷新保留手动目标，只有点击推荐才主动切换。读取失败仍可重试，界面分别说明镜头读取失败、建筑位置缺失或库存已满。

空间不足时自动按剩余容量添加，兼容的多个槽位会共同参与分配；已满库存不再添加。界面显示预计数量，游戏执行后显示实际数量。等待确认时仍可滚动详情、搜索、选择库存和调整下一次的数量；执行按钮等确认后恢复，避免重复提交。游戏显示 0x 时处于暂停，提交后请回到游戏继续运行。详情滚动位置不会被常规状态刷新重置。

建筑坐标会尝试多个经过验证的兼容接口；均不可用时仍可手动选择库存并添加，推荐失败不会阻断手动操作。具体接口错误记录在诊断页。镜头页面的“读取当前镜头”和“保存当前镜头”也会主动请求新数据。读取与修改能力分别验证，只有中心坐标时仍可推荐，完整参数可用时才允许保存和修改镜头。

游戏名称优先通过 `getEntityName` 读取，其次读取名称组件，缺省名称不再附加实体编号。游戏支持改名时可“保存到游戏”，并等待名称回读确认；无法读取游戏名时可设置“显示名称”，仅用于当前地图会话。显示名称不会覆盖可读的游戏名称，也不会跨游戏重启或其他地图套用。

## 从 Releases 下载单文件便携 EXE

打开仓库的 [Releases](https://github.com/hk9992221/transport-fever-3-modifier/releases)，下载 `TransportFever3Modifier-v1.1.5-windows-x64-portable.exe` 后直接运行，无需解压或安装。可同时下载 `.sha256` 校验文件。

主程序、DPI 配置、运行模块、Lua、使用说明与第三方许可均内嵌在这个 EXE 中，不需要另行下载 DLL。运行文件会释放到 `%LOCALAPPDATA%/TransportFever3Modifier/portable/` 下按资源内容区分的缓存目录；启动前核对 SHA-256。更新时不会覆盖游戏仍在使用的旧模块。

个人设置保存在便携 EXE 旁的 `配置/用户设置.json`，可随 EXE 一起移动；目录不可写时改用 `%LOCALAPPDATA%/TransportFever3Modifier/UserData/配置/`。更新时将新版 EXE 放在原目录即可继续使用设置。

## 手动构建并发布 Release

本仓库只在**手动操作**时构建和发布。上传代码、创建分支、提交 PR 或标签都不会触发工作流。

1. 打开仓库的 **Actions** 页面。
2. 选择 **Windows x64 手动发布**。
3. 点击 **Run workflow**，保持 `main` 分支并再次点击 **Run workflow**。
4. 构建成功后，工作流会按源码版本号自动创建标签及 Release，例如 `v1.1.5`，并上传单文件便携 EXE 和 `.sha256` 校验文件。
5. 在构建摘要中打开 Release 链接，或直接进入仓库的 **Releases** 下载。

发布文件保存在 Releases 中，不使用临时 Actions Artifacts 保存安装包。工作流使用仓库自动提供的 `GITHUB_TOKEN`，无需额外配置个人访问令牌。发布前先创建草稿并上传附件，完成后正式发布；同一提交重复运行可补传或更新附件。版本标签已绑定其他提交时会停止发布，需先升级版本；不会移动已有版本标签。

工作流使用 `windows-2022`、系统 .NET Framework C# 编译器和固定版本 Zig 0.14.1；下载 Zig 时核对 SHA-256。发布前会将便携 EXE 单独复制到含中文与空格的目录，运行离线布局检查，再核对自动释放的全部文件及 SHA-256。检查使用独立临时缓存与设置目录，不连接游戏。工作流名称和构建日志显示源码提交；测试新修复时请从 `main` 创建新的运行，重跑旧记录仍使用旧提交。

## 使用

需要 Windows 10/11 x64 和 .NET Framework 4.8。先打开游戏并进入地图，再运行下载的 `portable.exe`。游戏和修改器使用相同权限；更新运行模块后应完全重启游戏。

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

默认生成单文件便携 EXE 与校验文件。仅需展开的开发文件夹时：

```powershell
.\scripts\build-windows.ps1 -Compiler 'C:\Tools\zig\zig.exe' -OutputDirectory '.\dist-folder' -Folder
```

输出位于 `dist-folder/TransportFever3Modifier-v1.1.5-windows-x64/`，可直接运行其中的主程序。原有 `-SkipZip` 参数保留为 `-Folder` 的别名。GitHub Actions 默认发布单文件便携 EXE。

构建会输出当前阶段。离线检查通过环境标记和入口参数阻止游戏连接与模块注入。临时目录清理会短暂重试；仍有文件被占用时只单独告警，保留原始构建错误。能够读取进程模块时，会指出加载了临时 DLL 的游戏 PID。已被游戏加载的旧 DLL 需在正常退出游戏后才能删除。

默认产物在 `dist` 中。已有同名 EXE 时，先移走旧产物，或通过 `-OutputDirectory` 指定新目录。

## 文件范围

- `src/`：C#、C、Lua、资源、MinHook 源码和底层构建脚本。
- `scripts/build-windows.ps1`：统一构建、校验及便携打包入口。
- `packaging/`：单文件便携启动程序与内嵌资源构建脚本。
- `.github/workflows/windows-package.yml`：仅由 `workflow_dispatch` 触发的 Windows 构建及 Release 发布工作流。
- `docs/`：使用说明及第三方说明。

从交付包导入时保留编译必需文件；未导入 AKE 会话记录、日志、个人配置、旧产物发布清单及绑定旧哈希的发布计划。便携包根据每次实际构建重新生成“发布清单.json”。

第三方 MinHook 许可保存在 [src/minhook/LICENSE.txt](src/minhook/LICENSE.txt)，并随便携包分发。
