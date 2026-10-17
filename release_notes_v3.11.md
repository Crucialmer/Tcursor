# Tcursor v3.11 发行说明（正式版）

## 概述

Tcursor v3.11 是一次针对 **Google Chrome 中 Google 搜索主页输入框无提示符号** 与 **文字划选（从后向前或从前向后）时提示符号跟随鼠标移动** 的专项攻坚根治版本。全面重构了 Web 可编辑控件的鉴权体系，无缝支持携带 `role="combobox"` 的现代搜索与下拉建议输入框；同时彻底清除了鼠标坐标伪造光标的残留逻辑，坚决落实“仅在存在跳动的纯文字插入光标时才显示提示符号”的设计铁律，在用户划选文本时彻底隐藏符号并绝不随鼠标位移。

---

## 核心改进与深层技术根因修复

### 1. 彻底修复 Chrome 中 Google 搜索主页不显示光标提示符号 Bug（扩展网页可编辑控件支持 ComboBox 角色）
- **深层技术根因**：
  1. **ARIA `role="combobox"` 导致 UIA 映射为 `ControlType.ComboBox`**：Google 搜索主页（`google.com`）的中央核心搜索框在 HTML 中声明了 `role="combobox"` 属性（`<textarea class="gLFyf" role="combobox" ...>`）。Chromium 无障碍引擎（`AXPlatformNodeWin`）据此将该元素映射为 Windows UIA 的 `ControlType.ComboBox` 而非普通的 `ControlType.Edit`；
  2. **非编辑元素拦截逻辑的误杀**：此前版本在拦截网页普通段落/正文时，在 `CaptureCore` 与 `TryElementCaret` 中硬编码了 `focused.ControlType != ControlType.Edit`，将 `ControlType.ComboBox` 直接当作非编辑阅读区并强制拦截（置 `focusedIsChromiumNonEdit = true`），导致用户在 Google 搜索框无论未输入、点击还是打字均被彻底阻断；
  3. **空控件边界推算支持缺失**：此前 `IsEditableControl` 仅支持 `ControlType.Edit`，使 Google 搜索框在未输入/空内容时无法借由控件几何边界推算光标初始位置。
- **智能可编辑控件鉴权体系（`IsEditableControl`）**：
  1. 升级 `IsEditableControl`，全面支持 `ControlType.Edit` 与 `ControlType.ComboBox`，同时联动验证 `IsEnabled == true` 以及 `ValuePattern.IsReadOnly == false`，精确支持 Google 搜索主页、Bing 搜索、各类带下拉建议的现代 Web 输入框；
  2. 严格阻断网页普通文本（`<p>`、`<h1>`、`<a>`、`<div>`、`<span>` 等均为 `Text`/`Group`/`Hyperlink`），保持网页正文点击 100% 零误弹。

### 2. 彻底根治划选文字时提示符号跟随鼠标移动 Bug（坚决执行纯折叠光标鉴权，根除鼠标光标伪造漏洞）
- **深层技术根因**：
  1. 当用户从后向前（或从前向后）按住鼠标拖拽划选成片文字时，`ITextPattern.GetSelection()` 处于非折叠选区状态（`Start != End`），此时界面处于文本高亮选中态，根本不存在跳动的文本插入光标；
  2. 此前代码在选区包含单个字符（宽度 `<= 16px`）时误将其当作光标采用；而在划选较长文本时，此前代码又穿透调用了 `TryCaretFromMousePoint`，利用鼠标瞬时物理坐标在文本控件内拾取对应字符包围盒并伪造成光标，导致提示符号在划选过程中完全跟着鼠标指针游走，严重违背了“仅在有输入文本光标跳动时才显示”的设计原则。
- **纯折叠光标严格鉴权与假光标彻底清除**：
  1. 引入折叠选区双重精准校验（`CompareEndpoints == 0` 且 `range.GetText(1) == ""`），严格限定 `text2.GetCaretRange` 与 `text.GetSelection` 必须处于纯文字插入点（折叠态）才允许提取光标；
  2. 当检测到处于非折叠选区（成片划选中、双击选词、拖选等）时，坚决返回 `false` 并立即隐藏符号，杜绝任何符号显示；
  3. 彻底移除 `TryCaretFromMousePoint` 鼠标伪造光标逻辑，彻底根除任何提示符号跟随鼠标游走的漏洞。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 Chrome 网址栏与网页输入框、微信输入法输拼音尾端跟随、微信搜索框/聊天框垂直对齐、Windows 资源管理器、记事本等通用程序 100% 正常。
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
- **压缩包大小**：628,873 字节（约 614 KB）
- **zip sha256**：`43C150AB1ADDBB93F7897D570339156D8F843B790DFB861223F07006293ED6DF`
- **exe sha256**：`CDB37EE471515B64166ED485DF019FB164E33EFEDC58B42523542EC10818AAC8`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
