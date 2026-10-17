# Tcursor 2.7 正式版 发布说明

> **Tcursor（输入法状态光标提示）** 是一款在文本输入光标旁实时显示输入法状态符号的 Windows 绿色轻量辅助工具。支持中文、英文、大写锁定、英文键盘、日文、韩文六种状态，离线纯粹运行。

---

### 一、版本更新概述（Version 2.7 Overview）

**Tcursor 2.7 正式版** 是一次针对 **文件管理器（Explorer）等非输入场景光标虚假定位、以及托盘提权重启可靠性** 的专项修复与加固更新：

1. **根除文件管理器左上角/无输入框处虚假光标误判**：
   - **UIA 键盘焦点（HasKeyboardFocus）严格前置校验**：重构 `CaptureCore` 与 `TryCaretFromEditBounds`，彻底移除对未获焦点编辑框的盲目后代遍历。只有当前**明确拥有键盘焦点（`HasKeyboardFocus == true`）**的输入控件才允许几何推算；文件管理器在浏览普通文件夹时，其未激活的地址栏与搜索框绝对不再被误报为光标位置；
   - **智能兜底（Fallback）控件类名严格鉴权**：在非工字梁（`IDC_IBEAM`）鼠标指针下，严格检查 `gti.hwndFocus` 的窗口类名，只对标准 `Edit`/`RichEdit`/`TextBox` 类生效，彻底杜绝将文件管理器列表视图（`DirectUIHWND`/`SysListView32`）、工具栏按钮误判为输入光标；
   - **无缝保留 Quicker 与重命名/地址栏输入**：Quicker 在激活输入或文件管理器处于 F2 重命名/点击地址栏输入时，均拥有完整的键盘焦点与真实文本范围，所有 WPF 邻域推算依然 100% 精准响应。
2. **彻底解决「以管理员身份重启」死锁闪退问题**：
   - 托盘右键提权重启时，通过 `--wait-pid <pid>` 参数传递原进程 PID，使新管理员进程平滑等待原进程完全退出；
   - 在主程序单实例互斥锁（SingleInstance Mutex）检测中引入 1 秒安全宽限等待，完美解决新旧进程在纳秒级交替时的互斥体竞争，确保提权重启 100% 稳定唤起。
3. **界面外观绝对冻结（零变动）**：
   - 严格维持 960×680 画幅、100px 实时独立预览视口、全局圆角体系与三标签页纯净布局不变。

发布继续全面聚焦**超轻量小压缩包**分发：单文件约 1.25 MB，压缩包仅约 **608 KB**，启动时直接借用系统已安装的官方 .NET 10 运行时，享受 Windows Update 自动提速优化与补丁，零系统残留纯便携。

---

### 二、核心修复与改进详解

#### 1. 文件管理器与非输入界面虚假光标彻底清除
- **严格焦点前置**：在 `UiaCaretCapture` 中，移除了旧版本对窗口下所有 `ControlType.Edit` 的后代盲目回退遍历；`TryCaretFromEditBounds` 严格要求 `edit.Properties.HasKeyboardFocus.ValueOrDefault == true`；
- **智能兜底窗口类名过滤**：在 `CaptureChain.TryFallbackCaret` 中，针对非工字光标的窗口进行了 Win32 类名白名单过滤（仅匹配 Edit、RichEdit、TextBox 等明确编辑类），杜绝列表控件、树形控件、按钮被误当作单行输入框。

#### 2. 托盘「以管理员身份重启」平滑无缝
- **新旧进程优雅交接**：子进程启动后若检测到 `--wait-pid`，自动调用 `WaitForExit(5000)` 等待父进程彻底释放资源；
- **Mutex 宽限等待**：即使父进程退出略有延迟，子进程最多在互斥锁上等待 1 秒并安全捕获 `AbandonedMutexException` 接管互斥体，不再瞬间闪退退出。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 608 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：623,459 字节（约 608 KB）
- **zip sha256**：`224D7623BE94C77157929989FB2245179243946BF8C8120D5661A5BA17B6AED7`
- **exe sha256**：`84F0DEC07FFF46B9D05FC687AA5E997424F730862D67231CDE294D08F6A0C0B9`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
- 分发包内所包含的第三方开源组件（FlaUI.UIA3 等，均为 MIT 协议）详见 `THIRD-PARTY-NOTICES.txt`。
