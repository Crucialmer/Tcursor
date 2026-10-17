# Tcursor 3.3 正式版 发布说明

> **Tcursor（输入法状态光标提示）** 是一款在文本输入光标旁实时显示输入法状态符号的 Windows 绿色轻量辅助工具。支持中文、英文、大写锁定、英文键盘、日文、韩文六种状态，离线纯粹运行。

---

### 一、版本更新概述（Version 3.3 Overview）

**Tcursor 3.3 正式版** 重点解决并彻底根治了用户在真实使用场景中反馈的两大痛点缺陷：
1. **「在 Chrome 中，就算没有将光标放入文本框，也会出现提示符号；以及在文本框中输入后，提示符号位置不动」**；
2. **「在微信中，光标提示符号偏下」**。

经过深入的 UI 自动化追踪分析、Chromium 根文档（`Chrome_RenderWidgetHostHWND`）生命周期逆向排查与即时通讯软件文本基准线测量，彻底根治了两处缺陷的深层根因：

1. **彻底修复 Chrome 网页未入输入框误弹提示与输入时光标不动 Bug（核心根因根治）**：
   - **根文档可编辑假判溯源**：在 Chrome / Edge 等 Chromium 浏览器中，当用户未聚焦输入框或刚点击网页空白处时，Chromium 的无障碍体系将全局焦点保留在网页根文档（`ControlType.Document`）。在此前版本中，为了支持 Win11 记事本等特殊富文本框，在 `IsEditableControl` 中纳入了 `ControlType.Document`；
   - **假光标死锁循环**：当焦点处于网页根文档时，`TryElementCaret` 经过选区判断后，直接穿透进入 `else if (IsEditableControl(element) && TryCaretFromEditBounds(...))`，将整个 Chrome 网页区域（如 1920×1080）当成了可编辑输入框，并在左上角（`bounds.Left + 6, bounds.Top + 4`）虚构出一个假光标。更严重的是，第一阶段由于返回了该假坐标，导致后续高精度的实时光标捕捉被阻断，用户在网页输入框打字时提示符号也被死锁在左上角不动；
   - **双重立体防御体系**：
     - 在 `UiaCaretCapture.TryElementCaret` 入口处设立绝对防护线：直接调用增强型 `IsChromiumPageRoot` 检测，凡属于 `Chrome_RenderWidgetHostHWND`、`Chrome_WidgetWin_` 及其父树的页面根文档直接返回 `false`，严禁产生假光标；
     - 恢复 `IsEditableControl` 纯正语义（仅限 `ControlType.Edit`），彻底杜绝浏览器整页背景或只读文档被推算为输入控件。
2. **彻底消除微信聊天框提示符号偏下（文本基线紧凑对齐）**：
   - **微信 14px 紧凑基准行高锚定**：微信桌面客户端默认聊天字体为 10.5pt（14px 微软雅黑）。此前统一设定的 22px 归一化基准行高对于微信 14px 文字而言，在文本下方产生了大约 8px 的多余间隙，产生明显的“视觉偏下”；
   - **即时通讯应用专属 14px 基准行高**：在 `CaretController` 中通过进程名、窗口类名及窗口标题全维度精准识别聊天应用（微信/QQ/钉钉/飞书等），将基准行高设为 `14px * scale`。提示符号瞬间紧贴文字基线，彻底根除悬空感。
3. **其他所有软件高精度定位零回归**：
   - 严格保持 EmEditor、Windows 资源管理器、记事本、Edge/Chrome 网页正规输入框的高精度光标捕捉与跟随。
4. **界面外观绝对冻结（零变动）**：
   - 严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

发布继续全面聚焦**超轻量小压缩包**分发：单文件仅约 1.26 MB，压缩包仅约 **610 KB**，启动时直接借用系统已安装的官方 .NET 10 运行时，享受 Windows Update 自动提速优化与补丁，零系统残留纯便携。

---

### 二、核心修复与改进详解

#### 1. Chromium 网页根文档生命周期拦截
- 当在 Chrome 浏览网页时，无障碍焦点处于网页根视图，新版直接识别该特征并判定为“非输入状态”，决不在网页空白处显示提示符号；
- 当用户点击网页中的 `<input>`、`<textarea>` 或 `contenteditable` 区域时，焦点精准落在子元素上，正常激活 TextPattern/TextPattern2 毫秒级光标捕捉，输入打字时光标实时平滑跟随。

#### 2. 微信等即时通讯工具文字基线精准贴合
- 对微信聊天窗口引入 `14px * scale` 专用基准行高，有效消除符号与文字底部之间的视觉间距，视觉观感浑然一体。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 610 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：624,879 字节（约 610 KB）
- **zip sha256**：`DD92E5FFA0269C6672B793B5B47E266CF16F9298FF77BED1A42BFA688BE621A6`
- **exe sha256**：`A8E9CEFCCD85C6D7D8131E0E76D44B5F2C7681B4C7BAD7ACB44AB7E1370FD567`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
- 分发包内所包含的第三方开源组件（FlaUI.UIA3 等，均为 MIT 协议）详见 `THIRD-PARTY-NOTICES.txt`。
