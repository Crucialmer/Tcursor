# Tcursor（输入法状态光标提示）

在光标旁显示**输入法状态符号**的 Windows 小工具：中文 / 英文 / 大写 / 英文键盘 / 日文 / 韩文六种状态，颜色与形状均可自定义。采用 C#/.NET（WinForms）实现、自包含单文件，免安装运行库；当前版本 **1.0**；源码与构建说明见 [`dotnet/`](dotnet/README.md)。

## 下载与使用

1. 从 [Releases](https://github.com/Crucialmer/Tcursor/releases) 下载 `Tcursor.zip`（自包含单文件，约 46 MB，无需安装任何运行库）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（托盘常驻）；
3. 右键托盘图标 →“窗口信息”确认目标程序信息，并在“白名单规则”中添加要生效的程序（**白名单为空时不显示任何符号**）；
4. 在“光标符号设置”中调整颜色 / 尺寸 / 圆角 / 边框样式等外观。

> 配置位于 `%LOCALAPPDATA%\Tcursor\config.json`；在程序同目录放置 `portable.txt` 可切换为便携模式。
> 升级自早前版本时，首次启动会自动沿用原有设置。

## 界面一览

![设置界面](docs/settings.png)

![形状与圆角档位示例](docs/shapes.png)

深色模式（跟随系统）：![深色设置界面](docs/settings-dark.png)

## 特性速览

- **六种状态检测**：中文 `CN` · 英文 `EN` · 大写 `Caps` · 英文键盘 `US` · 日文 `JP` · 韩文 `KR`（CapsLock 优先；中文布局查询 IME 打开状态）
- **光标符号**：颜色 / 宽高 / 透明度 / 全局偏移；圆角 7 档（Apple 连续曲率）；5 种边框样式；逐像素透明、点击穿透、不抢焦点
- **白名单规则**：进程名 / 窗口标题 / 类名（支持正则），命中任一启用规则才显示
- **捕获链**：`GUI → UIA → MSAA` 逐级回退，覆盖 Chromium 系浏览器（输入框 / 内容可编辑区）与 Windows 11 资源管理器（重命名 / 地址栏 / 搜索框）等场景；只在真实输入位置显示，非输入区域（网页正文 / 空白处）不显示，离开输入框立即消失
- **高 DPI 与主题**：全部窗口随系统 DPI 缩放（最高 500% / 8K 可容纳）；macOS 风格无边框界面；深浅色跟随系统
- **长时运行**：捕获链缓存有上限、日志按大小轮转；显示层在更新失败或显示器拓扑变化后自动重建
- **托盘常驻**：暂停 / 恢复、设置、规则管理、窗口信息、打开配置文件 / 数据目录、开机自启

## 目录结构

- `dotnet/Tcursor/` — 应用源码（.NET 10 + WinForms）
- `dotnet/README.md` — 构建、运行与发布说明
- `docs/` — 界面截图
- `THIRD-PARTY-NOTICES.txt` — 第三方组件许可说明

## 许可

以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布（© 2026 Crucialmer），见 [LICENSE](LICENSE)；第三方组件许可说明见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
