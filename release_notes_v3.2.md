# Tcursor 3.2 正式版 发布说明

> **Tcursor（输入法状态光标提示）** 是一款在文本输入光标旁实时显示输入法状态符号的 Windows 绿色轻量辅助工具。支持中文、英文、大写锁定、英文键盘、日文、韩文六种状态，离线纯粹运行。

---

### 一、版本更新概述（Version 3.2 Overview）

**Tcursor 3.2 正式版** 彻底修复了用户在反馈中指出的 **「在微信软件中，光标位置还是不准确（偏下），其他软件位置没问题」** 的独立定位缺陷。

经过对微信（WeChat 3.x / Weixin 4.x / XEditor）、TSF 文本服务框架选区、以及 Win32 MMRichEdit 输入框几何特性的深入分析与全链路溯源，查明并彻底根治了微信光标位置偏下的底层缺陷：

1. **根除微信多行输入盒/段落高度脱保穿透（偏下核心根因）**：
   - 微信桌面客户端（无论基于 MMRichEdit 的 3.x 还是基于 XEditor/Qt 的 4.x）在聊天输入框处于未输入或刚放入光标状态时，其 TSF（`ITfContextView::GetTextExt`）或多行自绘包围盒向系统报告的光标矩形高度通常为整行段落甚至聊天输入框的客户区高度（通常在 `48px ~ 120px` 之间）；
   - 在此前 v2.9 的基准行高归一化算法中，仅在 `caretH <= 44px * scale` 时才将有效底部约束为标准行高（`22px * scale`），当 `caretH > 44px * scale` 时则误当作“用户在编辑器中设置了超大标题字号（如 36pt+）”而回退为尊重真实底部（`effectiveBottom = caret.Bottom`）；
   - 这直接导致微信的超大容器包围盒（48~120px）彻底绕过了基准归一化保护，使得提示符号直接沉底绘制在 `caret.Bottom`（聊天框底端或严重偏下数十像素处）；
   - 3.2 版本在 `CaretController` 中引入即时通讯应用智能判别，微信等聊天类应用（`WeChat`、`Weixin`、`WeChatAppEx`、`WeixinExt`、`QQ`、`DingTalk`、`Feishu`、`Lark`）恒为单行文本基准行高，无论其包围盒高度多大，统一严密锚定为 `effectiveBottom = caret.Top + targetLineH`（`22px * scale`），彻底消灭偏下悬空现象。
2. **Win32 兜底捕获全面覆盖微信 MMRichEdit**：
   - 在 `CaptureChain.TryFallbackCaret` 中，此前编辑控件类名检查仅采用 `StartsWith("RichEdit")`，未能匹配微信 Win32 经典输入控件类名 `MMRichEdit`；
   - 现调整为 `IndexOf("RichEdit", ...) >= 0` 并增补 `Chat` 类名识别，使微信 MMRichEdit 在所有极端回退场景下均能稳定识别首行输入点。
3. **TSF 进程内光标矩形安全防线**：
   - 在 `TsfCaretCapture` 中对解析出的 `rect` 补足非正宽高自动保底（`right <= left` 保底 +1，`bottom <= top` 保底 +20），杜绝极端坐标反转与除零异常。
4. **其他所有软件高精度定位零回归**：
   - 严格保持 EmEditor、Windows 资源管理器、Chrome/Edge 浏览器与记事本等软件在 v2.9/v3.0/v3.1 中建立的高精度行高对齐与防误判机制；
5. **界面外观绝对冻结（零变动）**：
   - 严格保持经典的 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系与三标签页纯净布局不变。

发布继续全面聚焦**超轻量小压缩包**分发：单文件仅约 1.26 MB，压缩包仅约 **610 KB**，启动时直接借用系统已安装的官方 .NET 10 运行时，享受 Windows Update 自动提速优化与补丁，零系统残留纯便携。

---

### 二、核心修复与改进详解

#### 1. 微信输入框基准行高归一化（Anchored Line-Height Normalization）
- 微信等聊天框本质上是多行富文本容器，但在用户未输入文字或输入单行消息时，其文本光标物理呈现依然遵循标准字体行高（约 18~22px）。
- 新版本通过对聊天应用的精准捕获判别，彻底切断多行空框几何容器（48~120px）对纵向坐标的下沉拉拽，使提示符号无论何时都精准贴合在第一行文字基线下方（`caret.Top + 22px * scale`）。

#### 2. MMRichEdit 兜底捕获支持
- 微信 3.x PC 客户端在特定系统或受限权限环境下主要依靠 `MMRichEdit` 控件运作。支持该类名后，即使系统级无障碍树受限，本地 Win32 兜底层亦能立即识别其首行光标锚点。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 610 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：624,858 字节（约 610 KB）
- **zip sha256**：`9A30B9E66A5B5D0003AE44D969E9BE316259AEDA46B2316F42A3AC1B4B5D1695`
- **exe sha256**：`1076C8F058D30444B0329DB2C45F260DE4252857E11A004AFDBB012F4961E596`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
- 分发包内所包含的第三方开源组件（FlaUI.UIA3 等，均为 MIT 协议）详见 `THIRD-PARTY-NOTICES.txt`。
