# Tcursor v3.9 发行说明（正式版）

## 概述

Tcursor v3.9 是一次针对 **微信搜索框光标提示垂直对齐不一致** 与 **微信输入法输拼音阶段提示符号首尾来回跳跃** 的专项攻坚根治版本。彻底解决了在微信顶栏搜索框中提示符号“时而偏上、时而正常”的纵向位置漂移，以及在使用微信输入法或主流输入法打字输入拼音字母过程中，提示符号在拼音串开头和结尾来回频繁横跳的严重缺陷，确保光标提示符号始终稳定贴合在拼音字母尾部。

---

## 核心改进与深层技术根因修复

### 1. 彻底根治微信搜索框光标提示符号“时而偏上、时而正常”Bug（退化行高补齐与单行搜索框居中排版）
- **深层技术根因**：
  1. **TSF 退化光标行高误判（偏上根因）**：在 `CaretController` 中，当 TSF 或底层窗口报告纯光标折叠选区（`caretH < 10px * scale`，例如折叠选区 `caretH == 1`）时，此前逻辑误假设底层 `top` 直接位于文字基线（Baseline），执行了 `effectiveBottom = caret.Top + 1px`。然而 TSF `ITfContextView::GetTextExt` 报告的 `top` 实际上是字符或单行的顶边（Top），仅加 1px 导致底边被强行绘制在字符顶部基线上方，比正常光标（`caret.Bottom = caret.Top + 16px`）生生上移了 15px，产生严重的“偏上”缺陷；当 Win32 GUI 线程光标恢复并报告正常高度（16px）时又恢复正常，两者交替触发表现为“有时候偏上、有时候正常”；
  2. **UIA 容器边界误套用多行聊天留白（未输入偏下剪刀差）**：在 `UiaCaretCapture.TryCaretFromEditBounds` 中，微信左上角的**单行搜索框**（容器高度仅 24~28px）因进程名被粗暴判为 `isChat`，套用了多行聊天窗口专属的大顶部边距（`padY = 8 * scale`）与 `caretHeight = 16 * scale`，未执行单行输入框的垂直居中排版，导致未输入时偏向底边，与打字时的文字基线存在纵向剪刀差。
- **退化光标行高智能补齐**：
  在 `CaretController` 的即时通讯应用校准中，当 `caretH < 10px * scale` 时，严密补齐为微信标准单行字号行高（`effectiveBottom = caret.Top + 16 * scale`），与正常 Win32 Caret 报告的 16px 底部实现 100% 绝对一致，彻底消除 15px 的突发跳变；
- **单行搜索框垂直居中精准排版**：
  在 `UiaCaretCapture` 中严格界定 `bounds.Height <= 36 * scale` 的微信控件为单行搜索框，执行 `(bounds.Height - caretHeight) / 2` 精准垂直居中排版与安全内边距，使未输入、放入光标与打字全阶段的纵向坐标 100% 严密贴合。

### 2. 彻底根治微信输入法打字输拼音阶段提示符号首尾来回跳跃 Bug（组字串末尾恒定锚定机制）
- **深层技术根因**：
  1. **TSF 拼音组字串跨度锚点错误**：在 `TsfCaretCapture` 中，调用 TSF `GetTextExt` 测量拼音组字串（Composition Range）时，返回的包围盒跨度包含 `left`（拼音串首字母起始坐标）与 `right`（拼音串末字母结束坐标）。此前代码直接将矩形左端赋给光标（`caret = new CaretRect(left, ...)`），导致 `CaretController` 将提示符号定位于拼音字母**开头**；
  2. **Win32 系统光标与 TSF 跨度锚点相反**：Win32 `GuiCaretCapture` 通过 `GetGUIThreadInfo` 读取系统光标时，微信输入法与主流 IME 会将系统插入点光标置于拼音串**末尾**（`caret.Left == right`）；
  3. **捕获后端交替触发导致高频横跳**：打字输拼音过程中，随着按键触发、IME 消息泵处理以及光标闪烁，`GUI` 捕获器与 `TSF` 捕获器高频交替命中（GUI 命中时在末尾，TSF 命中时在开头），导致提示符号在拼音文本首尾剧烈来回横跳，而非跟随在拼音字母后方。
- **全链路末尾插入点恒定锚定**：
  1. 在 `TsfCaretCapture` 中，当 `right > left + 1`（存在拼音组字串或文本选区跨度）时，光标有效插入点绝对锚定在文本末端（`caretX = right`），杜绝输出首字母坐标；
  2. 在 `UiaCaretCapture` 中，对 `Text2.GetCaretRange` 与 `Text.GetSelection` 同样建立末端锚定，若选区存在有效字符宽度，统一取 `selRect.Right`；
  3. 在 `CaretController` 控制核心中建立第二道坚固防线：任何后端报告 `caret.Right > caret.Left + 1` 时，横向坐标统一严格取 `caret.Right`，使提示符号在整个输拼音阶段始终稳定贴合在拼音字母尾部（`caret.Right`），彻底杜绝首尾来回跳跃！

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 Chrome 网址栏与网页输入框、Windows 资源管理器、微信聊天框、记事本、EmEditor 等通用应用的高精度定位保持 100% 正常。
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
- **压缩包大小**：628,781 字节（约 614 KB）
- **zip sha256**：`CB1B960003160B0A8997E2785976CD9BC8B0B80B1E929A70370A12AF1BD3BCE6`
- **exe sha256**：`040B279C1E6E2837B4C7F8A49C8B70007488CD063326C9A0CEFEB22AFC583A0D`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
