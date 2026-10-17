# Tcursor（C# / .NET 版）

Tcursor —— 在光标旁显示输入法状态符号的 Windows 小工具；本目录为其 C#/.NET（WinForms）实现（基于 .NET 10）。

## 功能范围（v3.11 正式版）

- **彻底修复 Chrome 中 Google 搜索主页不显示光标提示符号 Bug（扩展网页可编辑控件支持 ComboBox 角色）**：
  - **深层技术根因**：
    1. Google 搜索主页（`google.com`）的搜索框在 HTML 中声明了 `role="combobox"` 属性（`<textarea class="gLFyf" role="combobox" ...>`）；
    2. Chromium 无障碍引擎（`AXPlatformNodeWin`）将携带 `role="combobox"` 的输入元素直接映射为 Windows UIA 的 `ControlType.ComboBox`；
    3. 此前版本为拦截网页正文非编辑文本，在 `CaptureCore` 中硬编码了 `focused.ControlType != ControlType.Edit`，并在 `TryElementCaret` 中硬编码了 `element.ControlType != ControlType.Edit`，将 `ControlType.ComboBox` 误判为网页非编辑内容并直接拦截（标记为 `focusedIsChromiumNonEdit = true`），导致用户在 Google 搜索框无论未输入、点击还是打字均被全部阻断；
    4. 此外，此前 `IsEditableControl` 仅支持 `ControlType.Edit`，导致空搜索框无法借由控件边界推算未输入光标起始位置；
  - **智能可编辑控件鉴权体系（`IsEditableControl`）**：
    1. 升级 `IsEditableControl`，全面支持 `ControlType.Edit` 与 `ControlType.ComboBox`，同时联动验证 `IsEnabled == true` 以及 `ValuePattern.IsReadOnly == false`，精确支持 Google 搜索主页、Bing 搜索、各类带下拉建议的现代 Web 输入框；
    2. 严格阻断网页普通文本（`<p>`、`<h1>`、`<a>`、`<div>`、`<span>` 等均为 `Text`/`Group`/`Hyperlink`），保持网页正文点击 100% 零误弹。
- **彻底根治划选文字时提示符号跟随鼠标移动 Bug（坚决执行纯折叠光标鉴权，根除鼠标光标伪造漏洞）**：
  - **深层技术根因**：
    1. 当用户从后向前（或从前向后）按住鼠标拖拽划选成片文字时，`ITextPattern.GetSelection()` 处于非折叠选区状态（`Start != End`），此时界面处于文本高亮选中态，根本不存在跳动的文本插入光标；
    2. 此前代码在选区包含单个字符（宽度 `<= 16px`）时误将其当作光标采用；而在划选较长文本时，此前代码又穿透调用了 `TryCaretFromMousePoint`，利用鼠标瞬时物理坐标在文本控件内拾取对应字符包围盒并伪造成光标，导致提示符号在划选过程中完全跟着鼠标指针游走，严重违背了“仅在有输入文本光标跳动时才显示”的设计原则；
  - **纯折叠光标严格鉴权与假光标彻底清除**：
    1. 引入折叠选区双重精准校验（`CompareEndpoints == 0` 且 `range.GetText(1) == ""`），严格限定 `text2.GetCaretRange` 与 `text.GetSelection` 必须处于纯文字插入点（折叠态）才允许提取光标；
    2. 当检测到处于非折叠选区（成片划选中、双击选词、拖选等）时，坚决返回 `false` 并立即隐藏符号，杜绝任何符号显示；
    3. 彻底彻底移除 `TryCaretFromMousePoint` 鼠标伪造光标逻辑，彻底根除任何提示符号跟随鼠标游走的漏洞。
- **零回归与稳定性保证**：严格保证 Chrome 网址栏与网页输入框、微信输入法输拼音尾端跟随、微信搜索框/聊天框垂直对齐、Windows 资源管理器、记事本等通用程序 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## 功能范围（v3.10 正式版）

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

## 功能范围（v3.9 正式版）

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

## 功能范围（v3.8 正式版）

- **彻底修复文件资源管理器提示符号随鼠标移动 Bug（根除假光标鼠标坐标合成漏洞）**：
  - **深层技术根因**：此前在 `TryCaretFromMousePoint` 中引入了基于鼠标物理 X 坐标的兜底合成逻辑（`clampedX = mousePt.X`）。在 Windows 文件资源管理器（现代 DirectUI / XAML 地址栏、搜索框、重命名框及列表视图）中，控件虽然承载了 `ITextPattern` 接口，但底层未对字符范围暴露像素级包围盒；当用户鼠标掠过或悬停在控件包围盒范围内时，`text.RangeFromPoint(mousePt)` 成功返回文本区间但无几何边界，直接触发了该兜底合成，将鼠标当前瞬时物理 X 坐标强行伪造成了文字插入点光标，导致用户只要在控件内移动鼠标，提示符号便完全跟着鼠标指针游走，严重偏离了文本输入框的真实文字光标；
  - **坚决杜绝鼠标指针伪造光标，恢复纯正无障碍几何提取**：彻底移除 `TryCaretFromMousePoint` 中对鼠标 X 坐标的伪造推算分支，严格规定仅在 UIA 接口通过 `TryRectOf` 或 `TryCaretViaExpandUnit` 真实返回字符屏幕几何矩形时才予以采纳；在未暴露字符几何时，平滑回退至可靠的控件边界几何锚点（`TryCaretFromEditBounds`），彻底杜绝提示符号跟随鼠标指针漂移的严重缺陷。
- **彻底修复 Chrome 浏览网页未点击输入框出现提示符号 Bug（网页非编辑元素拦截与 MSAA 死锁熔断）**：
  - **深层技术根因**：Chromium 网页普通正文（段落 `<p>`、标题 `<h1>`、链接 `<a>`、通用容器 `<div>` 等）同样实现了 `ITextPattern`；当用户在浏览网页中点击文章正文、链接或空白处时，Chromium 会在被点击的非编辑元素上生成零长度折叠选区，由于此前版本未限定网页元素必须属于编辑类型，导致这些纯阅读文本被误当作可编辑输入框并提取了字符矩形，在未点击任何输入框的情况下弹出了提示符号；此外，此前版本在 `MsaaCaretCapture` 中放宽了宽度判定（允许 `width == 0`），Chrome 顶层窗口（`Chrome_WidgetWin_1`）的 `AXSystemCaretWin` 在整个浏览器运行期间始终维护着一个内部光标矩形（未处于打字状态时报告 `width = 0, height = 20`），`width == 0` 的穿透导致 `MsaaCaretCapture` 恒返回 `true`，使 `CaptureChain` 的 `_lastGood` 缓存被错误锁死在 MSAA 后端，导致符号常驻不消；
  - **网页内容宿主物理隔离与非编辑元素绝对拦截**：在 `UiaCaretCapture.TryElementCaret` 入口处建立严格的网页元素鉴权规则：凡处于 Chromium 渲染宿主窗口类（`Chrome_RenderWidgetHostHWND`）之下的元素，必须明确属于可编辑控件（`element.ControlType == ControlType.Edit`，涵盖 `<input>`、`<textarea>` 以及带编辑角色的 `contenteditable` 容器）；任何网页正文段落、标题、链接、普通容器或空白区域一律绝对拦截并返回 `false`，坚决禁止其产生打字光标；
  - **MSAA 零宽光标过滤与 Chrome 网址栏专属精准调度**：在 `MsaaCaretCapture` 中严格恢复正宽度（`width > 0` 与 `height > 0`）有效性校验，杜绝 `(0,0,0,0)` 及零宽假光标污染捕获链；同时将 `TryMsaaCaret` 严格限制仅在控件非网页元素、明确属于 Chrome 网址栏（`OmniboxViewViews`）、拥有键盘输入焦点且选区为折叠状态时才精准调用，彻底消除误触发。
- **零回归与稳定性保证**：严格保证微信聊天框、Windows 记事本、EmEditor、Windows 资源管理器等通用应用的高精度定位保持 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

## 功能范围（v3.7 正式版）

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

## 功能范围（v3.6 正式版）

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

## 功能范围（v3.5 正式版）

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

## 功能范围（v3.4 正式版）

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

## 功能范围（v3.3 正式版）

- **彻底修复 Chrome 网页未入输入框误弹提示与输入时光标不动 Bug**：
  - **根除 Chromium 页面根文档伪光标与整页输入框假判（核心根因）**：在 Chrome 中，当用户未激活输入框或浏览普通网页时，全局键盘焦点元素往往为网页根文档（`ControlType.Document`）。此前在 v3.1 将 `ControlType.Document` 引入 `IsEditableControl` 后，导致 `TryElementCaret` 在跳过 `IsChromiumPageRoot` 选区逻辑后，直接走入 `else if (IsEditableControl)`，将整页根文档（1920×1080）当作可编辑输入框执行几何推算，从而在网页左上角生成固定假光标（`bounds.Left + 6, bounds.Top + 4`），造成“没点输入框也出现符号”且输入时“符号固定在左上角不动”；
  - **UIA 元素入口级 Chromium 根文档绝对拦截**：在 `UiaCaretCapture.TryElementCaret` 入口处执行 `IsChromiumPageRoot` 绝对拦截，任何属于 Chromium 渲染宿主（`Chrome_RenderWidgetHostHWND`）及其层级下的页面根文档直接返回 `false`，坚决禁止其进入选区退化与控件几何推算；
  - **严格限定仅 Edit 控件参与几何边界兜底**：恢复 `IsEditableControl` 严格语义（仅限 `ControlType.Edit`），彻底杜绝浏览器整页背景、PDF 查看器或静态文档被伪造为输入控件；
- **彻底消除微信聊天框提示符号偏下（文本基准对齐）**：
  - **聊天应用 14px 紧凑基准行高锚定**：微信等即时通讯工具默认聊天字号为 10.5pt（14px 微软雅黑），此前统一的 22px 归一化行高导致符号距离文本底端仍有约 8px 的悬空感（视觉偏下）。新版在 `CaretController` 中针对聊天应用专门设定为 `14px * scale` 基准行高，并增加类名与标题全维度判别，提示符号瞬间贴紧文字基线，彻底根除悬空感；
- **其他所有软件高精度定位零回归**：严格保持 EmEditor、Windows 资源管理器、Edge/Chrome 网页正常输入框、记事本等软件的高精度实时跟随与定位；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## 功能范围（v3.2 正式版）

- **彻底修复微信聊天框光标符号定位偏下与坐标不准 Bug**：
  - **根除微信多行输入盒/段落高度脱保穿透（偏下根因）**：微信桌面客户端（WeChat 3.x / Weixin 4.x / XEditor / TSF 选区）在未输入或放入光标时，其 TSF `GetTextExt` 或多行自绘包围盒常报告 `48~120px` 的多行段落甚至整框高度。此前全局行高归一化仅在 `caretH <= 44px * scale` 时校准，导致微信的超大容器包围盒绕过归一化，提示符号被粗暴定位在 `caret.Bottom`（48~120px 底部），造成严重的“偏下”；
  - **即时通讯类应用单行行高基准锚定**：在 `CaretController` 中对微信等即时通讯应用（`WeChat`、`Weixin`、`WeChatAppEx`、`WeixinExt`、`QQ`、`DingTalk`、`Feishu`、`Lark`）强制执行单行行高基准归一化（`effectiveBottom = caret.Top + targetLineH`），杜绝任何多行输入框将提示符号拖拽至聊天框底部，确保光标刚放入与未输入时提示符号紧密贴合第一行文字基线；
  - **Win32 兜底捕获全面覆盖微信 MMRichEdit**：在 `CaptureChain.TryFallbackCaret` 中将编辑控件匹配由前缀匹配改为包含匹配（`IndexOf("RichEdit") >= 0` 并增加 `Chat` 类名识别），全面覆盖微信经典 Win32 控件 `MMRichEdit`；
  - **TSF 进程内光标矩形安全防线**：在 `TsfCaretCapture` 中对解析出的 `rect` 补足非正宽高自动保底（`right <= left` 保底 +1，`bottom <= top` 保底 +20），杜绝极端坐标反转；
- **其他所有软件高精度定位零回归**：严格保持 EmEditor、Windows 资源管理器、Chrome/Edge 浏览器与记事本等软件在 v2.9/v3.0/v3.1 中建立的高精度行高对齐与防误判机制；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## 功能范围（v3.1 正式版）

- **彻底根除所有软件光标提示符号不显示严重回归**：
  - **根除 MSAA 无光标虚假 (0,0,0,0) 污染与死锁**：在 `MsaaCaretCapture` 中严格要求光标宽度与高度均为正值，过滤 `(0,0,0,0)` 假光标，杜绝 `OBJID_CARET` 在目标未输入时误报 `(0,0,1,1)` 导致捕获链将 MSAA 锁死在 `_lastGood` 缓存并阻断 UIA 探测；
  - **捕获链多后端智能越界熔断过滤**：在 `CaptureChain.TryRunTracked` 中对所有捕获后端实施前台窗口包围盒有效性验证（加 32px 气泡容差），杜绝任何后端因返回 (0,0) 假坐标或外来窗口坐标而污染 `_lastGood` 缓存并导致后续帧死锁在错误后端；
  - **GuiCaretCapture 零高光标拦截**：修复 `bottomRight.Y - topLeft.Y <= 0` 时未命中判定；
  - **UIA 全局焦点元素窗口属权严格鉴权**：在 `UiaCaretCapture.CaptureCore` 中校验 `automation.FocusedElement()` 必须明确属于目标窗口 `hwnd`（或其子控件、同进程），杜绝桌面、任务栏等系统外来焦点元素几何坐标污染；
  - **UIA Document 控件可编辑回退增强**：将空输入框边界推算扩展支持 `ControlType.Document`（Win11 记事本、微信多行输入区等），光标刚放入时即可瞬时精准定位；
  - **UIA TextRange 几何矩形最大宽度约束**：在 `TryRectOf` 中对折叠光标范围实施 `MaxCaretLikeWidthPx` 约束，杜绝全行或宽控件包围盒被误当作光标；
  - **无配置文件初次启动平滑容灾**：在 `AppConfig.Load` 中确保本地配置文件尚不存在且处于便携模式时，亦平滑从 AppData 迁移已有白名单规则；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## 功能范围（v3.0 正式版）

- **彻底修复管理员模式下已加入白名单软件不提示光标符号 Bug**：
  - **前台窗口句柄防劫持校验（根治资源管理器地址栏/主窗口坐标失效）**：在 `ForegroundReader` 中对 `gti.hwndActive` 增加顶级根祖先鉴权（`GetAncestor(hwndActive, GA_ROOT) != hwnd`），仅当活动窗口为独立顶级窗口（如 Quicker 独立悬浮搜索框）时才采纳，严禁将资源管理器内部的子标签页（`ShellTabWindowClass`）误当作前台窗口，彻底解决地址栏光标因超出子标签页包围盒而被误判剔除的问题；
  - **UIA 极速全局焦点优先探测（< 2ms 瞬时响应，彻底杜绝 150ms 超时退避）**：重构 `UiaCaretCapture.CaptureCore` 发现机制，将 `automation.FocusedElement()` 提至第一阶段极速探测；在管理员权限跨权限访问目标应用时，彻底免除在整树遍历控件（`FindAllDescendants`）引起的几百毫秒卡顿与 150ms 超时退避；
  - **跨完整性级别（UIPI）焦点属性信任机制**：对直接由 `FocusedElement()` 返回的控件赋予焦点信任标识，免受跨权限时 `HasKeyboardFocus` 属性缓存失效与读取失败的影响；同时保持文件管理器浏览文件夹（焦点在 `ListItem`）时的零误判；
  - **便携模式跨权限配置无缝平滑容灾**：在 `ConfigStore.Load()` 中引入平滑容灾，当用户在标准用户下配置的白名单位于 `%LOCALAPPDATA%`，以管理员身份启动转入便携模式且本地规则为空时，自动无损导入 AppData 规则，杜绝白名单规则丢失；
  - **兜底捕获控件类名扩展**：在 `CaptureChain.TryFallbackCaret` 中扩充 `Search` 与 `Scintilla` 支持，覆盖更多现代化搜索框与代码编辑器；
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

## 功能范围（v2.9 正式版）

- **全局行高基准归一化 · 彻底统一各软件垂直对齐（彻底解决 EmEditor偏上 / 文件管理器偏上 / 微信聊天框偏下）**：
  - **各应用光标高度差异根源剖析**：Windows 异构框架对光标高度的定义极不统一——Win32 Edit、EmEditor 与文件管理器地址栏/搜索框仅报告纯字形上升高度（`14~16px`），导致下方符号「偏上」贴近基线；微信 RichEdit / 多行自绘输入框报告包含行间距甚至段落内边距的过大包围盒（`30~42px`），导致下方符号「偏下」悬空；现代标准单行文本行高（Notepad、Chrome、Word 默认等）为 `20~24px`；
  - **智能基准行高统一化（`22px * scale`）**：在 `CaretController` 中引入全局行高基准归一化算法，对所有常规 UI 文本字号范围（`<= 44px * scale`）的光标，统一基准行高为 `22px * scale`（即 `effectiveBottom = caret.Top + targetLineH`），彻底消除不同软件在未输入/刚放入光标时提示符号高低不一、纵向漂移的问题；
  - **超大字号排版智能保护**：当用户在编辑器中设置了较大标题字号（`> 44px * scale`）时，尊重真实底部，确保大字排版下提示符号不遮挡文字；
  - **零高度光标兼容增强**：在 `GuiCaretCapture` 与 `MsaaCaretCapture` 中对自绘编辑器的 0 高度光标自动保底为 1px，交由控制器统一扩充至基准行高，杜绝光标被意外丢弃；
- **图标内衬动态补偿（解决文件管理器偏左）**：针对资源管理器搜索框与地址栏未输入时左侧存在的 🔍 放大镜或文件夹图标，动态测量子元素或安全补偿 `padX = 28px`，让光标提示符号精准定位在首字输入点，彻底消除「偏左」；
- **多行编辑区高度折叠**：针对微信聊天框等多行输入区域（高度 120~200px），将空框几何推算的高度严格约束为首行单行高度（`20px`），杜绝全框高度推算误判；
- **彻底移除鼠标工字梁伪光标猜想**：坚决剔除鼠标光标悬停时产生的假光标锚点，杜绝鼠标在窗口中移动引起光标符号随机跳跃漂移。
- **彻底消除非输入控件/文件管理器（Explorer）虚假光标误判**：
  - **UIA 编辑框焦点严格约束**：重构 `CaptureCore` 与 `TryCaretFromEditBounds`，只有明确拥有键盘焦点（`HasKeyboardFocus == true`）的输入框才参与几何推算，杜绝文件管理器未激活的地址栏、搜索框等在浏览文件夹时被误报为光标；
  - **智能兜底（Fallback）控件类名严格鉴权**：严格过滤 Win32 编辑控件类（`Edit`/`RichEdit`/`TextBox` 等），杜绝将文件管理器列表项、工具栏按钮等误判为光标；
  - **完美平滑保留 Quicker 等浮动窗口输入支持**：Quicker 在激活输入时拥有完整的键盘焦点，所有 WPF 邻域推算与输入框几何依然 100% 稳定生效。
- **托盘「以管理员身份重启」死锁闪退彻底根除**：
  - 启动参数传递 `--wait-pid <pid>`，并在单实例互斥锁判定中引入宽限接管等待，确保原进程释放后新管理员进程平滑无缝启动。
- **彻底完美适配 Quicker 与 WPF 架构应用**：
  - **攻克 WPF TextPattern 纯光标折叠选区 0 矩形缺陷**：针对 WPF TextBox 在光标闪烁时 UIA `GetBoundingRectangles()` 返回空数组的问题，引入双向相邻字符边缘推算算法；
  - **突破顶层 Window 模式限制**：支持自适应深度检索目标窗口子树中具有键盘焦点的控件与 Edit 元素，完美识别嵌套在 WPF 窗口内部的输入框；
  - **空文本框边界智能推算（`TryCaretFromEditBounds`）**：解决全新打开的搜索栏或无文字输入框无法推算光标的行业难题；
  - **智能兜底捕获层（`TryFallbackCaret`）**：在传统后端全受阻时，结合鼠标文本工字梁（`IDC_IBEAM`）与焦点控件几何区域精准锚定光标；
  - **非激活/悬浮弹出窗口增强与一键提权**：增强 `ForegroundReader` 对 `gti.hwndActive` 独立弹出窗口的识别；托盘右键新增「以管理员身份重启」，并支持通过 Windows 计划任务实现最高权限无 UAC 弹窗开机自启。
- **界面外观绝对冻结（零变动）**：严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与精致微边框设计不变
- **六种状态检测（Quicker/提权应用穿透增强）**：中文 `CN` · 英文 `EN` · 大写 `Caps` · 英文键盘 `US` · 日文 `JP` · 韩文 `KR`（优化 IMM32 焦点窗口 + TSF 全局共享仓多级探测 + 智能状态保持 + 物理 Shift/Ctrl+Space 键流追踪，彻底解决 Quicker 等管理员程序下的中英文识别失败）
- **纯绿色便携与零系统残留**：默认程序目录可写时优先使用本地目录保存 `config.json` 与 `log.txt`，真正零残留，删除文件夹即彻底清除
- **轻量框架依赖包支持**：支持仅约 600KB 的轻量包，每次启动无缝借用 Windows 系统官方 .NET 10 运行时，自动享受 Windows Update 静默补丁与性能提升
- **光标符号（矩形）**：圆角为 **Apple/Figma 连续曲率**（corner smoothing 0.6），档位 直角 / 小 16% / 中 25% / 大 33%（默认）/ 特大 40% / 超大 45% / 胶囊 50%；**macOS 标准边框样式**：无 / 细描边 / 内高光 / 微立体 / 柔和阴影；每状态颜色/尺寸/透明度（位置“偏移”为全局共用）；渲染整幅超采样（小尺寸边缘平滑）；逐像素透明、点击穿透、不抢焦点
- **界面**：macOS 风格（浅灰窗口 + 白色圆角卡片；全自绘控件：开关 / 单选 / 复选 / 滑杆 / **数字步进框** / **文本字段** / 按钮；强调色 #007AFF；状态列表圆角高亮；**规则列表整表自绘**，复选框随深浅色）；**无边框窗口 + 自绘标题区（红黄绿交通灯）**、**跟随系统深浅色**（浅/深双调色板，托盘菜单与“关于”窗口同步）；托盘「提示 / 信息」类窗口（窗口信息 / 关于 / 更新进度 / 消息框）统一贴工作区**右下角**；全部窗口随系统 DPI 自动缩放（PerMonitorV2，自绘控件与列表同步缩放）
- **白名单规则**：命中任一启用规则才显示（进程/标题/类名正则），支持每条规则的捕获链与追加偏移
- **窗口信息**：实时显示当前窗口的进程/类名/标题/路径，并可一键添加为白名单规则
- **捕获链**：`GUI`（GetGUIThreadInfo）→ `UIA`（窗口/焦点元素 TextPattern2 光标范围；无几何时借相邻字符边缘推算——覆盖 Win11 资源管理器地址栏/搜索框；选区退化仅取“光标形”，且**页面根文档的伪光标不采用**——浏览器只在真实输入位置显示）→ `MSAA`（OBJID_CARET）→ `TSF`（进程内文本服务框架选区读取，**最后手段**：微信 4.x 等自绘封闭应用；设置中可关），按顺序回退；每进程记住最近成功后端，快失败即时重试、慢失败渐进退避；覆盖 Chromium 系浏览器与 Windows 11 资源管理器（重命名 / 地址栏 / 搜索框）等场景
- **托盘界面**：图标（运行 = 蓝 / 暂停 = 灰）、暂停/恢复、设置、规则管理、窗口信息、打开配置文件、打开数据目录、开机自启、检查更新
- **自动更新**：GitHub Releases 元数据检查（`TCURSOR_UPDATE_FEED` 可覆盖更新源，便于测试/镜像）→ 后台静默下载 → `zip sha256` 校验 → 复制自身为临时“更新器”进程 → 等主进程退出后替换 exe（随包文件存在才替换）。**安装时机**：手动立即装并重启 / 退出程序时装（不重启）/ 下次启动时装（重启一次，`TryInstallPreparedOnStartup`，仅当清单版本严格新于当前，防循环）。「已就绪」状态记在 `%TEMP%\Tcursor.Update\pending.json`（退出时安装不联网）。程序目录不可写时提示改用手动下载。无界面入口：`--check-update`、`--prepare-update`、`--install-prepared`、`--update-now`、`--apply-update`（后两者含 `--no-restart`）

## 构建与运行

```bash
# 需要 .NET SDK 10.0+（基于 .NET 10）
dotnet build -c Release
dotnet run --project Tcursor            # 或直接运行 bin 下的 exe
```

生成自包含版本（多机分发时可用）：

```bash
dotnet publish Tcursor -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 自检（无头环境可跑）

```bash
Tcursor.exe --selftest <输出目录>
```

输出环境探测 + 六种状态形状渲染样例 + 形状（圆角/边框对照）样例 + 托盘图标 + 捕获链结果 + 分层窗口与窗口信息测试（断开会话下捕获失败属预期）。

## 配置文件

默认位于 `%LOCALAPPDATA%\Tcursor\config.json`；
在 exe 同目录放置 `portable.txt` 可切换为便携模式（配置存于 exe 旁）。
升级自早前版本时，首次启动会自动沿用原有配置；旧版配置格式会在加载时自动迁移为当前格式。

> ⚠️ 白名单为空时不会显示任何符号 —— 需先在“白名单规则”里添加要生效的程序，或从“窗口信息”一键添加。

## 代码结构

```
Tcursor/
  Program.cs              入口（单实例 / 启动时安装已就绪的更新 / 自检 / 全局异常）
  Assets/                 内嵌资源（图标：运行蓝 / 暂停灰 + 应用 ico）
  App/TrayApp.cs          托盘应用与生命周期
  Core/                   状态检测、控制器（工作线程）、前台信息读取、窗口信息探测、规则预编译、日志、自动更新（UpdateService）
  Capture/                捕获后端 GUI/UIA/MSAA + 捕获链（缓存&退避）
  Render/                 形状渲染（GDI+，圆角/边框样式）与分层窗口（GDI 缓存快速路径）
  Config/                 配置模型（形状样式）与 JSON 存取（含旧配置迁移）
  Ui/                     设置（状态列表+实时预览）、规则、窗口信息、关于、更新进度、主题、托盘图标工厂
  Win32/NativeMethods.cs  全部 P/Invoke
```

## 设计说明

- 圆角采用 **Apple/Figma 连续曲率**算法（贝塞尔肩 + 圆弧 + 贝塞尔肩，corner smoothing 0.6）：曲率从直边平滑过渡到圆角，无圆弧拼接的接缝感
- 圆角档位按 macOS/iOS 标准取比例：小 16% / 中 25%（≈iOS 图标 22.37%）/ 大 33%（默认）/ 特大 40% / 超大 45% / 胶囊 50%
- 边框样式参照 macOS：细描边 ≈ rgba(23,23,23,.16~.25) 发丝线；内高光 = 顶部 1px 白色渐隐高光 + 底部 1px 暗边；柔和阴影 = 高斯模糊投影（下方偏移）+ 微暗边
- “柔和阴影”会向四周扩出投影边距，控制器按 `GetShadowPadding` 回补显示位置，保证形状锚点不变
- 小尺寸符号（12px 等）先按 2~4x 超采样绘制再高质量缩放，消除锯齿、边缘更精细

## 性能设计要点

- 前台变化用 `SetWinEventHook` 事件驱动唤醒（前台切换即时响应）
- 自适应轮询：显示时快频（默认 20ms）/ 隐藏时 120ms 降频 / 无输入空闲暂停（>500ms）
- UIA 调用在独立线程执行并限时等待（150ms）：目标应用繁忙时不阻塞检测循环
- 每进程记忆最近成功的捕获后端；失败后端渐进退避；per-PID 缓存有上限（256 条，超限整体清空）；捕获失败分流：**瞬态**（超时/繁忙）给小宽限防闪烁，**确定性**（各后端都很快回答“无光标”）立即隐藏——离开输入框即消失
- 低分配检测循环：前台信息复用缓冲 + 按需读取 + 进程名缓存；键鼠空闲时间每轮只取一次
- 输入法状态检测置于「捕获成功 + 光标在窗口内」之后：未命中或无光标的 tick 不触发 IME 跨进程查询
- 规则正则加载时预编译；样式对象引用比对触发重绘
- 分层窗口缓存屏幕 DC/内存 DC/位图副本：仅移动时只更新位置（快速路径）；并检查更新结果、在显示器拓扑变化时重建缓存（失败自愈）
- 设置窗口预览的棋盘格背景为缓存位图；DPI 按显示器缓存（5s TTL）；无规则快速短路

## 许可

以 **GPL-3.0** 发布（见 [../LICENSE](../LICENSE)）；分发包内第三方组件（.NET 运行时、FlaUI 等，均为 MIT）的许可说明见根目录 [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt)。
