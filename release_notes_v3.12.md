# Tcursor v3.12 发行说明（正式版）

## 概述

Tcursor v3.12 是一次针对 **EmEditor 等专业纯文本/代码编辑器中提示符号垂直基准位置不正确（字号变大时横切字符字形、字号变小时过度下坠）** 与 **Google Chrome 网址栏（Omnibox）中提示符号位置偏离锁头图标与鼠标点击不跟随** 的专项攻坚根治版本。彻底重构了通用文本编辑器在各字号下的垂直基准对齐机制，坚决废止对常规字体行高的强制覆盖；全面升级了 Chrome 网址栏的多语言/跨平台 UIA 标识探测与 Chromium `AXSystemCaretWin` 活跃系统物理光标优先调用链路，并在未输入时精准避开锁头图标，实现全场景毫厘不差的像素级高精度定位。

---

## 核心改进与深层技术根因修复

### 1. 彻底修复 EmEditor 中光标提示符号位置不正确 Bug（恢复真实字号基线跟随与大型编辑视口兜底隔离）
- **深层技术根因**：
  1. **`CaretController.cs` 行高强制归一化篡改（垂直 Y 偏离主因）**：此前版本在 `!isChatApp` 分支中对 `caretH <= maxUiLineH (44px)` 的光标一律强行覆盖为 `effectiveBottom = caret.Top + 22px * scale`。EmEditor 属于专业纯文本与代码编辑器，用户广泛自定义 9pt (13px)、10pt (15px)、12pt (18px)、16pt (24px) 等字号与行距。当字体较小时，强行归一化导致符号大幅下坠，甚至覆盖下一行文字；当字体或行距较大时，强行归一化导致符号向上侵入文字内部，直接横切字符字形；
  2. **`GuiCaretCapture.cs` 零高/反向高度被误拒**：Win32 自绘编辑器在特定状态下报告 `rcCaret.Bottom == rcCaret.Top`，此前 `if (bottomRight.Y - topLeft.Y <= 0) return false;` 导致 `GuiCaretCapture` 被误判失败；
  3. **`CaptureChain.TryFallbackCaret` 灾难性左上角假光标穿透**：当主捕获后端穿透到兜底逻辑时，`TryFallbackCaret` 对类名命中 `isEditClass` 的控件无高度上限约束，将整个 1920×1080 编辑视口的外框左上角 `(focusRect.Left + 8, focusRect.Top + 4)` 误当做光标，导致符号突发瞬移至全屏第一行第一列。
- **全链路精准修复**：
  1. 在 `CaretController.cs` 中，对处于常规排版字号范围（`12px * scale <= caretH <= 48px * scale`）内的光标，100% 遵照真实 `caret.Bottom` 锚定，彻底消除强制归一化导致的字形侵入与过度下坠；仅在 `caretH < 12px` 时补偿至 18px，`caretH > 48px` 时折叠至 20px；
  2. 在 `GuiCaretCapture.cs` 中容忍零高度与反向高度，自动按 `Math.Max(14, Math.Abs(diffY))` 兜底保证 GUI 捕获首发命中；
  3. 在 `CaptureChain.TryFallbackCaret` 中增加 `if (!isChat && h > (int)Math.Round(54 * scale)) return false;`，严禁对大型代码编辑器（`EmEditorView`、`Scintilla`）套用窗口左上角兜底坐标。

### 2. 彻底修复 Chrome 网址栏（Omnibox）中光标提示符号位置不正确 Bug（MSAA 活跃系统光标优先与首字内衬校准）
- **深层技术根因**：
  1. **网址栏类名/属性识别断裂**：在不同系统与语言环境下，Chrome Views 网址栏的 UIA `ClassName` 常为 `ViewsTextfieldEdit` 或为空，真正的标识存在于 `AutomationId`（`OmniboxViewViews`）与 `Name`（“地址和搜索栏” / “Address and search bar”）。此前代码仅检查 `clsName.Contains("Omnibox")`，导致地址栏识别失败；
  2. **MSAA 物理光标调用次序滞后与空文本阻断**：Chrome Views 网址栏的 UIA `TextPattern` 无法测量字符屏幕矩形，必须依赖 Chromium 内部维护的 `AXSystemCaretWin`（MSAA `OBJID_CARET`）。此前代码将 MSAA 置于 `TryCaretViaExpandUnit` 之后且限定 `hasText` 必须为真，导致未输入或点击时 MSAA 未能正确接管；
  3. **未输入时虚假 6px 留白落在锁头图标上**：此前在未探测到左侧图标时取 `padX = 6px`。Chrome 网址栏左侧包含宽度约 28~34px 的锁头/安全图标，导致未输入时光标提示符号直接被画在锁头图标正中央。
- **全链路精准修复**：
  1. 全面拓展 `isAddressBar` 识别逻辑，支持 `ClassName`、`AutomationId` 及多语言 `Name`；
  2. 针对 `isAddressBar` 在纯折叠光标状态下优先调用 `TryMsaaCaret`，以最高优先级提取 Chromium `AXSystemCaretWin` 活跃系统光标，实现鼠标点击与打字时毫厘不差的物理屏幕光标跟随；
  3. 将未探测到图标时的未输入内衬修正为标准安全内衬 `34px * scale`，精准落在首字输入起始基线上。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 Google 搜索主页（`ComboBox` 角色支持）、文字划选不跟鼠标（仅纯折叠光标显示）、微信打字不横跳及垂直对齐、Windows 资源管理器、记事本等通用程序 100% 正常。
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
- **压缩包大小**：628,367 字节（约 614 KB）
- **zip sha256**：`6B75DB064C7CF923395636C4D392A9C638755049127625BCA51C20FC9DD99BED`
- **exe sha256**：`448D6203E3B4BCADEA1EB5B2540C74CDC4DF73B7FF6D52203F41A9A412E0F821`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
