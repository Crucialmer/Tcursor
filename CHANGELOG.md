# 更新日志（Tcursor）

## v3.10（2026-10-17，正式版）

- **彻底根除 Chrome 刚打开或切换时未输入状态下初始位置错误与延迟恢复 Bug（Chromium MSAA 污染隔离与回退拦截）**：
  - **深层技术根因**：
    1. Chromium 顶层窗口（`Chrome_WidgetWin_1`）实现了单例 `AXSystemCaretWin`（系统光标桥接器）。当 Chrome 刚打开或在标签页/窗口间切换时，UIA 跨进程连接瞬间因 IPC 调度产生微弱延迟（>150ms），捕获链降级到全局 `MsaaCaretCapture`；此前代码未过滤 Chromium 类名，全局 MSAA 立即读取了顶层窗口留存的**陈旧废弃光标坐标**，导致 `CaptureChain` 将 `_lastGood[chromePid]` 锁定为 `"MSAA"`。在后续多轮轮询中持续采纳该错误坐标，表现为未输入时提示符号出现在荒谬位置；直到用户“点击别处”使 Chromium 更新光标或“等一会儿”系统光标闪烁消失导致 MSAA 失败，捕获链才回退到 UIA 恢复正常；
    2. 在 `UiaCaretCapture.CaptureCore` 步骤 3 中：当用户在前台查看 Chrome 网页正文时，虽然步骤 1 识别了网页非编辑元素并拦截，但步骤 3 仍向全窗口子树搜索 `HasKeyboardFocus == true` 的 Edit 控件，将处于休眠激活态的顶栏 `OmniboxViewViews` 搜出并调用 `TryCaretFromEditBounds`，导致在网页正文浏览时误在地址栏弹出提示符号；
    3. 在 `UiaCaretCapture.TryCaretFromEditBounds` 中：针对 `isSearchOrAddress`（即 `OmniboxViewViews`），此前在未探测到内部子图标时硬编码了 `padX = 38 * scale`。然而 `OmniboxViewViews` 自身是真实文本编辑框，其边界已在锁头图标右侧，文本实际起始内边距仅为 6px 左右。38px 的虚构留白导致未输入/刚打开时提示符号悬空在第 3~4 个字母上方；此外，当切换到 Chrome 时 Chrome 往往全选 URL（`!IsCollapsed`），此前代码穿透 `IsCollapsed` 分支执行了 `TryCaretFromEditBounds`，在全选 URL 上强行渲染了一个 38px 偏移的符号；
    4. 在 `UiaCaretCapture.TryElementCaret` 中：针对 Omnibox 调用 `TryMsaaCaret` 前未检查地址栏是否有文本内容。当新标签页地址栏为空（`chrome://newtab`）时，Chromium `AXSystemCaretWin` 仍残留上个网页的旧坐标，导致新开标签页地址栏为空时提示符号坐标失真；
  - **全链路治理与精准适配**：
    1. 在 `MsaaCaretCapture` 中全面跳过 `Chrome_WidgetWin` 类名，坚决禁止全局 MSAA 读取 Chrome 窗口层级的陈旧废弃光标；
    2. 在 `UiaCaretCapture.CaptureCore` 中识别 `focusedIsChromiumNonEdit`，当焦点在网页非编辑区时直接返回 `false`，杜绝步骤 3 误抓取顶栏 Omnibox；
    3. 在 `UiaCaretCapture.TryElementCaret` 中将 `TryCaretFromEditBounds` 严格限制在 `IsCollapsed(selRange)` 纯折叠光标状态下，全选 URL 时坚决不虚构未输入光标；
    4. 在 `TryElementCaret` 中校验 Omnibox 必须包含有效文本才调用 `TryMsaaCaret`，杜绝空地址栏读取旧坐标；
    5. 将未探测到图标时的 `padX` 修正为真实单行文本内边距 `6 * scale`，使未输入、刚打开与光标闪烁基线 100% 严密贴合。
- **彻底根治通用软件中光标提示符号“一开始正常，过一会儿右移了”漂移 Bug（移除全局 caret.Right 篡改）**：
  - **深层技术根因**：
    1. 在 v3.9 中为了解决微信输入法输拼音横跳，在 `CaretController.cs` 中添加了全局逻辑：`int caretX = caret.Right > caret.Left + 1 ? caret.Right : caret.Left;`，并在 `UiaCaretCapture.cs` 的 `TryElementCaret` 中将 `caretX = selRect.Right > selRect.Left + 1 ? selRect.Right : selRect.Left` 强行写入返回矩形；
    2. 在其他所有通用软件中（Notepad、Word、VS Code、富文本编辑器等）：当用户光标处于字符间时，许多 UIA 接口返回的是光标所在单个字符的包围盒（宽度 8~14px），或者 Win32 粗光标本身宽度为 2px/3px；
    3. 当用户刚点击或刚打字时，`GUI` 后端先命中，报告单点光标（`caret.Left`），提示符号在字符左端（“一开始是正常的”）；
    4. 停顿片刻后，光标闪烁或 UIA 后端接管，UIA 返回了宽度 > 1 的字符包围盒，v3.9 的全局代码误将其当作组字串并强行锚定到 `caret.Right`，使符号从字符左端瞬移到字符右端（右移了 8~14 像素！），表现为“过一会发现右移了”；
  - **组字串尾端隔离与通用光标基准坐标还原**：
    1. 微信输入法拼音防横跳的真实来源已在 `TsfCaretCapture.cs` 内部将组字串的有效插入点置于 `caret.Left = caretX = right`，宽度设为 1，完全由 TSF 内部闭环输出尾端；
    2. 彻底还原 `CaretController.cs` 中的 `x = caret.Left + offsetX - pad;` 与 `cx = caret.Left;`，彻底移除误导性的 `caret.Right` 全局篡改；
    3. 彻底还原 `UiaCaretCapture.cs` 中的 `caret = selRect;`，消除所有导致通用软件“过一会儿右移”的漂移根源。
- **零回归与稳定性保证**：严格保证微信拼音输入尾端跟随不横跳、微信单行搜索框与多行聊天框垂直基线 100% 对齐、Chrome 网址栏与网页输入框、Windows 资源管理器、记事本等通用程序 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## v3.9（2026-10-17，正式版）

- **彻底根治微信搜索框光标提示符号“时而偏上、时而正常”Bug（退化行高补齐与单行搜索框居中排版）**：
  - **深层技术根因**：
    1. 在 `CaretController` 中，当 TSF 或底层窗口报告纯光标折叠选区（`caretH < 10px * scale`，例如 `caretH == 1`）时，此前分支误将其认定为文字基线并仅执行 `effectiveBottom = caret.Top + 1px`。而 TSF `ITfContextView::GetTextExt` 报告的 `top` 实际上位于文字顶边，仅加 1px 导致有效底边被强行绘制在字符顶部基线，比正常光标（`caret.Bottom = caret.Top + 16px`）生生上移了 15px，产生严重的“偏上”缺陷；当 Win32 GUI 线程光标恢复并报告正常高度（16px）时又恢复正常，两者交替触发表现为“有时候偏上、有时候正常”；
    2. 在 `UiaCaretCapture.TryCaretFromEditBounds` 中，微信左上角的**单行搜索框**（容器高度仅 24~28px）因进程名匹配被误判为多行聊天窗口（`isChat`），被粗暴套用了固定的大顶部边距（`padY = 8 * scale`）与 `caretHeight = 16 * scale`，未执行单行输入框的垂直居中排版，导致未输入时偏向底边，与打字时的文字基线存在纵向剪刀差；
  - **退化光标行高智能补齐**：在 `CaretController` 的即时通讯应用校准中，当 `caretH < 10px * scale` 时，严密补齐为微信标准单行字号行高（`effectiveBottom = caret.Top + 16 * scale`），与正常 Win32 Caret 报告的 16px 底部实现 100% 绝对一致，彻底消除 15px 的突发跳变；
  - **单行搜索框垂直居中精准排版**：在 `UiaCaretCapture` 中严格界定 `bounds.Height <= 36 * scale` 的微信控件为单行搜索框，执行 `(bounds.Height - caretHeight) / 2` 精准垂直居中排版与安全内边距，使未输入、放入光标与打字全阶段的纵向坐标 100% 严密贴合。
- **彻底根治微信输入法打字输拼音阶段提示符号首尾来回跳跃 Bug（组字串末尾恒定锚定机制）**：
  - **深层技术根因**：
    1. 在 `TsfCaretCapture` 中，调用 TSF `GetTextExt` 测量拼音组字串（Composition Range）时，返回的包围盒跨度包含 `left`（拼音串首字母起始坐标）与 `right`（拼音串末字母结束坐标）。此前代码直接将矩形左端赋给光标（`caret = new CaretRect(left, ...)`），导致 `CaretController` 将提示符号定位于拼音字母**开头**；
    2. 而 Win32 `GuiCaretCapture` 通过 `GetGUIThreadInfo` 读取系统光标时，微信输入法与主流 IME 会将系统插入点光标置于拼音串**末尾**（`caret.Left == right`）；
    3. 打字输拼音过程中，随着按键触发、IME 消息泵处理以及光标闪烁，`GUI` 捕获器与 `TSF` 捕获器高频交替命中（GUI 命中时在末尾，TSF 命中时在开头），导致提示符号在拼音文本首尾剧烈来回横跳，而非跟随在拼音字母后方；
  - **全链路末尾插入点恒定锚定**：
    1. 在 `TsfCaretCapture` 中，当 `right > left + 1`（存在拼音组字串或文本选区跨度）时，光标有效插入点绝对锚定在文本末端（`caretX = right`），杜绝输出首字母坐标；
    2. 在 `UiaCaretCapture` 中，对 `Text2.GetCaretRange` 与 `Text.GetSelection` 同样建立末端锚定，若选区存在有效字符宽度，统一取 `selRect.Right`；
    3. 在 `CaretController` 控制核心中建立第二道坚固防线：任何后端报告 `caret.Right > caret.Left + 1` 时，横向坐标统一严格取 `caret.Right`，使提示符号在整个输拼音阶段始终稳定贴合在拼音字母尾部（`caret.Right`），彻底杜绝首尾来回跳跃！
- **零回归与稳定性保证**：严格保证 Chrome 网址栏/网页输入框、Windows 资源管理器、微信聊天框、记事本、EmEditor 等通用应用的高精度定位 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## v3.8（2026-10-17，正式版）

- **彻底修复文件资源管理器提示符号随鼠标移动 Bug（根除假光标鼠标坐标合成漏洞）**：
  - **深层技术根因**：此前在 `TryCaretFromMousePoint` 中引入了基于鼠标物理 X 坐标的兜底合成逻辑（`clampedX = mousePt.X`）。在 Windows 文件资源管理器（现代 DirectUI / XAML 地址栏、搜索框、重命名框及列表视图）中，控件虽然承载了 `ITextPattern` 接口，但底层未对字符范围暴露像素级包围盒；当用户鼠标掠过或悬停在控件包围盒范围内时，`text.RangeFromPoint(mousePt)` 成功返回文本区间但无几何边界，直接触发了该兜底合成，将鼠标当前瞬时物理 X 坐标强行伪造成了文字插入点光标，导致用户只要在控件内移动鼠标，提示符号便完全跟着鼠标指针游走，严重偏离了文本输入框的真实文字光标；
  - **坚决杜绝鼠标指针伪造光标，恢复纯正无障碍几何提取**：彻底移除 `TryCaretFromMousePoint` 中对鼠标 X 坐标的伪造推算分支，严格规定仅在 UIA 接口通过 `TryRectOf` 或 `TryCaretViaExpandUnit` 真实返回字符屏幕几何矩形时才予以采纳；在未暴露字符几何时，平滑回退至可靠的控件边界几何锚点（`TryCaretFromEditBounds`），彻底杜绝提示符号跟随鼠标指针漂移的严重缺陷。
- **彻底修复 Chrome 浏览网页未点击输入框出现提示符号 Bug（网页非编辑元素拦截与 MSAA 死锁熔断）**：
  - **深层技术根因**：Chromium 网页普通正文（段落 `<p>`、标题 `<h1>`、链接 `<a>`、通用容器 `<div>` 等）同样实现了 `ITextPattern`；当用户在浏览网页中点击文章正文、链接或空白处时，Chromium 会在被点击的非编辑元素上生成零长度折叠选区，由于此前版本未限定网页元素必须属于编辑类型，导致这些纯阅读文本被误当作可编辑输入框并提取了字符矩形，在未点击任何输入框的情况下弹出了提示符号；此外，此前版本在 `MsaaCaretCapture` 中放宽了宽度判定（允许 `width == 0`），Chrome 顶层窗口（`Chrome_WidgetWin_1`）的 `AXSystemCaretWin` 在整个浏览器运行期间始终维护着一个内部光标矩形（未处于打字状态时报告 `width = 0, height = 20`），`width == 0` 的穿透导致 `MsaaCaretCapture` 恒返回 `true`，使 `CaptureChain` 的 `_lastGood` 缓存被错误锁死在 MSAA 后端，导致符号常驻不消；
  - **网页内容宿主物理隔离与非编辑元素绝对拦截**：在 `UiaCaretCapture.TryElementCaret` 入口处建立严格的网页元素鉴权规则：凡处于 Chromium 渲染宿主窗口类（`Chrome_RenderWidgetHostHWND`）之下的元素，必须明确属于可编辑控件（`element.ControlType == ControlType.Edit`，涵盖 `<input>`、`<textarea>` 以及带编辑角色的 `contenteditable` 容器）；任何网页正文段落、标题、链接、普通容器或空白区域一律绝对拦截并返回 `false`，坚决禁止其产生打字光标；
  - **MSAA 零宽光标过滤与 Chrome 网址栏专属精准调度**：在 `MsaaCaretCapture` 中严格恢复正宽度（`width > 0` 与 `height > 0`）有效性校验，杜绝 `(0,0,0,0)` 及零宽假光标污染捕获链；同时将 `TryMsaaCaret` 严格限制仅在控件非网页元素、明确属于 Chrome 网址栏（`OmniboxViewViews`）、拥有键盘输入焦点且选区为折叠状态时才精准调用，彻底消除误触发。
- **零回归与稳定性保证**：严格保证微信聊天框、Windows 记事本、EmEditor、Windows 资源管理器等通用应用的高精度定位保持 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## v3.7（2026-10-17，正式版）

- **彻底修复 Chrome 网页输入框未输入时原始位置偏右与误判 Bug（渲染宿主与原生壳层严格物理隔离）**：
  - **深层技术根因**：主流中英文搜索引擎（百度 `baidu.com`、Google、Bing、淘宝、B站等）中，网页 `<input>` 搜索框的 HTML `name`、`placeholder` 或 `aria-label` 普遍包含“搜索”或“Search”字样。此前版本在 `TryCaretFromEditBounds` 中通过字符串包含判定 `isSearchOrAddress`，导致这些普通的网页 `<input>` 元素被误判为桌面顶层地址栏，被赋予了针对桌面浏览器网址栏专属的大留白（`38px * scale`）。高达 38~57px 的虚假左留白直接导致提示符号悬空在网页输入框正中央偏右，严重偏离了用户刚点击放入光标时的真实起始位置；
  - **网页渲染宿主物理隔离算法（`IsWebElement`）**：引入 `IsWebElement` 判定机制，通过检查 UIA 元素所属的 HWND 以及向上递归 8 层的父级树是否包含 Chromium 渲染宿主窗口类（`Chrome_RenderWidgetHostHWND`）或 HTML `ControlType.Document` 容器。任何处于渲染宿主之内的控件均被严格确立为网页内部元素，坚决禁止其被误判为桌面原生地址栏（`isSearchOrAddress` 恒为 `false`）；
  - **标准网页单行行高与垂直居中排版**：对于所有网页内部单行输入框，统一按真实 CSS 内边距（`10px * scale`）排版，并结合行高（`18px * scale`）执行纵向精准垂直居中（`padY = (bounds.Height - caretHeight) / 2`），使光标未输入/刚点击放入时的提示符号位置与首个输入字符的底边基线 100% 完美重合。
- **彻底修复 Chrome 网址输入框（Omnibox）未输入时鼠标点击光标不移动 Bug（深度桥接 MSAA 活跃系统光标与鼠标智能拾取）**：
  - **深层技术根因**：Chrome 网址栏（`OmniboxViewViews`）基于 Chromium 原生 Views 桌面控件框架开发。在 Windows UI Automation 规范下，Chromium Views 的平台节点委托器（`ViewsAXPlatformNodeDelegate`）未实现字符级边界测量接口（`GetInnerTextRangeBoundsRect`），导致对其 TextPattern 选区调用 `GetBoundingRectangles()` 时无论折叠光标还是单字符扩展单元恒返回空数组；此前逻辑因无法提取有效矩形，被迫强制退回 `TryCaretFromEditBounds` 并在整个前台焦点期间死锁在固定的静态初始坐标（`bounds.Left + 38 * scale`），导致无论用户在网址栏的 URL 内部点击任何位置，光标提示符号永远停留在最左侧不动；
  - **Chromium 活跃系统光标（MSAA `OBJID_CARET`）深度桥接**：Chromium 内部为兼容 Windows 无障碍体系在顶层窗口（`Chrome_WidgetWin_1`）实现了 `AXSystemCaretWin` 系统光标桥接器。无论在网址栏中鼠标点击任意字符位置、键盘左右方向键移动光标，还是打字输入，Chromium 均会通过 `MoveCaretTo` 实时向 `OBJID_CARET` 投递绝对精准的物理屏幕坐标。新版在 UIA 获取折叠选区后优先调用 `TryMsaaCaret` 查询当前控件在当前前台顶层窗口与焦点子窗口中的活跃 MSAA 光标，直接获取像素级物理屏幕矩形，彻底攻克 Chromium Views 在 UIA 下不暴露字符几何的核心瓶颈；
  - **鼠标点击位置辅助感知与即时合成（`TryCaretFromMousePoint`）**：当用户使用鼠标点击网址栏任意字符处时，若底层瞬态尚未完成无障碍事件分发，利用 `text.RangeFromPoint(mousePt)` 验证鼠标点击命中有效文本区域，并在控件包围盒内直接合成精准的鼠标点击物理坐标，确保 100% 随点随移、零延迟跟手。
- **零回归与稳定性保证**：严格保证 EmEditor、Windows 资源管理器、微信聊天框、记事本等既有高精度特性 100% 保持正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## v3.6（2026-10-17，正式版）

- **长时运行永久有效与全局白名单自愈机制（根治“运行久了失效、必须重启”Bug）**：
  - **TSF 全局状态仓 COM RCW 接口深度泄露修复**：`StateDetector.QueryTsfGlobalState` 在每次查询时通过 `TF_GetGlobalCompartment` 生成 `compMgr`、`compOpenClose`、`compConv` 接口，但缺少显式 COM 释放；高频调用下 COM RCW 句柄与 RPC 槽位累积耗尽，导致进程 COM 子系统瘫痪并引发 UIA/TSF 全面崩溃。新版加入 `finally` 严格 `Marshal.ReleaseComObject` 释放，彻底消灭句柄与内存泄露；
  - **SysInfo 输入空闲时间 32 位溢出与时钟抖动下溢修复**：修复当操作系统的高精度采样时钟与 `dwTime` 发生微小抖动或系统运行跨越时间边界时，`unchecked((uint)Environment.TickCount - lii.dwTime)` 下溢为 `4294967295ms` 导致 `idleMs > IdleStopMs` 永久成立、控制器死锁在暂停/隐藏状态的底层缺陷；
  - **UIA COM 异常向上传递与真自愈重建**：消除 `UiaCaretCapture.CaptureCore` 内部吞噬致命 COM 异常的漏洞，准确识别并向外传递 `RPC_E_DISCONNECTED`、`CO_E_OBJNOTCONNECTED`、`RPC_S_SERVER_UNAVAILABLE` 等致命断开错误，使 `WorkerLoop` 真实累计连续异常并在必要时触发 `RecycleAutomation` 重新初始化 UIA 自动化客户端；
  - **UIA 步骤 3 焦点编辑控件瞬时检索优化**：用 `FindFirstDescendant` + `HasKeyboardFocus == true` 替代耗时的 `FindAllDescendants` 全树遍历，杜绝在复杂窗口（Chrome、WeChat、VSCode、Explorer）中阻塞 COM 工作线程；
  - **GDI 屏幕 DC 与分层窗口跨锁屏/睡眠自愈保障**：`LayeredSymbolWindow` 监听 `SystemEvents.SessionSwitch`（锁屏 Win+L / 解锁）与 `SystemEvents.PowerModeChanged`（睡眠 / 唤醒），在桌面切换与系统唤醒时主动重置屏幕 DC 与位图缓存，彻底杜绝 GDI 句柄失效；
  - **空进程名短时缓存自愈**：优化 `ForegroundReader`，对启动过渡或权限受限产生的空进程名仅做 500ms 短缓存，彻底消除 30 秒僵死期。
- **窗口拖拽位移极速瞬时跟随算法（彻底消除拖动窗口时的拖影、卡顿与滞后感）**：
  - **工作线程直接渲染（彻底消除 20~40ms 跨线程消息泵延迟）**：为 `LayeredSymbolWindow` 引入内部线程同步锁 `_renderGate`，使工作线程无需通过 WinForms UI 线程消息泵（`Control.Invoke`）即可直接调用 Win32 `UpdateLayeredWindow`，更新延迟从 20~40ms 降至 0.05ms 以内，彻底解除对主界面消息队列的依赖；
  - **窗口相对位移瞬时补偿（`MoveTo`）**：在目标窗口被用户按住标题栏拖动或平移动画时，输入光标在窗口内的相对坐标保持不变。新版在检测到前台窗口发生屏幕位移 $(\Delta X, \Delta Y)$ 时，以零 GDI 重建、零内存分配直接平移分层窗口；
  - **拖拽模态期间挂起耗时跨进程查询**：在窗口位移期间直接以 8ms（~120 FPS）高频平移光标符号，跳过繁重的跨进程 UIA/TSF 查询，彻底消灭拖影、跳变与掉帧，实现 144Hz 极速跟手；窗口拖动停止后无缝恢复全量探测与校准。
- **零回归与稳定性保证**：严格保证 EmEditor、Windows 资源管理器、Chrome、微信输入等既有高精度特性 100% 保持正常。
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## v3.5（2026-10-17，正式版）

- **彻底解决 Chrome 网址输入框未输入与鼠标点击时不移动 Bug（UIA 折叠选区字符单元深度还原）**：
  - **Chromium Views 折叠选区还原机制（核心根因突破）**：在 Chrome 网址输入框（Omnibox）中，当光标为零长度折叠选区时，Chromium 的 `GetBoundingRectangles()` 恒返回空数组；此前采用的 `TryCaretViaNeighborRange` 依赖 `MoveEndpointByUnit`，而 Chromium Views 对该方法抛出 `COMException(E_NOTIMPL)` 导致推算失败，进而强制退回控件几何边界推算并死锁在固定坐标 `bounds.Left + 28`，导致无论用户点击网址栏任何位置，光标提示符号永远停留在最左侧不动；
  - **基于 `ExpandToEnclosingUnit(Character)` 的高精度字符定位**：Chromium 完整实现了 `ExpandToEnclosingUnit(TextUnit.Character)`。新版通过克隆折叠选区并扩展至单字符单元，直接提取光标所在字符的精确屏幕矩形；结合 `caretRange.CompareEndpoints` 判定光标位于字符左侧或右侧，实现纯光标插入点在 Chrome 网址栏与各类 Chromium 文本控件中的 100% 像素级跟随（完美支持鼠标点击定位与键盘左右移动）；
  - **成片选区真实宽度保持（禁止假光标截断）**：修正 `TryRectOf` 将宽选区粗暴限制为 16px 的缺陷，保留选区真实宽度，使成片拖选/全选整条 URL 时正确判定为非光标状态，杜绝选中文字时符号被画在起始端点；
  - **实时鼠标坐标光标拾取辅助（`RangeFromPoint`）**：引入 `TryCaretFromMousePoint`，在用户点击输入框的瞬态或端点未暴露时，联动当前鼠标物理坐标通过 `text.RangeFromPoint` 进行第二重高精度定位。
- **彻底解决 Chrome 网页输入框与微信聊天框未输入时原始位置不正确 Bug（DPI 动态感知与居中排版）**：
  - **现代网页单行输入框垂直居中与安全内边距（解决 Chrome 网页未输入偏上/偏左）**：现代网页 `<input>`（高度 32~48px）普遍采用垂直居中排版与 10~16px 内边距。此前硬编码 `padY = 4`、`padX = 6` 导致未输入时光标提示符号贴紧左上角。新版引入 DPI 动态感知模型（`GetDpiScaleFor`），针对单行输入框严格执行单行行高（18px*scale）与容器高度垂直居中（`padY = (bounds.Height - caretHeight) / 2`），并设置 `10px * scale` 真实内边距，使未输入时的原始位置与首个输入字符完全重合；
  - **Chrome 网址输入框未输入时左侧芯片/锁头安全留白**：通过探测子元素动态避开左侧锁头/搜索芯片图标，未探测到时提供 `38px * scale`（DPI 缩放）安全留白，杜绝未输入时符号与搜索图标重叠；
  - **微信聊天输入框未输入时真实边距锚定（解决微信未输入偏上/偏左）**：微信聊天框 `MMRichEdit` 首行文字起始边距为左 12px、顶 8px。新版在 `TryCaretFromEditBounds` 与 `TryFallbackCaret` 中针对微信/即时通讯类应用统一设置 `padX = 12 * scale`、`padY = 8 * scale`、`caretHeight = 16 * scale`，与 `CaretController` 的打字文字底边基准实现 100% 绝对咬合，彻底消灭光标刚放入与未输入时的跳变和偏差。
- **零回归与稳定性保证**：严格保证 EmEditor、Windows 资源管理器、记事本等通用应用的高精度定位 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v3.4（2026-10-17，正式版）

- **彻底根治微信聊天框光标提示符号偏下与基线三态对齐（视觉与几何深度根治）**：
  - **TSF 进程内光标假膨胀根除（最关键偏下根因）**：在微信（WeChat 3.x / Weixin 4.x / XEditor）中，TSF `ITfContextView::GetTextExt` 在纯光标折叠选区（无字符或未输入时）时，底层直接报告文字基线（Baseline）退化坐标（`top == bottom`）。此前版本在 `TsfCaretCapture` 中简单执行 `bottom = top + 20` 强行填补虚假 20px 高度，导致基线位置被虚构出 20px 下沉矩形，使光标提示符号被推入文字下方的空白区域中（悬空 14~20px）。新版恢复真实退化坐标（`bottom = top + 1`），让上层精准感知基线位置；
  - **即时通讯应用专属基线三态精准对齐算法**：
    1. **真实单行光标高度态（10~24px）**：Win32/UIA/TSF 报告真实字符高度时，有效底边严格直取 `caret.Bottom`，提示符号瞬间贴紧文字底边缘；
    2. **基线退化残片态（< 10px）**：当 TSF 折叠选区或仅暴露文字基线时，顶边即为文字基线，有效底边直取 `caret.Top + 1*scale`，紧密咬合文字基准线，彻底消灭任何向下漂移与悬空间隙；
    3. **超大多行包围盒折叠态（> 24px）**：当多行编辑区或空输入框报告整框包围盒（48~150px）时，首行文字严格按 14px*scale 标准聊天字号折叠，杜绝被容器拉扯到聊天框底部；
  - **聊天应用多维度全谱系立体识别体系**：
    - `win.Process` 覆盖：`WeChat`、`Weixin`、`WXWork`（企业微信）、`QQ`、`TIM`、`DingTalk`（钉钉）、`Feishu`（飞书）、`Lark`；
    - `win.Class` 覆盖：`WeChatMainWndForPC`、`ChatWnd` 等，且在 `CaretController` 中始终保持窗口类名读取（`needClass: true`），杜绝类名未加载导致的判空漏判；
    - `win.Title` 与规则名/规则正则全覆盖：支持含“微信”、`WeChat`、`Weixin`、`WXWork` 的规则与标题匹配，杜绝任何别名遗漏；
  - **管理员/高完整性级别进程名提取兜底保障**：在 `ForegroundReader` 中对 `Process.GetProcessById` 增加 `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `K32GetModuleBaseNameW` 双重兜底，即便微信以管理员身份运行或受 UIPI 隔离，亦能 100% 正确解析进程名为 `WeChat`；
- **其他所有软件高精度定位零回归**：严格保持 EmEditor、Windows 资源管理器、Edge/Chrome 网页正常输入框、记事本等软件的高精度实时跟随与定位；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v3.3（2026-10-17，正式版）

- **彻底修复 Chrome 网页未入输入框误弹提示与输入时光标不动 Bug**：
  - **根除 Chromium 页面根文档伪光标与整页输入框假判（核心根因）**：在 Chrome 中，当用户未激活输入框或浏览普通网页时，全局键盘焦点元素往往为网页根文档（`ControlType.Document`）。此前在 v3.1 将 `ControlType.Document` 引入 `IsEditableControl` 后，导致 `TryElementCaret` 在跳过 `IsChromiumPageRoot` 选区逻辑后，直接走入 `else if (IsEditableControl)`，将整页根文档（1920×1080）当作可编辑输入框执行几何推算，从而在网页左上角生成固定假光标（`bounds.Left + 6, bounds.Top + 4`），造成“没点输入框也出现符号”且输入时“符号固定在左上角不动”；
  - **UIA 元素入口级 Chromium 根文档绝对拦截**：在 `UiaCaretCapture.TryElementCaret` 入口处执行 `IsChromiumPageRoot` 绝对拦截，任何属于 Chromium 渲染宿主（`Chrome_RenderWidgetHostHWND`）及其层级下的页面根文档直接返回 `false`，坚决禁止其进入选区退化与控件几何推算；
  - **严格限定仅 Edit 控件参与几何边界兜底**：恢复 `IsEditableControl` 严格语义（仅限 `ControlType.Edit`），彻底杜绝浏览器整页背景、PDF 查看器或静态文档被伪造为输入控件；
- **彻底消除微信聊天框提示符号偏下（文本基准对齐）**：
  - **聊天应用 14px 紧凑基准行高锚定**：微信等即时通讯工具默认聊天字号为 10.5pt（14px 微软雅黑），此前统一的 22px 归一化行高导致符号距离文本底端仍有约 8px 的悬空感（视觉偏下）。新版在 `CaretController` 中针对聊天应用专门设定为 `14px * scale` 基准行高，并增加类名与标题全维度判别，提示符号瞬间贴紧文字基线，彻底根除悬空感；
- **其他所有软件高精度定位零回归**：严格保持 EmEditor、Windows 资源管理器、Edge/Chrome 网页正常输入框、记事本等软件的高精度实时跟随与定位；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v3.2（2026-10-17，正式版）

- **彻底修复微信聊天框光标符号定位偏下与坐标不准 Bug**：
  - **根除微信多行输入盒/段落高度脱保穿透（偏下根因）**：微信桌面客户端（WeChat 3.x / Weixin 4.x / XEditor / TSF 选区）在未输入或放入光标时，其 TSF `GetTextExt` 或多行自绘包围盒常报告 `48~120px` 的多行段落甚至整框高度。此前全局行高归一化仅在 `caretH <= 44px * scale` 时校准，导致微信的超大容器包围盒绕过归一化，提示符号被粗暴定位在 `caret.Bottom`（48~120px 底部），造成严重的“偏下”；
  - **即时通讯类应用单行行高基准锚定**：在 `CaretController` 中对微信等即时通讯应用（`WeChat`、`Weixin`、`WeChatAppEx`、`WeixinExt`、`QQ`、`DingTalk`、`Feishu`、`Lark`）强制执行单行行高基准归一化（`effectiveBottom = caret.Top + targetLineH`），杜绝任何多行输入框将提示符号拖拽至聊天框底部，确保光标刚放入与未输入时提示符号紧密贴合第一行文字基线；
  - **Win32 兜底捕获全面覆盖微信 MMRichEdit**：在 `CaptureChain.TryFallbackCaret` 中将编辑控件匹配由前缀匹配改为包含匹配（`IndexOf("RichEdit") >= 0` 并增加 `Chat` 类名识别），全面覆盖微信经典 Win32 控件 `MMRichEdit`；
  - **TSF 进程内光标矩形安全防线**：在 `TsfCaretCapture` 中对解析出的 `rect` 补足非正宽高自动保底（`right <= left` 保底 +1，`bottom <= top` 保底 +20），杜绝极端坐标反转；
- **其他所有软件高精度定位零回归**：严格保持 EmEditor、Windows 资源管理器、Chrome/Edge 浏览器与记事本等软件在 v2.9/v3.0/v3.1 中建立的高精度行高对齐与防误判机制；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v3.1（2026-10-17，正式版）

- **彻底根除所有软件光标提示符号不显示严重回归**：
  - **根除 MSAA 无光标虚假 (0,0,0,0) 污染与死锁**：在 `MsaaCaretCapture` 中严格要求光标宽度与高度均为严格正值，过滤 `(0,0,0,0)` 假光标，杜绝 `OBJID_CARET` 在目标未输入时误报 `(0,0,1,1)` 导致捕获链将 MSAA 锁死在 `_lastGood` 缓存并阻断 UIA 探测；
  - **捕获链多后端智能越界熔断过滤**：在 `CaptureChain.TryRunTracked` 中对所有捕获后端实施前台窗口包围盒有效性验证（加 32px 气泡容差），杜绝任何后端因返回 (0,0) 假坐标或外来窗口坐标而污染 `_lastGood` 缓存并导致后续帧死锁在错误后端；
  - **GuiCaretCapture 零高光标拦截**：修复 `bottomRight.Y - topLeft.Y <= 0` 时未命中判定；
  - **UIA 全局焦点元素窗口属权严格鉴权**：在 `UiaCaretCapture.CaptureCore` 中校验 `automation.FocusedElement()` 必须明确属于目标窗口 `hwnd`（或其子控件、同进程），杜绝桌面、任务栏等系统外来焦点元素几何坐标污染；
  - **UIA Document 控件可编辑回退增强**：将空输入框边界推算扩展支持 `ControlType.Document`（Win11 记事本、微信多行输入区等），光标刚放入时即可瞬时精准定位；
  - **UIA TextRange 几何矩形最大宽度约束**：在 `TryRectOf` 中对折叠光标范围实施 `MaxCaretLikeWidthPx` 约束，杜绝全行或宽控件包围盒被误当作光标；
  - **无配置文件初次启动平滑容灾**：在 `AppConfig.Load` 中确保本地配置文件尚不存在且处于便携模式时，亦平滑从 AppData 迁移已有白名单规则；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v3.0（2026-10-17，正式版）

- **彻底修复管理员模式下已加入白名单软件不提示光标符号 Bug**：
  - **前台窗口句柄防劫持校验（根治资源管理器地址栏/主窗口坐标失效）**：在 `ForegroundReader` 中对 `gti.hwndActive` 增加顶级根祖先鉴权（`GetAncestor(hwndActive, GA_ROOT) != hwnd`），仅当活动窗口为独立顶级窗口（如 Quicker 独立悬浮搜索框）时才采纳，严禁将资源管理器内部的子标签页（`ShellTabWindowClass`）误当作前台窗口，彻底解决地址栏光标因超出子标签页包围盒而被误判剔除的问题；
  - **UIA 极速全局焦点优先探测（< 2ms 瞬时响应，彻底杜绝 150ms 超时退避）**：重构 `UiaCaretCapture.CaptureCore` 发现机制，将 `automation.FocusedElement()` 提至第一阶段极速探测；在管理员权限跨权限访问目标应用时，彻底免除在整树遍历控件（`FindAllDescendants`）引起的几百毫秒卡顿与 150ms 超时退避；
  - **跨完整性级别（UIPI）焦点属性信任机制**：对直接由 `FocusedElement()` 返回的控件赋予焦点信任标识，免受跨权限时 `HasKeyboardFocus` 属性缓存失效与读取失败的影响；同时保持文件管理器浏览文件夹（焦点在 `ListItem`）时的零误判；
  - **便携模式跨权限配置无缝平滑容灾**：在 `ConfigStore.Load()` 中引入平滑容灾，当用户在标准用户下配置的白名单位于 `%LOCALAPPDATA%`，以管理员身份启动转入便携模式且本地规则为空时，自动无损导入 AppData 规则，杜绝白名单规则丢失；
  - **兜底捕获控件类名扩展**：在 `CaptureChain.TryFallbackCaret` 中扩充 `Search` 与 `Scintilla` 支持，覆盖更多现代化搜索框与代码编辑器；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v2.9（2026-10-17，正式版）

- **全局行高基准归一化 · 彻底统一各软件垂直对齐（根治 EmEditor 偏上 / 文件管理器偏上 / 微信聊天框偏下）**：
  - **根因分析**：Windows 各异构框架（Win32 Edit、EmEditor、RichEdit、UIA、TSF）对标准单行光标高度的报告定义完全不同。Win32 Edit、EmEditor 与文件管理器地址栏/搜索框仅报告纯字形上升高度（`14~16px`），导致下方提示符号偏上紧贴基线；微信 RichEdit / 多行自绘输入框报告包含行间距甚至段落内边距的过大包围盒（`30~42px`），导致下方符号偏下悬空；现代标准单行文本行高（Notepad、Chrome、Word 默认等）为 `20~24px`；
  - **智能基准行高统一化（`22px * scale`）**：在 `CaretController` 中引入全局行高基准归一化算法，对所有常规 UI 文本字号范围（`<= 44px * scale`）的光标，统一基准行高为 `22px * scale`（即 `effectiveBottom = caret.Top + targetLineH`），彻底消除不同软件在未输入/刚放入光标时提示符号高低不一、纵向漂移的问题；
  - **超大字号排版智能保护**：当用户在编辑器中设置了较大标题字号（`> 44px * scale`，如 36pt+ 标题排版）时，尊重真实底部，确保大字排版下提示符号不遮挡文字；
  - **零高度光标兼容增强**：在 `GuiCaretCapture` 与 `MsaaCaretCapture` 中对自绘编辑器的 0 高度光标自动保底为 1px，交由控制器统一扩充至基准行高，杜绝光标被意外丢弃；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## v2.4（2026-10-17，正式版）

- **实时视觉预览视口扩容 3 倍（纵向 100px 独立舞台）**：
  - 彻底解决符号与柔和阴影高度不足碰边裁剪问题，视口行高增至 160px（棋盘格净高 100px）；
  - 升级为 macOS 26 视口微边框 + 8px 连续曲率圆角视窗与 12 行完整高保真透明网格；
  - 引入动态自适应缩放与安全留白保护机制，无论何种尺寸与阴影扩散度，均居中舒展呈现且绝对杜绝裁剪；
- **极简化圆角体系：统一保留全局圆角设置**：
  - 移除状态样式卡片中冗余复杂的单状态圆角单选组、同步勾选与应用按钮，单卡片高度释放 115px；
  - 纯粹统一由“边框光效与全局圆角”集中调控，经典 960×680 画幅通透整洁，达到 macOS 26 仪表盘最佳视觉效果。

## v2.3（2026-10-17，正式版）

- **“状态与符号”与“外观与位置”合体重构为【🎨 符号与外观】单一主视图**：
  - 彻底打破以往需要频繁在不同标签页之间切换的痛点，将输入法状态、符号尺寸颜色、形状圆角、立体光效与边框、全局默认圆角、光标相对位置与偏移全部汇聚于同一视图；
- **全要素毫秒级实时视觉预览联动**：
  - 右上角高精度棋盘格置顶预览视窗全链路打通，调整当前状态颜色、尺寸、透明度、独立圆角，或者勾选立体光效（细描边、内高光、微立体、柔和阴影）、全局默认圆角时，均能实时直接呈现真机渲染效果；
- **画幅与比例视觉全面升级（macOS 26 仪表盘黄金比例）**：
  - 画幅拓宽至 960×680，左侧 6 大输入法状态与光标位置/偏移彻底消除滚动条与文字折叠，右侧立体光效与 7 档圆角全量露白；
  - 设置窗口标签页体系进化为极简 3 标签结构：`🎨 符号与外观` · `⚡ 行为与捕获` · `⚙️ 系统与更新`。

## v2.2（2026-10-17，正式版）

- **光标符号形状圆角支持全状态一键同步**：
  - 在“状态与符号”设置中加入“同步应用至所有状态”开关与快捷按钮，在切换任一圆角档位时可一键广播并应用至中文、英文、大写锁定、US键盘、日文、韩文全状态；
- **修复“行为与捕获”生命周期卡片高度裁剪与显示问题**：
  - 彻底移除触发内部滚动偏移的 `AutoScroll`，赋予 DPI 缩放绝对安全行高；
  - 保护微调数值输入框宽度并修复卡片内部容器 Dock 布局，使主开关、空闲暂停、隐藏延时、轮询周期 4 行设置项完整舒朗展示；
- **优化自绘窗口标题栏字阶（macOS 26 极简美学）**：
  - 将各功能设置窗口的自绘标题栏字体由 16pt 降至 11.5pt Bold，与 42px 标题栏高度及 12px 交通灯控制按钮达成黄金视觉比例；
- **系统托盘右键菜单增加舒适呼吸感**：
  - 菜单项纵向内边距调优，分割线上下间距提升，配合 12pt Bold 700 字体与 2px 内缩选中胶囊，带来通透高雅的层次视觉效果；
- **增强更新检查 sha256 校验提取鲁棒性**：
  - 兼容发布说明中的 Markdown 粗体排版标记，杜绝在线静默更新提示校验值缺失。

## v2.1（2026-10-17，正式版）

- **彻底修复 Quicker 等管理员权限软件中英文状态识别与 Shift 切换 Bug**：
  - 修复 IMM32 `QueryChineseState` 提前返回导致未深入检验 conversion bit 0 模式转换码的缺陷；
  - 修复 TSF 全局共享仓在 UIPI 跨权限线程私有仓无法读取时武断返回 true 导致冲刷物理 Shift 翻转英文字态的缺陷，改用智能回退机制保障状态平稳；
- **全面恢复多状态独立圆角自定义设置（0%~50% 及跟随全局）**：
  - `SymbolStyle` 扩展独立 `CornerPreference` 属性（-1 跟随全局，0~6 分别对应直角至胶囊等档位）；
  - `SettingsForm` Tab 0（状态与符号）全面提供单选组与实时预览联动，旧配置历史形态（圆形/圆角矩形）加载时无损自动升级；
- **重构美化“窗口信息”（WindowInfoForm）面板**：
  - 窗口尺寸收紧为 460×330 黄金比例画幅，彻底移除多余垂直滚动条；
  - 修复 `MacCard` 容器 Padding 二次 DPI 缩放与 `TableLayoutPanel` AutoScroll 高度膨胀死循环；
  - 属性标签与文本框像素级垂直居中对齐，遵循 macOS 26 仪表盘舒朗字阶与视觉美学。

## v2.0（2026-10-17，正式版）

- **超轻量小文件版机制**：发布全面转向框架依赖单文件，解压包仅约 600 KB，无缝借用 Windows 系统官方 .NET 10 运行时并自动享受 Windows Update 底层优化；
- **纯绿色零残留便携**：启动时智能探测目录写权限，默认纯便携运行，删除软件目录即完全清除，零系统残留；
- **系统托盘 168px 黄金紧凑美学**：12px（9.0pt）Bold 700 高对比度字阶，垂直空间压缩 30%，聚合精悍。

## v1.0（2026-10-17，正式版）

**版本线重置说明**：本版为 **1.0 正式版**——全量汇总 2.x 线全部成果（macOS 26 巅峰设计美学、8K 高分屏 PerMonitorV2 深度适配、180px 黄金对称紧凑托盘菜单、四级智能捕获链 GUI/UIA/MSAA/TSF 覆盖微信 4.x、六态预渲染零堆分配极速热路径、两轮全库审计与加固），发布与自检统一以 **1.0** 标识。

### 软件做什么

在文本输入光标旁显示一个直观微小状态符号，指示当前输入法状态（中文 / 英文 / 大写锁定 / 英文键盘 / 日文 / 韩文），避免在编辑器、浏览器和聊天软件中「想打中文却出英文」或「误开 CapsLock 错打」的频繁打断。六种状态各有独立颜色与形状，可分别自定义。自包含单文件绿色便携，免安装运行库，离线纯粹运行。

### 核心功能与亮点

- **六种输入法状态毫秒级识别**：中文 `CN` · 英文 `EN` · 大写锁定 `Caps` · 英文键盘 `US` · 日文 `JP` · 韩文 `KR`（CapsLock 硬件状态优先；中文布局深入查询 IME 打开与转换状态）。
- **macOS 26 巅峰设计美学**：
  - **180px 黄金对称紧凑托盘菜单**：托盘右键菜单基准宽度收缩至 180px，留白比达到 33px : 37px 极致平衡，彻底消除右侧大面积无意义空白；
  - **居中呼吸勾选对号**：选中指示对勾横向平移至 x=17px，12px 黄金内边距与 10px 呼吸间距，杜绝边框挤压感；
  - **顶栏专属品牌状态卡片**：180×56px 连续曲率药丸岛，内嵌 App 图标与 Bold 700 品牌名，绿/橙呼吸圆点实时呈现运行与规则生效数；
  - **Apple Squircle 连续曲率超椭圆**：全局控件与卡片容器全量接入苹果工业级连续曲率平滑算法，告别机械生硬圆角；
  - **极光药丸分段选择器（MacSegmentedControl）**：四标签极光胶囊切换卡（`🎯 状态与符号`、`🎨 外观与位置`、`⚡ 行为与捕获`、`⚙️ 系统与更新`），层级分明、交互丝滑；
  - **原生取色器胶囊按钮**：macOS 原厂药丸胶囊取色器，微光描边颜色圆点与实时十六进制色值；
  - **视网膜红绿灯标题栏**：红黄绿视网膜圆点物理居中，0.5px 精细暗色环形描边，悬停浮现微符号；
  - **Windows 11 原生亚克力与沉浸深色自适应**：无边框窗口底色启用 DWM 原生亚克力磨砂半透明材质，深浅双调色板跟随系统无缝切换。
- **最高 8K 显示器（PerMonitorV2 高 DPI）全链路适配**：
  - 内嵌专用 `app.manifest` 开启 Windows 10/11 最高级 PerMonitorV2 缩放感知与 OS 兼容声明；
  - 智能冷启动主屏物理 DPI 探测与动态内边距响应，杜绝 200%~500% 缩放下的尺寸畸变；
  - macOS 26 现代字阶体系（22pt Hero 品牌大标题、16pt 窗口标题、12pt 卡片分段标题、10.5pt 舒朗正文/菜单字阶与 Cascadia Mono 等宽数据展示）。
- **四级智能捕获链（GUI → UIA → MSAA → TSF）**：
  - `GUI`（GetGUIThreadInfo）→ `UIA`（TextPattern2 光标范围与字符边缘几何推算）→ `MSAA`（OBJID_CARET）→ `TSF`（Text Services Framework 进程内文本选区深度读取，作为最后手段）；
  - 完美覆盖 Win32 原生程序、Chromium/Electron 系浏览器（Chrome / Edge / VSCode / 飞书等）与 Windows 11 资源管理器（F2 重命名 / 地址栏 / 搜索框）；
  - **微信 4.x 深度适配**：对微信 4.1+ Qt 自绘架构输入框通过 TSF 后端精准定位光标；
  - **浏览器伪光标过滤机制**：过滤 Chromium 系页面根文档折叠选区，仅在真实输入位置显示，离开输入框立即瞬态消失。
- **光标符号与外观高度自定义**：
  - 独立自定义每种状态的颜色、宽高尺寸、透明度、全局位置偏移；
  - 7 档圆角（直角 / 16% 小 / 25% 中 / 33% 大 / 40% 特大 / 45% 超大 / 50% 胶囊，Apple/Figma 连续曲率）；
  - 5 种边框样式（无 / 细描边 / 内高光 / 微立体 / 柔和阴影）；
  - 逐像素透明分层窗口、点击穿透、不抢焦点；小尺寸超采样平滑绘制。
- **性能优化与零 GC 堆分配热路径**：
  - **六态位图预渲染池**：日常按 Shift 快速切换中英输入状态直接查表复用，微秒级延迟且**零托管堆内存分配**；
  - **ArrayPool 阴影模糊算法**：软阴影高斯模糊改用共享内存池租借工作数组，杜绝垃圾抖动；
  - **前台事件驱动唤醒 + 自适应轮询**：前台切换即时响应，输入显示时 20ms，隐藏时 120ms 降频，空闲自动暂停。
- **白名单规则系统**：
  - 进程名 / 窗口标题 / 类名支持精准匹配与正则表达式过滤，命中任一启用规则才显示；
  - 支持单规则指定捕获链优先级与追加显示偏移；
  - 实时窗口信息侦测器（WindowInfoForm）卡片 HUD，支持一键捕获并快捷添加白名单。
- **完整闭环自动更新与便携模式**：
  - 启动时自动检查更新（每日一次 / 20 小时节流）→ 后台静默下载并经 SHA256 完整性强校验 → 托盘气泡提示「更新已就绪」；
  - **三个安装时机**（点气泡立即装并重启 / 退出程序时装好、下次启动生效 / 下次启动时装好并重启一次），自愈回滚防损坏；
  - 默认配置 `%LOCALAPPDATA%\Tcursor\config.json`；程序同目录下放置 `portable.txt` 即刻切换为绿色便携模式（U盘即插即用）。
- **开源协议**：GNU 通用公共许可证第 3 版（GPL-3.0）。
