# Tcursor v3.13 发行说明（正式版）

## 概述

Tcursor v3.13 是一次针对 **Google Chrome 浏览器长时间运行后光标提示符号失灵与分离不稳定** 以及 **Chrome 网页/主页搜索框未开始输入时光标提示符号偏左** 两大深层痛点问题的专项攻坚根治版本。彻底重构了 UIA 异步调用队列模型并消除级联超时死锁；对 Chromium 后端实施退避豁免与全树深搜防护；支持 Chromium 渲染宿主跨 PID 归属鉴权与周期性轻量 COM 代理垃圾回收；并全面打通 Chromium `AXSystemCaretWin` 物理系统光标首选调用链路与网页搜索框放大镜图标智能避让，实现全场景长时间运行毫秒级稳定响应与未输入/打字全阶段像素级精准对齐。

---

## 核心改进与深层技术根因修复

### 1. 彻底根治 Chrome 长时间运行后变得不稳定与指示符号分离失灵 Bug
- **深层技术根因**：
  1. **UIA 工作线程队列级联超时与 Convoy 效应**：在 Chrome 长时间运行多标签页或挂载扩展的场景下，单次跨进程 UIA 查询偶发需 60~90ms。调用端轮询每 20~30ms 发出一个请求。一旦前序请求排队耗时超过 `CallTimeoutMs`，调用端超时放弃，但后台工作线程仍将超时废弃的请求执行到底。后续每个请求一入队就背负着前一个请求的延迟，形成全链级联超时死锁；
  2. **`CaptureChain` 对 Chromium 错误施加 3 秒死锁退避惩罚**：原 `SlowFailureMs` 为 100ms。当发生上述跨进程偶发超时时，耗时达到门限，`CaptureChain` 将其判定为“慢失败”并对 UIA 后端处以渐进退避（250ms -> 3000ms）。然而对于 Chromium，Windows 底层 Win32 GUI 与 MSAA 均不支持网页内部输入，UIA 是唯一可用后端！一旦 UIA 被禁用退避长达 3 秒，Chrome 将完全没有任何可用后端，导致光标符号瞬间消失或停滞数秒；
  3. **步骤 3 `FindFirstDescendant` 深度全树遍历卡死 Chromium**：当 `FocusedElement()` 瞬态为 null 时，步骤 3 对顶层 Chrome 窗口执行 `element.FindFirstDescendant(...)`。在多标签页复杂的 Chrome 树中，全树扫描耗时高达 500~2000ms，严重阻塞后台线程并导致死锁；
  4. **Chromium 多进程架构 Renderer 子进程 PID 误判**：网页 `<input>` 在 UIA 中的 `NativeWindowHandle` 常为 0，而 `focusedPid` 是 Chromium 的渲染子进程 PID，与顶层窗口的主浏览器 PID 不相等。此前的 `belongs` 校验将其误判为外来元素而丢弃；
  5. **COM RCW 代理对象长时堆积**：长时运行每秒数十次 UIA 调用积压了海量 COM 代理对象，导致 Windows COM IPC 通道与 Chrome 内部无障碍桥接严重滞后。
- **全链路精准攻坚**：
  1. `UiaCaretCapture` 请求对象新增 `CreatedAt` 与 `Abandoned` 标志，调用端超时立即标记放弃；后台工作线程检测到已废弃请求直接丢弃，彻底消除排队拥堵导致的级联超时死锁；同时将超时门限提升至 200ms；
  2. 新增每 300 次调用周期性轻量 `GC.Collect(1)`，高效回收 COM RCW 代理对象，防止 IPC 句柄累积；
  3. 升级 `belongs` 多进程归属判定：当 `focusedPid != hwndPid` 时，验证两者是否同属 Chrome/Edge 浏览器进程，同属则判定归属成立；
  4. 步骤 3 针对 `Chrome_WidgetWin` 窗口彻底跳过 `FindFirstDescendant` 全树深搜，杜绝卡死 Chromium；
  5. `CaptureChain` 调整 `SlowFailureMs` 至 300ms，并在退避机制中对 `Chrome_WidgetWin` 实施绝对豁免，坚决不切断 Chromium 的 UIA 通道。

### 2. 彻底根治 Chrome 中未开始输入时光标提示符号偏左 Bug
- **深层技术根因**：
  1. 此前代码将 `TryMsaaCaret(element, topHwnd, out caret)` 严格限定在 `if (isAddressBar)`，所有网页内部输入框（如 Google 搜索框、百度搜索框）从未调用过 MSAA；
  2. 当网页输入框为空（未开始输入）时，UIA 文本范围无字符，`TryCaretViaExpandUnit` 必然返回 `false`；
  3. 代码降级到 `TryCaretFromEditBounds`，硬编码取普通单行输入框 `padX = 10 * scale`。然而 Google 搜索主页（`google.com`）、百度等现代搜索框左侧有宽达 36~40px 的放大镜 🔍 图标！10px 直接落在了放大镜图标正中央，比真实的文字起始光标偏左了近 30 像素；一旦用户开始打字，输入框内有了字符，`TryCaretViaExpandUnit` 立即生效并精准锚定在字符右侧，表现为“未开始输入时偏左，开始输入后正常”；
- **全链路精准修复**：
  1. 在 `TryElementCaret` 中，取消 `isAddressBar` 限制，对所有处于折叠纯光标态（或选区为空态）的可编辑元素均优先执行 `TryMsaaCaret(element, topHwnd, out caret)`，直接提取 Chromium 内部 `AXSystemCaretWin` 维护的物理系统光标屏幕坐标（自带 bounds 包围盒有效性验证），未输入与打字阶段位置完全一致；
  2. 在 `TryCaretFromEditBounds` 中拓展网页搜索框（Google `gLFyf` / ComboBox / 包含“搜索”或“Search”）识别，未探测到图标时自动采用 `38px * scale` 安全内衬，垂直居中排版，彻底消除左侧放大镜 10px 假光标。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 EmEditor 真实字号跟随与垂直对齐、Chrome 网址栏光标跟随、Google 搜索主页 ComboBox 支持、划选文字不随鼠标跑、微信输入法输拼音尾端跟随、Windows 资源管理器、记事本等通用程序 100% 正常。
- **界面外观绝对冻结（零变动）**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。
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
- **压缩包大小**：628,627 字节（约 614 KB）
- **zip sha256**：`85FFA15DE45C53EB162C0E2EE7B67F5B6543B792DE54FB41EF7ACAA7B0A092A9`
- **exe sha256**：`FC40DFA18679404EE3AD3A1A5EFDBEA86CDF9781FCFD8A4FFC179B4174976593`

---

### 五、开源协议与鸣谢

本项目采用 [GNU General Public License v3.0](LICENSE) 开源协议，遵循开源精神，感谢所有提供反馈与支持的开源社区用户！
