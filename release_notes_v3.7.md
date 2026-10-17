# Tcursor v3.7 发行说明（正式版）

## 概述

Tcursor v3.7 是一次针对 **Google Chrome / Chromium 浏览器环境下光标高精度定位** 的深度专项优化版本。彻底解决了在 Chrome 网页普通输入框（如百度、Google、Bing 等网页搜索框）未输入时光标提示符号位置偏右偏离真实光标的问题，以及在 Chrome 网址输入框（Omnibox）中未输入时鼠标点击定位光标、提示符号不会随点击位置移动的问题。

---

## 核心改进与深层技术根因修复

### 1. 彻底解决 Chrome 网页输入框未输入时原始位置偏右与误判 Bug（渲染宿主与原生壳层严格物理隔离）
- **深层技术根因**：
  在主流中英文搜索引擎（百度 `baidu.com`、Google、Bing、淘宝、B站等）中，网页 `<input>` 搜索框的 HTML `name`、`placeholder` 或 `aria-label` 普遍包含“搜索”或“Search”字样。此前版本在 `TryCaretFromEditBounds` 中通过字符串包含判定 `isSearchOrAddress`，导致这些普通的网页 `<input>` 元素被误判为桌面顶层地址栏，被赋予了针对桌面浏览器网址栏专属的大留白（`38px * scale`）。高达 38~57px 的虚假左留白直接导致提示符号悬空在网页输入框正中央偏右，严重偏离了用户刚点击放入光标时的真实起始位置。
- **网页渲染宿主物理隔离算法（`IsWebElement`）**：
  引入 `IsWebElement` 判定机制，通过检查 UIA 元素所属的 HWND 以及向上递归 8 层的父级树是否包含 Chromium 渲染宿主窗口类（`Chrome_RenderWidgetHostHWND`）或 HTML `ControlType.Document` 容器。
  任何处于渲染宿主之内的控件均被严格确立为**网页内部元素**，坚决禁止其被误判为桌面原生地址栏（`isSearchOrAddress` 恒为 `false`）；
- **标准网页单行行高与垂直居中排版**：
  对于所有网页内部单行输入框，统一按真实 CSS 内边距（`10px * scale`）排版，并结合行高（`18px * scale`）执行纵向精准垂直居中（`padY = (bounds.Height - caretHeight) / 2`），使光标未输入/刚点击放入时的提示符号位置与首个输入字符的底边基线 100% 完美重合。

### 2. 彻底解决 Chrome 网址输入框（Omnibox）未输入时鼠标点击光标不移动 Bug（深度桥接 MSAA 活跃系统光标与鼠标智能拾取）
- **深层技术根因**：
  Chrome 网址栏（`OmniboxViewViews`）基于 Chromium 原生 Views 桌面控件框架开发。在 Windows UI Automation 规范下，Chromium Views 的平台节点委托器（`ViewsAXPlatformNodeDelegate`）未实现字符级边界测量接口（`GetInnerTextRangeBoundsRect`），导致对其 TextPattern 选区调用 `GetBoundingRectangles()` 时无论折叠光标还是单字符扩展单元恒返回空数组；此前逻辑因无法提取有效矩形，被迫强制退回 `TryCaretFromEditBounds` 并在整个前台焦点期间死锁在固定的静态初始坐标（`bounds.Left + 38 * scale`），导致无论用户在网址栏的 URL 内部点击任何位置，光标提示符号永远停留在最左侧不动。
- **Chromium 活跃系统光标（MSAA `OBJID_CARET`）深度桥接**：
  Chromium 内部为兼容 Windows 无障碍体系在顶层窗口（`Chrome_WidgetWin_1`）实现了 `AXSystemCaretWin` 系统光标桥接器。无论在网址栏中鼠标点击任意字符位置、键盘左右方向键移动光标，还是打字输入，Chromium 均会通过 `MoveCaretTo` 实时向 `OBJID_CARET` 投递绝对精准的物理屏幕坐标。新版在 UIA 获取折叠选区后优先调用 `TryMsaaCaret` 查询当前控件在当前前台顶层窗口与焦点子窗口中的活跃 MSAA 光标，直接获取像素级物理屏幕矩形，彻底攻克 Chromium Views 在 UIA 下不暴露字符几何的核心瓶颈；
- **鼠标点击位置辅助感知与即时合成（`TryCaretFromMousePoint`）**：
  当用户使用鼠标点击网址栏任意字符处时，若底层瞬态尚未完成无障碍事件分发，利用 `text.RangeFromPoint(mousePt)` 验证鼠标点击命中有效文本区域，并在控件包围盒内直接合成精准的鼠标点击物理坐标，确保 100% 随点随移、零延迟跟手。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 EmEditor、Windows 资源管理器、微信聊天框、记事本等既有高精度特性 100% 保持正常。
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
- **压缩包大小**：628,966 字节（约 614 KB）
- **zip sha256**：`8C6A31E5FD7746A435223B8464E2E47A1078FE5D303231A84B0BAF0D9DC28BB3`
- **exe sha256**：`00ED21DF031354355A714F6F57A8B75370922FA8C86DD12213177A0678F67851`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。