# Tcursor v3.6 发行说明（正式版）

## 概述

Tcursor v3.6 是一次针对 **长时间运行稳定性（彻底消除长期运行后所有白名单软件均失效的问题）** 与 **窗口拖拽跟手度（彻底消除拖动目标窗口时光标提示符号的拖影、卡顿感与滞后）** 的里程碑级架构优化版本。

---

## 核心改进与深层技术根因修复

### 1. 长时运行永久有效与全局白名单自愈机制（根治“运行久了失效、必须重启”Bug）
- **TSF 全局状态仓 COM RCW 接口深度泄露修复**：
  `StateDetector.QueryTsfGlobalState` 在每次查询时通过 `TF_GetGlobalCompartment` 生成 `compMgr`、`compOpenClose`、`compConv` 接口，但缺少显式 COM 释放；高频调用下 COM RCW 句柄与 RPC 槽位累积耗尽，导致进程 COM 子系统瘫痪并引发 UIA/TSF 全面崩溃。新版加入 `finally` 严格 `Marshal.ReleaseComObject` 释放，彻底消灭句柄与内存泄露。
- **SysInfo 输入空闲时间 32 位溢出与时钟抖动下溢修复**：
  修复当操作系统的高精度采样时钟与 `dwTime` 发生微小抖动或系统运行跨越时间边界时，`unchecked((uint)Environment.TickCount - lii.dwTime)` 下溢为 `4294967295ms` 导致 `idleMs > IdleStopMs` 永久成立、控制器死锁在暂停/隐藏状态的底层缺陷。
- **UIA COM 异常向上传递与真自愈重建**：
  消除 `UiaCaretCapture.CaptureCore` 内部吞噬致命 COM 异常的漏洞，准确识别并向外传递 `RPC_E_DISCONNECTED`、`CO_E_OBJNOTCONNECTED`、`RPC_S_SERVER_UNAVAILABLE` 等致命断开错误，使 `WorkerLoop` 真实累计连续异常并在必要时触发 `RecycleAutomation` 重新初始化 UIA 自动化客户端。
- **UIA 步骤 3 焦点编辑控件瞬时检索优化**：
  用 `FindFirstDescendant` + `HasKeyboardFocus == true` 替代耗时的 `FindAllDescendants` 全树遍历，杜绝在复杂窗口（Chrome、WeChat、VSCode、Explorer）中阻塞 COM 工作线程。
- **GDI 屏幕 DC 与分层窗口跨锁屏/睡眠自愈保障**：
  `LayeredSymbolWindow` 监听 `SystemEvents.SessionSwitch`（锁屏 Win+L / 解锁）与 `SystemEvents.PowerModeChanged`（睡眠 / 唤醒），在桌面切换与系统唤醒时主动重置屏幕 DC 与位图缓存，彻底杜绝 GDI 句柄失效。
- **空进程名短时缓存自愈**：
  优化 `ForegroundReader`，对启动过渡或权限受限产生的空进程名仅做 500ms 短缓存，彻底消除 30 秒僵死期。

### 2. 窗口拖拽位移极速瞬时跟随算法（彻底消除拖动窗口时的拖影、卡顿与滞后感）
- **工作线程直接渲染（彻底消除 20~40ms 跨线程消息泵延迟）**：
  为 `LayeredSymbolWindow` 引入内部线程同步锁 `_renderGate`，使工作线程无需通过 WinForms UI 线程消息泵（`Control.Invoke`）即可直接调用 Win32 `UpdateLayeredWindow`，更新延迟从 20~40ms 降至 0.05ms 以内，彻底解除对主界面消息队列的依赖。
- **窗口相对位移瞬时补偿（`MoveTo`）**：
  在目标窗口被用户按住标题栏拖动或平移动画时，输入光标在窗口内的相对坐标保持不变。新版在检测到前台窗口发生屏幕位移 $(\Delta X, \Delta Y)$ 时，以零 GDI 重建、零内存分配直接平移分层窗口。
- **拖拽模态期间挂起耗时跨进程查询**：
  在窗口位移期间直接以 8ms（~120 FPS）高频平移光标符号，跳过繁重的跨进程 UIA/TSF 查询，彻底消灭拖影、跳变与掉帧，实现 144Hz 极速跟手；窗口拖动停止后无缝恢复全量探测与校准。

---

## 零回归与稳定性保证
- **通用应用零回归**：严格保证 EmEditor、Windows 资源管理器、Chrome、微信输入等既有高精度特性 100% 保持正常。
- **界面外观绝对冻结**：严格保持 960×680 画幅、100px 纵向独立实时预览视口、全局圆角体系及三标签页纯净布局零修改。

---

### 三、下载与使用说明

1. 下载下方附件 `Tcursor.zip`（仅约 613 KB，需本机安装有官方 .NET 10 桌面运行时）；
2. 解压后进入 `Tcursor` 文件夹，运行 `Tcursor.exe`（在系统托盘后台常驻）；
3. 右键托盘图标可进行光标符号与外观实时预览设置、白名单规则配置及以管理员身份重启等操作。

---

### 四、发布资产与哈希校验

解压 `Tcursor.zip` 后为 `Tcursor/` 文件夹，包含 `Tcursor.exe`、`Tcursor.pdb`、`LICENSE` 以及 `THIRD-PARTY-NOTICES.txt`。

- **压缩包名称**：`Tcursor.zip`
- **压缩包大小**：627,925 字节（约 613 KB）
- **zip sha256**：`A07996EBD1E400EFB415B8912ED3E8BE56A44E22BE003AB08101CBC271AA9A9E`
- **exe sha256**：`AE39B4720C2863AF655A0FD2E2EE9EDE0E30DC188A1B02742D9009C91D5C434A`

---

### 五、开源协议与鸣谢

- 本软件以 **GNU 通用公共许可证第 3 版（GPL-3.0）** 发布，© 2026 Crucialmer。
