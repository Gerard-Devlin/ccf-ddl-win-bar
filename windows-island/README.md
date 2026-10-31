# CCF Deadline Island

Windows 灵动岛版 CCF 截止时间工具。界面直接使用
[DynamicWin](https://github.com/FlorianButz/DynamicWin) 的黑色外壳、Inter 字体、图标和弹簧动画，
以最近三场会议及截止时间为主界面。

![Upcoming conferences](docs/screenshots/upcoming.png)

0.1.2 移除底部状态/刷新整行，修复置顶和选中会议的收起显示，并补齐领域筛选与详情操作图标。
0.1.3 中，**All** 列表按实际会议条数收缩，最多显示五行，避免不足五场时留下空白；**Back** 和 **Reset filters** 左侧增加对应图标。
0.1.4 将筛选和开机启动开关统一收进齿轮设置，保留按钮动画并调整勾选间距。支持顶部/左右拖拽停靠；未关注且 24 小时内截止时显示红色，关注会议的倒计时显示绿色。
0.1.5 将左右侧的收起状态改为 **64×160 DIP** 竖向胶囊，会议名与两行倒计时保持正向；展开时向屏幕内侧伸展为正常面板。复制成功后，**Copy** 短暂变为勾选图标与 **Copied**，约 1.5 秒后恢复。
详情返回入口改为左箭头；设置页收紧到 **320×416 DIP**，仅投稿与开机启动使用右侧蓝／灰滑动开关，并保留整行点击、键盘操作和切换动画。
验收记录见 [QA-0.1.5](docs/QA-0.1.5.md)。

上游默认 `V2` 分支只提供发行文件。本项目使用其公开 `main` 分支的源码，固定在
`9f24d176a16333b1cfda21dd3ed48087497194ed`；63 个原始源码/资源文件及许可证位于
`Upstream/DynamicWin`，逐文件 SHA-256 见 `VENDOR-MANIFEST.json`。这不是对闭源 V2 的源码移植。

## 使用

- 悬停展开；点击固定。`Esc` 收起，`Ctrl+Alt+D` 展开/收起。
- 拖动面板空白处可停靠到 Windows 主显示器的顶部、左侧或右侧；顶部始终居中，侧边的垂直位置会保存，支持重启恢复。底部不作为停靠位置。
- 顶部收起时保持横向胶囊；左右侧收起时为竖向胶囊，会议名与倒计时上下排列。左侧向右展开，右侧向左展开，面板文字保持正常阅读方向。
- 显示器、DPI 或任务栏变化后，按当前停靠方式自动重算位置。
- 持续置顶，其他屏幕的全屏窗口不会让灵动岛自动隐藏；收起状态跟随所选会议。
- 点击会议行查看详情，详情中的 **Website** 打开官网。`F5` 刷新。
- `Tab` / `Shift+Tab` 切换控件，空格或 `Enter` 激活；筛选项与开关支持整行点击。
- 齿轮设置中统一管理等级、研究领域、时间范围和仅投稿筛选，选择即时保存；领域支持 **All / Clear**。
- 设置中的 **Submission deadlines only** 与 **Launch at startup** 使用右侧滑动开关；开机启动仅影响当前用户，**Reset** 仅重置筛选，动画始终开启。
- 详情中的 **Follow / Following** 可关注或取消关注；状态会保存，关注后的倒计时统一显示绿色。未关注且 24 小时以内截止的会议显示红色。
- 数据每十分钟刷新，支持镜像回退和离线缓存；摘要截止后自动切换到下一有效截止。
- 详情可复制时间；成功时显示勾选图标与 **Copied** 约 1.5 秒，失败时保留 **Copy**。按钮按下、松开和选中切换均有动画反馈。

安装包为 Windows x64 自包含版本，无需另装 .NET。安装到
`%LOCALAPPDATA%\Programs\CCF Deadline Island`，开始菜单中可打开应用；托盘菜单可显示、刷新或退出。
卸载保留 `%LOCALAPPDATA%\CCFDeadlineIsland` 下的设置和缓存。

## 开发与构建

需要 Windows、.NET 8 SDK，以及 NSIS 3（仅生成安装包时需要）。以下命令从仓库根目录运行：

```powershell
dotnet run --project windows-island/CcfDeadlineIsland.csproj
./windows-island/Build.ps1
```

脚本优先使用仓库内 `.tools/dotnet/dotnet.exe`，其次使用 PATH 上的 SDK。NSIS 可通过
`MAKENSIS` 指定，也会查找 PATH、Tauri 缓存和标准 NSIS 安装目录。构建不会启动应用或安装程序。

输出：

```text
artifacts/windows-island-0.1.5/
artifacts/CCF-Deadline-Island_0.1.5_x64-setup.exe
```

`./windows-island/Build.ps1 -PublishOnly` 只生成自包含应用。分发整个输出目录，以保留字体、图标和许可证文件。

## 检查

```powershell
dotnet run --project windows-island/Tests/Island.Tests.csproj -c Release
# 可选：额外检查实时 CCF 数据
dotnet run --project windows-island/Tests/Island.Tests.csproj -c Release -- --live
# 用生产控件离屏生成预览及交互检查结果，不打开桌面窗口
dotnet 'windows-island/bin/Release/net8.0-windows/CCF Deadline Island.dll' --render-preview artifacts/ui-checks
```

发布后的单文件 EXE 也支持 `--render-preview <输出目录>`。输出包括 `preview.png`、各界面 PNG、
`geometry.json` 和 `checks.txt`。界面检查通过 WPF 路由的按下/抬起事件验证命中与捕获，
并覆盖键盘、快速点击、隐藏控件和数据变化。数据测试不依赖桌面 UI；实时数据检查不写入正式缓存。

## 来源与许可

DynamicWin 由 Florian Butz 开发，使用 CC BY-SA 4.0。本子项目保留相同许可及来源声明；
详见 [LICENSE](LICENSE)、[NOTICE.md](NOTICE.md) 和 `licenses/`。会议数据来自
[CCF Deadlines](https://github.com/ccfddl/ccf-deadlines)。本项目与 DynamicWin、CCF 均无隶属关系。
