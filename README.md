<div align="center">

# CCF Deadline Island

在 Windows 桌面顶部查看 CCF 会议截止时间。

![Windows 10 / 11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
[![Build Windows island](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/actions/workflows/build-island.yml/badge.svg)](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/actions/workflows/build-island.yml)

[下载 Windows x64 安装包 · 0.1.6](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/releases/download/island-v0.1.6/CCF-Deadline-Island_0.1.6_x64-setup.exe) ·
[发行记录](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/releases/tag/island-v0.1.6) ·
[反馈问题](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/issues/new)

</div>

![CCF Deadline Island 操作演示](docs/demo.gif)

## 功能

- **顶部灵动岛**：固定在主显示器顶部居中并持续置顶；悬停展开，点击固定，`Esc` 收起。
- **会议与详情**：主界面显示最近三场会议，**All** 查看全部；点击会议查看摘要、投稿截止及本地／源时区时间，**Website** 打开官网。
- **筛选与设置**：齿轮内按 CCF 等级、领域和时间范围筛选；仅投稿与开机启动使用右侧动画开关，选择即时保存。
- **关注与颜色**：**Follow / Following** 关注或取消关注，状态会保存；关注会议的倒计时为绿色，未关注且 24 小时内截止时为红色。
- **复制反馈**：**Copy** 复制截止时间；成功后短暂显示勾与 **Copied**，约 1.5 秒后恢复。
- **缓存与刷新**：每十分钟刷新，支持镜像回退及离线缓存；刷新失败保留已有数据。按 `F5` 或使用托盘菜单手动刷新。
- **Windows 集成**：`Ctrl+Alt+D` 展开／收起，`Tab`、空格和 `Enter` 操作控件；可为当前 Windows 用户开启登录启动。

## 安装

下载 [0.1.6 Windows x64 自包含安装包](https://github.com/Gerard-Devlin/ccf-ddl-win-bar/releases/download/island-v0.1.6/CCF-Deadline-Island_0.1.6_x64-setup.exe)。更新前先从托盘退出旧版，再运行安装程序。

支持 Windows 10 / 11 x64，自包含安装包**无需另装 .NET**。应用安装在 `%LOCALAPPDATA%\Programs\CCF Deadline Island`；开始菜单和托盘菜单均可打开。卸载保留 `%LOCALAPPDATA%\CCFDeadlineIsland` 下的偏好与缓存。

## 源码与构建

项目源码位于 [windows-island](windows-island/)。开发需要 Windows 和 .NET 8 SDK；生成安装包还需要 NSIS 3。从仓库根目录运行：

```powershell
# 启动开发版本
dotnet run --project windows-island/CcfDeadlineIsland.csproj

# 构建 Windows x64 自包含安装包
./windows-island/Build.ps1

# 数据、持久化及定位检查
dotnet run --project windows-island/Tests/Island.Tests.csproj -c Release

# 生产渲染器与输入检查，不打开可见桌面窗口
dotnet 'windows-island/bin/Release/net8.0-windows/CCF Deadline Island.dll' --render-preview artifacts/ui-checks
```

安装包输出为 `artifacts/CCF-Deadline-Island_0.1.6_x64-setup.exe`。更多构建选项见 [项目说明](windows-island/README.md)，验收记录见 [QA-0.1.6](windows-island/docs/QA-0.1.6.md)。

## 数据、来源与许可

会议数据来自 [CCF Deadlines](https://github.com/ccfddl/ccf-deadlines)。界面改编自 Florian Butz 的 [DynamicWin](https://github.com/FlorianButz/DynamicWin) 公开 `main` 源码，固定快照为 `9f24d176a16333b1cfda21dd3ed48087497194ed`，保留于 [Upstream/DynamicWin](windows-island/Upstream/DynamicWin/)。

本项目沿用 **CC BY-SA 4.0**，完整文本见 [LICENSE](LICENSE)。[Inter](https://github.com/rsms/inter) 字体使用 [SIL Open Font License 1.1](windows-island/licenses/Inter-OFL.txt)；其他字体和依赖保留各自许可，详见 [NOTICE](NOTICE.md) 与 [完整第三方声明](windows-island/NOTICE.md)。

本项目是非官方社区工具，与 CCF、CCF Deadlines 或 DynamicWin 作者无隶属关系。
