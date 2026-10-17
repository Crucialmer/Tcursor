# Tcursor v3.5 发行说明（正式版）

## 概述

Tcursor v3.5 是一次针对 **Chrome 网址输入框光标跟随** 以及 **Chrome 网页与微信未输入时初始位置偏位** 的关键精度修复版本。通过深入挖掘 Chromium Views 与 Windows 无障碍架构（UIA）底层，突破了 Chromium 折叠选区无几何矩形的长期痛点，并建立了 DPI 动态感知的居中与真实边距模型。

---

## 核心改进与深层技术根因修复

### 1. 彻底解决 Chrome 网址输入框未输入与鼠标点击时光标提示符不移动问题
- **Chromium Views 折叠选区还原机制**：
  在 Chrome 网址输入框（Omnibox）中，当光标为零长度折叠选区（纯插入点闪烁）时，Chromium 的 `IUIAutomationTextRange::GetBoundingRectangles()` 恒返回空数组 `[]`。既有的 `TryCaretViaNeighborRange` 依赖 `MoveEndpointByUnit`，而 Chromium Views 对该方法抛出 `COMException(E_NOTIMPL)` 导致推算直接失败抛弃，从而退回控件几何边界推算并死锁在固定坐标 `bounds.Left + 28`，导致无论用户点击网址栏任何字符，提示符号永远被死锁在最左侧不动。
- **基于 `ExpandToEnclosingUnit(Character)` 的像素级还原**：
  Chromium 完整实现了 `ExpandToEnclosingUnit(TextUnit.Character)`。新版通过克隆折叠选区并扩展至单字符单元，直接提取光标所在字符的精确屏幕矩形；结合 `caretRange.CompareEndpoints` 判定光标位于字符左侧或右侧，实现纯光标插入点在 Chrome 网址栏与各类 Chromium 文本控件中的 100% 像素级跟随（完美支持鼠标点击定位与键盘左右移动）。
- **成片选区真实宽度保持（禁止假光标截断）**：
  修正 `TryRectOf` 将宽选区粗暴限制为 16px 的缺陷，保留选区真实宽度，使成片拖选/全选整条 URL 时正确判定为非光标状态，杜绝选中文字时符号被画在起始端点。
- **实时鼠标坐标光标拾取辅助（`RangeFromPoint`）**：
  引入 `TryCaretFromMousePoint`，在用户点击输入框的瞬态或端点未暴露时，联动当前鼠标物理坐标通过 `text.RangeFromPoint` 进行第二重高精度定位。

### 2. 彻底解决 Chrome 网页输入框未输入时原始位置不正确（偏上/偏左）问题
- **现代网页单行输入框垂直居中与安全内边距**：
  现代网页 `<input>`（高度 32~48px）普遍采用垂直居中排版与 10~16px 内边距。此前硬编码 `padY = 4`、`padX = 6` 导致未输入时光标提示符号贴紧左上角（偏上 10px、偏左 6~10px）。新版引入 DPI 动态感知模型（`GetDpiScaleFor`），针对单行输入框严格执行单行行高（18px*scale）与容器高度垂直居中（`padY = (bounds.Height - caretHeight) / 2`），并设置 `10px * scale` 真实内边距，使未输入时的原始位置与首个输入字符完全重合。
- **Chrome 网址输入框未输入时左侧芯片/锁头安全留白**：
  通过探测子元素动态避开左侧锁头/搜索芯片图标，未探测到时提供 `38px * scale`（DPI 缩放）安全留白，杜绝未输入时符号与搜索图标重叠。

### 3. 彻底解决微信聊天输入框未输入时原始位置不正确问题
- **微信聊天输入框真实边距锚定**：
  微信聊天框 `MMRichEdit` 首行文字起始边距为左 12px、顶 8px。此前在未输入退回边界推算时，使用了通用的 `padX = 6`、`padY = 4`，导致未输入时符号明显贴在左上角边框缝隙中。新版在 `TryCaretFromEditBounds` 与 `TryFallbackCaret` 中针对微信/即时通讯类应用统一设置 `padX = 12 * scale`、`padY = 8 * scale`、`caretHeight = 16 * scale`，与 `CaretController` 的打字文字底边基准实现 100% 绝对咬合，彻底消灭光标刚放入与未输入时的跳变和偏差。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 EmEditor、Windows 资源管理器、记事本等通用应用的高精度定位 100% 保持正常。
- **界面外观绝对冻结**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局不变。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 612 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：626,622 字节（约 612 KB）
- **zip sha256**：`3988BE2D7F1EFF54BC741E65DE75FD042908E9CEFE3C81A22FB1B625E7998029`
- **exe sha256**：`E491C709FA1F08EC270F8173E616C8716B312EE73EB7B33A25ECF95BE5C38D91`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
- 分发包内所包含的第三方开源组件（FlaUI.UIA3 等，均为 MIT 协议）详见 `THIRD-PARTY-NOTICES.txt`。
