# Tcursor v3.8 发行说明（正式版）

## 概述

Tcursor v3.8 是一次针对 **文件资源管理器鼠标漂移跟随与 Chrome 网页非输入区误弹提示** 的专项架构根治版本。彻底解决了在 Windows 文件资源管理器（地址栏/搜索框/文件列表）中提示符号跟随鼠标指针移动的异常现象，以及在 Google Chrome 中未将光标点击放入文本框时也会异常出现提示符号的 Bug。

---

## 核心改进与深层技术根因修复

### 1. 彻底解决文件管理器提示符号随鼠标移动 Bug（根除假光标鼠标坐标合成漏洞）
- **深层技术根因**：
  此前版本在 `TryCaretFromMousePoint` 中引入了基于鼠标物理 X 坐标的兜底合成逻辑（`clampedX = mousePt.X`）。在 Windows 文件资源管理器（现代 DirectUI / XAML 地址栏、搜索框、重命名框及列表视图）中，控件虽然承载了 `ITextPattern` 接口，但底层未对字符范围暴露像素级包围盒；当用户鼠标掠过或悬停在控件包围盒范围内时，`text.RangeFromPoint(mousePt)` 成功返回文本区间但无几何边界，直接触发了该兜底合成，将**鼠标当前瞬时物理 X 坐标**强行伪造成了文字插入点光标，导致用户只要在控件内移动鼠标，提示符号便完全跟着鼠标指针游走，严重偏离了文本输入框的真实文字光标。
- **坚决杜绝鼠标指针伪造光标，恢复纯正无障碍几何提取**：
  彻底移除 `TryCaretFromMousePoint` 中对鼠标 X 坐标的伪造推算分支，严格规定仅在 UIA 接口通过 `TryRectOf` 或 `TryCaretViaExpandUnit` 真实返回字符屏幕几何矩形时才予以采纳；在未暴露字符几何时，平滑回退至可靠的控件边界几何锚点（`TryCaretFromEditBounds`），彻底杜绝提示符号跟随鼠标指针漂移的严重缺陷。

### 2. 彻底解决 Chrome 浏览网页未点击输入框出现提示符号 Bug（网页非编辑元素拦截与 MSAA 死锁熔断）
- **深层技术根因**：
  1. **Chromium 网页非编辑文本暴露 TextPattern 选区**：在 Chrome / Chromium 浏览器中，网页普通正文（段落 `<p>`、标题 `<h1>`、链接 `<a>`、通用容器 `<div>` 等）同样实现了 `ITextPattern`；当用户在浏览网页中点击文章正文、链接或空白处时，Chromium 会在被点击的非编辑元素上生成零长度折叠选区，由于此前版本未限定网页元素必须属于编辑类型，导致这些纯阅读文本被误当作可编辑输入框并提取了字符矩形，在未点击任何输入框的情况下弹出了提示符号；
  2. **MSAA 零宽伪光标穿透引发捕获链死锁**：此前版本在 `MsaaCaretCapture` 中放宽了宽度判定（允许 `width == 0`）。Chrome 顶层窗口（`Chrome_WidgetWin_1`）的 `AXSystemCaretWin` 在整个浏览器运行期间始终维护着一个内部光标矩形（未处于打字状态时报告 `width = 0, height = 20`）；`width == 0` 的穿透导致 `MsaaCaretCapture` 恒返回 `true`，使 `CaptureChain` 的 `_lastGood` 缓存被错误锁死在 MSAA 后端，即使 UIA 正确拦截了页面文档，后续帧也会被 MSAA 强制读取旧光标坐标而导致符号常驻不消。
- **网页内容宿主物理隔离与非编辑元素绝对拦截**：
  在 `UiaCaretCapture.TryElementCaret` 入口处建立严格的网页元素鉴权规则：凡处于 Chromium 渲染宿主窗口类（`Chrome_RenderWidgetHostHWND`）之下的元素，**必须明确属于可编辑控件（`element.ControlType == ControlType.Edit`，涵盖 `<input>`、`<textarea>` 以及带编辑角色的 `contenteditable` 容器）**；任何网页正文段落、标题、链接、普通容器或空白区域一律绝对拦截并返回 `false`，坚决禁止其产生打字光标；
- **MSAA 零宽光标过滤与 Chrome 网址栏专属精准调度**：
  在 `MsaaCaretCapture` 中严格恢复正宽度（`width > 0` 与 `height > 0`）有效性校验，杜绝 `(0,0,0,0)` 及零宽假光标污染捕获链；同时将 `TryMsaaCaret` 严格限制仅在控件非网页元素、明确属于 Chrome 网址栏（`OmniboxViewViews`）、拥有键盘输入焦点且选区为折叠状态时才精准调用，彻底消除误触发。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证微信聊天框、Windows 记事本、EmEditor、Windows 资源管理器等通用应用的高精度定位保持 100% 正常。
- **界面外观绝对冻结**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。
- **代码自检完备性**：`--selftest` 验证全部后端与分层窗口渲染测试均输出 `RESULT: OK`。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 614 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：628,680 字节（约 614 KB）
- **zip sha256**：`E6B752CFDB20EF852CB1D13DC891F4EBCDCEDF2AEF5673B34168530CC5B7F1B9`
- **exe sha256**：`DABA3DCCB5263CECB62D5ECB67E85ED0ED88746816B5FD5C056076EFC9859F46`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
