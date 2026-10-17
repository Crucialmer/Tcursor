# Tcursor v3.10 发行说明（正式版）

## 概述

Tcursor v3.10 是一次针对 **Chrome 浏览器刚打开或切换标签页时未输入状态下初始位置错误与延迟恢复** 以及 **通用软件中光标提示符号“一开始正常，过一会儿右移了”漂移缺陷** 的攻坚根治版本。彻底解决了 Chrome 刚打开或前后台切换时提示符号出现在旧标签页陈旧坐标或虚假偏移位置的问题，并排除了全局代码对光标右侧坐标的误篡改，使通用文本编辑器及办公软件在打字与停顿间保持 100% 稳定锚定在真实文字光标处。

---

## 核心改进与深层技术根因修复

### 1. 彻底根除 Chrome 刚打开或切换时未输入状态下初始位置错误与延迟恢复 Bug（Chromium MSAA 污染隔离与回退拦截）
- **深层技术根因**：
  1. **Chromium MSAA 顶层窗口光标陈旧废弃缓存污染**：Chromium 顶层窗口（`Chrome_WidgetWin_1`）实现了单例 `AXSystemCaretWin`（系统光标桥接器）。当 Chrome 刚打开或在标签页/窗口间切换时，UIA 跨进程连接瞬间因 IPC 调度产生微弱延迟（>150ms），捕获链降级到全局 `MsaaCaretCapture`；此前代码未过滤 Chromium 类名，全局 MSAA 立即读取了顶层窗口留存的**陈旧废弃光标坐标**，导致 `CaptureChain` 将 `_lastGood[chromePid]` 锁定为 `"MSAA"`。在后续多轮轮询中持续采纳该错误坐标，表现为未输入时提示符号出现在荒谬位置；直到用户“点击别处”使 Chromium 更新光标或“等一会儿”系统光标闪烁消失导致 MSAA 失败，捕获链才回退到 UIA 恢复正常；
  2. **UIA 步骤 3 网页正文焦点误回退搜索顶栏网址栏**：在 `UiaCaretCapture.CaptureCore` 步骤 3 中，当用户在前台查看 Chrome 网页正文时，虽然步骤 1 识别了网页非编辑元素并拦截，但步骤 3 仍向全窗口子树搜索 `HasKeyboardFocus == true` 的 Edit 控件，将处于休眠激活态的顶栏 `OmniboxViewViews` 搜出并调用 `TryCaretFromEditBounds`，导致在网页正文浏览时误在地址栏弹出提示符号；
  3. **网址输入框未输入虚假留白与全选 URL 穿透**：在 `UiaCaretCapture.TryCaretFromEditBounds` 中，针对 `isSearchOrAddress`（即 `OmniboxViewViews`），此前在未探测到内部子图标时硬编码了 `padX = 38 * scale`。然而 `OmniboxViewViews` 自身是真实文本编辑框，其边界已在锁头图标右侧，文本实际起始内边距仅为 6px 左右。38px 的虚构留白导致未输入/刚打开时提示符号悬空在第 3~4 个字母上方；此外，当切换到 Chrome 时 Chrome 往往全选 URL（`!IsCollapsed`），此前代码穿透 `IsCollapsed` 分支执行了 `TryCaretFromEditBounds`，在全选 URL 上强行渲染了一个 38px 偏移的符号；
  4. **空地址栏 MSAA 残留光标污染**：在 `UiaCaretCapture.TryElementCaret` 中，针对 Omnibox 调用 `TryMsaaCaret` 前未检查地址栏是否有文本内容。当新标签页地址栏为空（`chrome://newtab`）时，Chromium `AXSystemCaretWin` 仍残留上个网页的旧坐标，导致新开标签页地址栏为空时提示符号坐标失真。
- **全链路治理与精准适配**：
  1. 在 `MsaaCaretCapture` 中全面跳过 `Chrome_WidgetWin` 类名，坚决禁止全局 MSAA 读取 Chrome 窗口层级的陈旧废弃光标；
  2. 在 `UiaCaretCapture.CaptureCore` 中识别 `focusedIsChromiumNonEdit`，当焦点在网页非编辑区时直接返回 `false`，杜绝步骤 3 误抓取顶栏 Omnibox；
  3. 在 `UiaCaretCapture.TryElementCaret` 中将 `TryCaretFromEditBounds` 严格限制在 `IsCollapsed(selRange)` 纯折叠光标状态下，全选 URL 时坚决不虚构未输入光标；
  4. 在 `TryElementCaret` 中校验 Omnibox 必须包含有效文本才调用 `TryMsaaCaret`，杜绝空地址栏读取旧坐标；
  5. 将未探测到图标时的 `padX` 修正为真实单行文本内边距 `6 * scale`，使未输入、刚打开与光标闪烁基线 100% 严密贴合。

### 2. 彻底根治通用软件中光标提示符号“一开始正常，过一会儿右移了”漂移 Bug（移除全局 caret.Right 篡改）
- **深层技术根因**：
  1. 在 v3.9 中为了解决微信输入法输拼音横跳，在 `CaretController.cs` 中添加了全局逻辑：`int caretX = caret.Right > caret.Left + 1 ? caret.Right : caret.Left;`，并在 `UiaCaretCapture.cs` 的 `TryElementCaret` 中将 `caretX = selRect.Right > selRect.Left + 1 ? selRect.Right : selRect.Left` 强行写入返回矩形；
  2. 在其他所有通用软件中（Notepad、Word、VS Code、富文本编辑器等）：当用户光标处于字符间时，许多 UIA 接口返回的是光标所在单个字符的包围盒（宽度 8~14px），或者 Win32 粗光标本身宽度为 2px/3px；
  3. 当用户刚点击或刚打字时，`GUI` 后端先命中，报告单点光标（`caret.Left`），提示符号在字符左端（“一开始是正常的”）；
  4. 停顿片刻后，光标闪烁或 UIA 后端接管，UIA 返回了宽度 > 1 的字符包围盒，v3.9 的全局代码误将其当作组字串并强行锚定到 `caret.Right`，使符号从字符左端瞬移到字符右端（右移了 8~14 像素！），表现为“过一会发现右移了”。
- **组字串尾端隔离与通用光标基准坐标还原**：
  1. 微信输入法拼音防横跳的真实来源已在 `TsfCaretCapture.cs` 内部将组字串的有效插入点置于 `caret.Left = caretX = right`，宽度设为 1，完全由 TSF 内部闭环输出尾端；
  2. 彻底还原 `CaretController.cs` 中的 `x = caret.Left + offsetX - pad;` 与 `cx = caret.Left;`，彻底移除误导性的 `caret.Right` 全局篡改；
  3. 彻底还原 `UiaCaretCapture.cs` 中的 `caret = selRect;`，消除所有导致通用软件“过一会儿右移”的漂移根源。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证微信拼音输入尾端跟随不横跳、微信单行搜索框与多行聊天框垂直基线 100% 对齐、Chrome 网址栏与网页输入框、Windows 资源管理器、记事本等通用程序 100% 正常。
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
- **压缩包大小**：628,933 字节（约 614 KB）
- **zip sha256**：`B0521587280A8CFB33845C04CA8F0972ACD5E419D47D4C6996DBBCF8705ACC88`
- **exe sha256**：`1468D411A5149F26AD5430919A6763483C69D2D9C000080061E2D19FC24FD996`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
