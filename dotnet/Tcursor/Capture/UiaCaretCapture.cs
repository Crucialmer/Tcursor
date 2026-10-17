using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using Tcursor.Core;

namespace Tcursor.Capture;

/// <summary>
/// UIA 捕获：TextPattern2 的 GetCaretRange（失败时退化为选区端点；光标范围无几何的 XAML/壳控件
/// ——如 Win11 资源管理器地址栏/搜索框——借相邻字符范围的边缘推算光标位置）。
/// 两个来源：① 目标窗口元素；② 全局焦点元素（Chromium 等浏览器：顶层窗口无 Text 模式，
/// 光标在焦点元素（文档/输入框）上；且窗口 A11y 对象短时失效时该路径仍可用——作为冗余来源）。
///
/// v0.2 优化：UIA 调用移入独立工作线程执行，调用方只限时等待（150ms）——
/// 目标应用繁忙/无响应时不会阻塞检测循环；超时按失败处理，由捕获链退避。
/// 该线程只处理“最新”的请求（陈旧请求直接作废），UIA3Automation 在自身线程创建。
/// </summary>
internal sealed class UiaCaretCapture : ICaretCapture, IDisposable
{
    private const int CallTimeoutMs = 150;
    private const int RecycleAfterErrors = 8;   // 连续异常达到该次数：重建自动化连接（见 RecycleAutomation）

    private sealed class Request
    {
        public IntPtr Hwnd;
        public readonly ManualResetEventSlim Done = new(false);
        public bool Found;
        public CaretRect Rect;
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Thread _worker;
    private Request? _pending;
    private volatile bool _quit;
    private UIA3Automation? _automation;
    private int _consecutiveErrors;   // 仅 UIA 工作线程访问

    public UiaCaretCapture()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Tcursor.UIA",
        };
        _worker.Start();
    }

    public bool TryGetCaret(IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        var request = new Request { Hwnd = hwnd };
        lock (_gate)
            _pending = request; // 只保留最新请求
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有待处理信号：工作线程会取到最新请求
        }
        catch (ObjectDisposedException)
        {
            return false; // 已释放（应用退出中）
        }

        if (!request.Done.Wait(CallTimeoutMs))
        {
            request.Done.Dispose();   // 超时也要释放（SetDone 已捕获 ObjectDisposedException，与晚到的 Set 竞争是安全的）
            return false;             // 超时：不再等待（晚到的结果会被丢弃）
        }

        bool found = request.Found;
        var rect = request.Rect;
        request.Done.Dispose();
        if (found)
        {
            caret = rect;
            return true;
        }
        return false;
    }

    private void WorkerLoop()
    {
        while (!_quit)
        {
            try
            {
                _signal.Wait(500);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (_quit)
                break;

            Request? request;
            lock (_gate)
            {
                request = _pending;
                _pending = null;
            }
            if (request is null)
                continue;

            // 已有更新的请求排队：作废本次（优先响应最新状态）
            lock (_gate)
            {
                if (_pending is not null)
                {
                    SetDone(request);
                    continue;
                }
            }

            bool found = false;
            CaretRect rect = default;
            try
            {
                _automation ??= new UIA3Automation();
                found = CaptureCore(_automation, request.Hwnd, out rect);
                _consecutiveErrors = 0;   // 无异常完成（含“未命中”）：连接健康
            }
            catch (Exception ex)
            {
                // 目标繁忙/元素失效等：按失败处理。
                // 但持续的连续异常或致命 COM 异常意味着自动化连接本身已断开/劣化（RPC 崩溃/句柄断开等）——立即重建！
                _consecutiveErrors++;
                if (IsFatalComException(ex) || _consecutiveErrors >= RecycleAfterErrors)
                {
                    Core.Log.Error($"UIA 自动化连接劣化（{ex.GetType().Name}: 0x{ex.HResult:X8}，连续 {_consecutiveErrors} 次）：已重建");
                    RecycleAutomation();
                }
            }

            if (found)
            {
                request.Rect = rect;
                request.Found = true;
            }
            SetDone(request);
        }
    }

    /// <summary>通知调用方请求结束。
    /// 防御性保护：调用方在成功分支与超时分支都会释放 Done，本方法的 Set() 可能与 Dispose() 竞争——
    /// 未捕获的 ObjectDisposedException 会终止整个 UIA 工作线程（= UIA 捕获永久失效），故在此兜底。</summary>
    private static void SetDone(Request request)
    {
        try
        {
            request.Done.Set();
        }
        catch (ObjectDisposedException)
        {
            // 调用方已放弃并释放：忽略
        }
    }

    /// <summary>释放并重建 UIA 自动化连接（下次使用时惰性重建）。仅 UIA 工作线程调用。
    /// 背景：UIA 客户端的 COM 连接在目标崩溃/RPC 断开等故障后可能持续报错，重建自动化对象是
    /// 公认的恢复手段；不重建则 UIA 捕获会一直失败到进程重启。</summary>
    private void RecycleAutomation()
    {
        _consecutiveErrors = 0;
        try
        {
            _automation?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
        _automation = null;
    }

    /// <summary>外部请求自愈/重建自动化连接（如系统锁屏解锁/睡眠唤醒后）</summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _consecutiveErrors = RecycleAfterErrors;
        }
    }

    private static bool IsFatalComException(Exception ex)
    {
        if (ex is InvalidComObjectException)
            return true;
        if (ex is COMException comEx)
        {
            uint hr = (uint)comEx.HResult;
            // 典型 COM 断开/不可用/死锁错误码：
            // 0x80010108: RPC_E_DISCONNECTED (对象已与其客户端断开连接)
            // 0x800401FD: CO_E_OBJNOTCONNECTED (对象未连接到服务器)
            // 0x800706BA: RPC_S_SERVER_UNAVAILABLE (RPC 服务器不可用)
            // 0x800706BE: RPC_E_CALL_FAILED (远程过程调用失败)
            // 0x800706BF: RPC_E_SERVER_DIED (RPC 服务器在调用期间终止)
            // 0x80010105: RPC_E_SERVERFAULT (服务器引发了意外异常)
            // 0x80010001: RPC_E_CALL_REJECTED (调用被被调用方拒绝)
            return hr == 0x80010108 || hr == 0x800401FD || hr == 0x800706BA ||
                   hr == 0x800706BE || hr == 0x800706BF || hr == 0x80010105 ||
                   hr == 0x80010001;
        }
        return false;
    }

    private static bool CaptureCore(UIA3Automation automation, IntPtr hwnd, out CaretRect caret)
    {
        caret = default;

        // 步骤 1（极速首选，耗时通常 < 2ms）：直接获取当前具有全局键盘焦点的 UIA 元素
        // 覆盖 99% 的文本输入场景（Chrome、Edge、微信、Notepad、EmEditor、资源管理器地址栏/搜索框、各类 WPF/自绘窗口）
        // 且由于 FocusedElement 本身即代表当前键盘输入焦点，可免受跨完整性级别/跨权限时 HasKeyboardFocus 属性读取失效的影响
        bool focusedIsChromiumNonEdit = false;
        try
        {
            AutomationElement? focused = automation.FocusedElement();
            if (focused is not null)
            {
                if (IsChromiumPageRoot(focused) || (IsWebElement(focused) && !IsEditableControl(focused)))
                {
                    focusedIsChromiumNonEdit = true;
                }
                else
                {
                    // 严格校验：焦点元素必须属于目标前台窗口（或其子控件、同进程），
                    // 杜绝跨窗口、桌面、任务栏等外来焦点元素的几何污染
                    bool belongs = false;
                    IntPtr focusedHwnd = (IntPtr)focused.Properties.NativeWindowHandle.ValueOrDefault;
                    if (focusedHwnd != IntPtr.Zero)
                    {
                        belongs = focusedHwnd == hwnd || Win32.NativeMethods.GetAncestor(focusedHwnd, Win32.NativeMethods.GA_ROOT) == hwnd;
                    }
                    else
                    {
                        Win32.NativeMethods.GetWindowThreadProcessId(hwnd, out uint hwndPid);
                        int focusedPid = focused.Properties.ProcessId.ValueOrDefault;
                        belongs = hwndPid != 0 && focusedPid == (int)hwndPid;
                    }

                    if (belongs && TryElementCaret(focused, hwnd, out caret, isFocusedElement: true))
                        return true;
                }
            }
        }
        catch (Exception ex) when (!IsFatalComException(ex))
        {
            // 忽略跨进程 UIPI 或焦点获取常规异常，致命 COM 异常向上抛出以触发自愈重建
        }

        // 若前台键盘焦点明确位于 Chromium 网页非编辑区域（网页正文、链接、图片、空白背景），
        // 用户根本未处于打字状态，坚决禁止回退到步骤 3 误抓取顶栏 Omnibox 网址输入框！
        if (focusedIsChromiumNonEdit)
            return false;

        // 步骤 2：直接检查目标窗口根元素（部分简易窗口根元素自身即承载 TextPattern）
        AutomationElement? element = null;
        try
        {
            element = automation.FromHandle(hwnd);
            if (element is not null && TryElementCaret(element, hwnd, out caret, isFocusedElement: false))
                return true;
        }
        catch (Exception ex) when (!IsFatalComException(ex))
        {
            // 忽略常规句柄获取异常，致命 COM 异常向上抛出以触发自愈重建
        }

        // 步骤 3：退化回退（仅当 FocusedElement 未能命中时，针对非标准窗口在窗口子树内查找焦点控件）
        if (element is not null)
        {
            try
            {
                // 仅寻找当前明确拥有键盘焦点的单项 Edit 控件，绝不可使用 FindAllDescendants 遍历全树（会卡死 Chromium/微信/VSCode 数秒）
                var edit = element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(new FlaUI.Core.Conditions.PropertyCondition(automation.PropertyLibrary.Element.HasKeyboardFocus, true)));
                if (edit is not null && TryElementCaret(edit, hwnd, out caret, isFocusedElement: true))
                    return true;
            }
            catch (Exception ex) when (!IsFatalComException(ex))
            {
                // 忽略跨进程 UIPI 或子树查找异常
            }
        }

        return false;
    }

    private static bool IsEditableControl(AutomationElement element)
    {
        try
        {
            var ct = element.ControlType;
            if (ct == ControlType.Edit || ct == ControlType.ComboBox)
            {
                // 排除明确被禁用的控件
                if (!element.Properties.IsEnabled.ValueOrDefault)
                    return false;

                // 若支持 ValuePattern，检查是否只读
                var val = element.Patterns.Value.PatternOrDefault;
                if (val is not null && val.IsReadOnly.ValueOrDefault)
                    return false;

                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>在单个元素上尝试取光标：Text2.GetCaretRange 优先，Text.GetSelection 端点退化
    /// （含 Chromium/WPF 扩展字符单元、WPF 相邻字符推算、鼠标点击拾取，失败时按输入控件边界推算）。</summary>
    private static bool TryElementCaret(AutomationElement element, IntPtr topHwnd, out CaretRect caret, bool isFocusedElement = false)
    {
        caret = default;
        try
        {
            // Chromium 页面根文档拦截：Chromium 页面根文档为整页展示背景，绝不能视作光标或可编辑控件
            if (IsChromiumPageRoot(element))
                return false;

            // 网页内容宿主物理隔离：处于 Chromium 渲染宿主（Chrome_RenderWidgetHostHWND）之下的元素，
            // 必须明确是可编辑控件（IsEditableControl，涵盖 input/textarea/combobox/contenteditable），
            // 严禁将网页普通段落、标题、链接、正文容器等非编辑元素的选区误判为打字光标！
            bool isWeb = IsWebElement(element);
            if (isWeb && !IsEditableControl(element))
                return false;

            var text2 = element.Patterns.Text2.PatternOrDefault;
            if (text2 is not null)
            {
                ITextRange? range = text2.GetCaretRange(out bool isActive);
                if (isActive && range is not null && IsCollapsed(range))
                {
                    if (TryRectOf(range, out caret) && caret.Right - caret.Left <= MaxCaretLikeWidthPx)
                        return true;
                    if (TryCaretViaExpandUnit(range, out caret))
                        return true;
                    if (TryCaretViaNeighborRange(range, out caret))
                        return true;
                }
            }

            var text = element.Patterns.Text.PatternOrDefault;
            if (text is not null)
            {
                ITextRange[] selection = text.GetSelection();
                if (selection is { Length: > 0 })
                {
                    ITextRange selRange = selection[0];
                    // 关键逻辑：必须是纯文字插入点（折叠选区，Start == End）！
                    // 当用户选中文字（向前/向后划选、双击选词、拖选等处于非折叠选区）时，
                    // 界面不存在跳动的输入光标，坚决不显示提示符号，更绝不能跟随鼠标向前跑！
                    if (IsCollapsed(selRange))
                    {
                        if (TryRectOf(selRange, out CaretRect selRect)
                            && selRect.Right - selRect.Left <= MaxCaretLikeWidthPx)
                        {
                            caret = selRect;
                            return true;
                        }

                        // 优先通过 ExpandToEnclosingUnit(Character) 还原光标位置（彻底解决 Chrome 网址栏与网页输入框）
                        if (TryCaretViaExpandUnit(selRange, out caret))
                            return true;

                        // WPF 关键修复：借相邻字符边缘推算
                        if (TryCaretViaNeighborRange(selRange, out caret))
                            return true;

                        // Chromium Views 专用：对于 Chrome 网址输入框（OmniboxViewViews），
                        // 折叠选区自身不返回字符几何矩形，且仅在拥有文本且拥有焦点时借助 Chromium 活跃系统光标（MSAA OBJID_CARET）提取物理屏幕矩形！
                        string clsName = "";
                        try { clsName = element.Properties.ClassName.ValueOrDefault ?? ""; } catch { }
                        if (!isWeb && clsName.Contains("Omnibox", StringComparison.OrdinalIgnoreCase))
                        {
                            bool hasText = false;
                            try
                            {
                                string doc = text.DocumentRange?.GetText(8) ?? "";
                                hasText = doc.Trim().Length > 0;
                            }
                            catch { }

                            if (hasText && TryMsaaCaret(element, topHwnd, out caret))
                                return true;
                        }

                        // 纯光标插入点折叠状态下未暴露文本几何（如未输入/空文本框）：回退到控件几何边界推算
                        if (IsEditableControl(element) && TryCaretFromEditBounds(element, out caret, isFocusedElement))
                            return true;
                    }
                    else
                    {
                        // 选区处于非折叠展开状态（用户正在划选或已选中成片文字）：
                        // 此时用户处于划选状态，界面无文本插入光标，坚决返回 false！
                        return false;
                    }
                }
                else
                {
                    // 选区为空（非输入编辑态）：仅当拥有焦点的可编辑输入框允许从控件几何推算
                    if (IsEditableControl(element) && TryCaretFromEditBounds(element, out caret, isFocusedElement))
                        return true;
                }
            }
            else if (!isWeb && IsEditableControl(element))
            {
                if (TryCaretFromEditBounds(element, out caret, isFocusedElement))
                    return true;
            }
        }
        catch (Exception ex) when (!IsFatalComException(ex))
        {
            // 元素失效/模式不可用：按未命中处理
        }
        return false;
    }

    /// <summary>从编辑框自身几何边界推算光标位置（用于空文本框或未暴露文本几何的自绘/WPF 输入框，仅在拥有键盘焦点时生效）</summary>
    private static bool TryCaretFromEditBounds(AutomationElement edit, out CaretRect caret, bool isFocusedElement = false)
    {
        caret = default;
        try
        {
            // 严格验证：必须明确具有键盘焦点，防止未处于编辑状态的空控件或被动控件被误判为光标；
            // 若该元素已经来自 FocusedElement()，则已知具有全局焦点，无需且不可再因跨完整性级别读取属性失败而误判放弃
            if (!isFocusedElement && !edit.Properties.HasKeyboardFocus.ValueOrDefault)
                return false;

            var bounds = edit.BoundingRectangle;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                double scale = GetDpiScaleFor((int)bounds.Left, (int)bounds.Top);

                string name = "";
                string className = "";
                string autoId = "";
                try
                {
                    name = edit.Properties.Name.ValueOrDefault ?? "";
                    className = edit.Properties.ClassName.ValueOrDefault ?? "";
                    autoId = edit.Properties.AutomationId.ValueOrDefault ?? "";
                }
                catch { }

                bool isChat = false;
                if (className.Contains("MMRichEdit", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("ChatWnd", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("WeChat", StringComparison.OrdinalIgnoreCase))
                {
                    isChat = true;
                }
                else
                {
                    try
                    {
                        int pid = edit.Properties.ProcessId.ValueOrDefault;
                        if (pid > 0)
                        {
                            string pName = System.Diagnostics.Process.GetProcessById(pid).ProcessName;
                            if (pName.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("Weixin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("WXWork", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("QQ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("DingTalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("Feishu", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isChat = true;
                            }
                        }
                    }
                    catch { }
                }

                bool isWeb = IsWebElement(edit);
                bool isSearchOrAddress = false;
                if (!isWeb)
                {
                    // 仅针对真正的桌面顶层地址栏/搜索框（Chrome 网址栏 OmniboxViewViews、文件管理器地址栏/面包屑等）
                    if (className.Contains("Omnibox", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Breadcrumb", StringComparison.OrdinalIgnoreCase) ||
                        autoId.Contains("Omnibox", StringComparison.OrdinalIgnoreCase) ||
                        autoId.Contains("AddressBandRoot", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("地址和搜索栏", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Address and search bar", StringComparison.OrdinalIgnoreCase) ||
                        (className.Contains("Edit", StringComparison.OrdinalIgnoreCase) && name.Contains("地址", StringComparison.OrdinalIgnoreCase)) ||
                        (className.Contains("Edit", StringComparison.OrdinalIgnoreCase) && name.Contains("Address", StringComparison.OrdinalIgnoreCase)))
                    {
                        isSearchOrAddress = true;
                    }
                }

                // 探测左侧图标/子元素（如文件管理器搜索框左侧的 🔍 放大镜图标或地址栏下拉图标，避开图标区域）
                int detectedIconRight = 0;
                if (!isWeb)
                {
                    try
                    {
                        var children = edit.FindAllChildren();
                        if (children is { Length: > 0 })
                        {
                            foreach (var child in children)
                            {
                                var cb = child.BoundingRectangle;
                                if (cb.Width > 0 && cb.Left >= bounds.Left && cb.Right <= bounds.Left + 80 * scale)
                                {
                                    if (child.ControlType == ControlType.Image || child.ControlType == ControlType.Button || child.ControlType == ControlType.Text)
                                    {
                                        detectedIconRight = Math.Max(detectedIconRight, (int)(cb.Right - bounds.Left) + (int)Math.Round(4 * scale));
                                        isSearchOrAddress = true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                int padX;
                int padY;
                int caretHeight;

                if (isChat && bounds.Height > Math.Round(36 * scale))
                {
                    // 微信/即时通讯软件多行聊天输入框（高度通常 100~200px）：
                    // 标准首行左边距 12px*scale，顶边距 8px*scale，首行高度 16px*scale，与打字时光标基准绝对吻合
                    padX = (int)Math.Round(12 * scale);
                    padY = (int)Math.Round(8 * scale);
                    caretHeight = (int)Math.Round(16 * scale);
                }
                else if (isSearchOrAddress || (isChat && bounds.Height <= Math.Round(36 * scale)))
                {
                    // Chrome 网址输入框（Omnibox）/ Windows 资源管理器地址与搜索框 / 微信顶栏单行搜索框：
                    // 避开探测到的左侧图标，若无内部图标则取真实单行文字起始内边距（微信 10px / 网址栏 6px）
                    padX = detectedIconRight > 0 ? detectedIconRight : (int)Math.Round((isChat ? 10 : 6) * scale);
                    caretHeight = (int)Math.Round(16 * scale);
                    if (bounds.Height < caretHeight + 4)
                        caretHeight = Math.Max(12, (int)bounds.Height - 4);
                    // 单行搜索/地址栏文字垂直居中排版
                    padY = Math.Max(2, ((int)bounds.Height - caretHeight) / 2);
                }
                else if (bounds.Height <= Math.Round(54 * scale))
                {
                    // 现代网页普通单行输入框（Chrome input，含百度/Google等网页搜索框）/ 原生单行编辑框：
                    // 内边距 10px*scale，文字垂直居中排版，行高 18px*scale，彻底解决偏上与偏左
                    padX = (int)Math.Round(10 * scale);
                    caretHeight = (int)Math.Round(18 * scale);
                    if (bounds.Height < caretHeight + 4)
                        caretHeight = Math.Max(12, (int)bounds.Height - 4);
                    padY = Math.Max(2, ((int)bounds.Height - caretHeight) / 2);
                }
                else
                {
                    // 多行编辑控件（网页 textarea / Notepad / 自绘多行框）：
                    padX = (int)Math.Round(8 * scale);
                    padY = (int)Math.Round(6 * scale);
                    caretHeight = (int)Math.Round(18 * scale);
                }

                int left = (int)bounds.Left + padX;
                int top = (int)bounds.Top + padY;
                caret = new CaretRect(left, top, left + 1, top + caretHeight);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 判断 UIA 元素是否属于网页内部元素（渲染宿主 Chrome_RenderWidgetHostHWND 或 Document 之下的控件）。
    /// 网页内部的 input 搜索框绝对不是桌面顶层地址栏，严禁套用 38px 网址栏左留白。
    /// </summary>
    private static bool IsWebElement(AutomationElement element)
    {
        try
        {
            IntPtr elHwnd = (IntPtr)element.Properties.NativeWindowHandle.ValueOrDefault;
            if (elHwnd != IntPtr.Zero)
            {
                var sb = new System.Text.StringBuilder(64);
                if (Win32.NativeMethods.GetClassName(elHwnd, sb, sb.Capacity) > 0)
                {
                    string cls = sb.ToString();
                    if (cls.IndexOf("RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            AutomationElement? current = element;
            for (int depth = 0; current is not null && depth < 8; depth++)
            {
                string cls = current.ClassName ?? "";
                if (cls.IndexOf("RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (current.ControlType == ControlType.Document)
                    return true;
                current = current.Parent;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 「Chromium 页面（含内层框架）根文档」判定（Chrome / Edge / WebView2 / Electron / QtWebEngine 同源实现）。
    ///
    /// Chromium 系浏览器会在渲染宿主（Chrome_RenderWidgetHostHWND）之下的**根文档**上维护一个“伪光标”：
    /// 折叠选区始终停留在“最后一次点击处”，**即使焦点不在任何可编辑元素上**——点击正文文字行、空白区域、
    /// 链接、图片，Focus 元素都会是这篇根文档，选区退化到 1px 宽（“光标形”），从而被上面的选区退化路径
    /// 当作输入框光标采用，符号就显示在了没有输入框的地方（用户上报：Chrome 中点击文字行也会弹提示）。
    ///
    /// 2026-10-14 扩展：**同源 iframe 的内层文档**同样维护该伪光标（用户上报：阿里旺旺网页版聊天框在
    /// 非输入区也弹提示；实测 Chrome 155：点 iframe 内文字行，焦点元素即内层 Document，选区 1×37px）。
    /// 内层文档的父元素不是渲染宿主，而是外层文档/分组，故不能只看父元素——改为沿父链向上最多 9 层
    /// 找渲染宿主/顶层窗口类，命中任意一层即视为“页面根文档”。
    ///
    /// 真实可编辑元素不受此判定影响：表单控件（input/textarea/地址栏）暴露为 Edit；contenteditable
    /// 暴露为**文档之下**的子元素（Group），不是 Document 类型，照常采用（实测：Chrome 155 点
    /// textarea / 内容可编辑区 / iframe 内 contenteditable 仍正常显示）。代价：以 Document 形式暴露的
    /// 整页可编辑（designMode、body contenteditable）在 Chromium 窗口下不再提示——与该判定既有取舍一致。
    /// 判定失败（父链读不到）时不拦截，保持原有行为。
    /// </summary>
    private static bool IsChromiumPageRoot(AutomationElement element)
    {
        try
        {
            if (element.ControlType != ControlType.Document)
                return false;
            string selfCls = element.ClassName ?? "";
            if (selfCls.StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)
                || selfCls.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                return true;

            AutomationElement? current = element.Parent;
            for (int depth = 0; current is not null && depth < 9; depth++)
            {
                string cls = current.ClassName ?? "";
                if (cls.StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)
                    || cls.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                    return true;
                current = current.Parent;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>选区退化路径的最大可接受宽度（像素）：只把“光标形”（零宽/单字符）选区当作光标，
    /// 成片选区（拖选文本）不显示符号，避免符号被画在选区区域上。</summary>
    private const int MaxCaretLikeWidthPx = 16;

    /// <summary>借光标相邻字符的几何推算光标位置（调用方仅应在光标处于活动状态、且光标范围自身无矩形时调用）：
    /// 优先 [k-1,k) 的右缘（k&gt;0）；k==0 时退而取 [k,k+1) 的左缘。</summary>
    private static bool TryCaretViaNeighborRange(ITextRange caretRange, out CaretRect caret)
    {
        caret = default;
        // ① 前一字符 [k-1,k) 右缘
        try
        {
            ITextRange back = caretRange.Clone();
            back.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
            System.Drawing.Rectangle[] rects = back.GetBoundingRectangles();
            if (rects is { Length: > 0 })
            {
                System.Drawing.Rectangle r = rects[^1];
                if (r.Height > 0)
                {
                    caret = new CaretRect(r.Right, r.Top, r.Right + 1, r.Bottom);
                    return true;
                }
            }
        }
        catch
        {
            // 尝试下一方案
        }
        // ② 后一字符 [k,k+1) 左缘
        try
        {
            ITextRange fwd = caretRange.Clone();
            fwd.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
            System.Drawing.Rectangle[] rects = fwd.GetBoundingRectangles();
            if (rects is { Length: > 0 })
            {
                System.Drawing.Rectangle r = rects[0];
                if (r.Height > 0)
                {
                    caret = new CaretRect(r.Left, r.Top, r.Left + 1, r.Bottom);
                    return true;
                }
            }
        }
        catch
        {
            // 未命中
        }
        return false;
    }

    private static bool TryRectOf(ITextRange? range, out CaretRect caret)
    {
        caret = default;
        if (range is null)
            return false;
        System.Drawing.Rectangle[] rectangles = range.GetBoundingRectangles();
        if (rectangles is null)
            return false;
        foreach (var r in rectangles)
        {
            if (r.Height > 0 && r.Width >= 0)
            {
                int w = r.Width <= 0 ? 1 : r.Width;
                caret = new CaretRect(r.Left, r.Top, r.Left + w, r.Bottom);
                return true;
            }
        }
        return false;
    }

    /// <summary>判断选区是否为零长度纯光标折叠态（双重校验：端点重合且跨度为 0 字符）</summary>
    private static bool IsCollapsed(ITextRange range)
    {
        try
        {
            if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0)
                return false;
            string sample = range.GetText(1);
            return string.IsNullOrEmpty(sample);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 通过 ExpandToEnclosingUnit(Character) 获取光标所在单个字符的屏幕矩形。
    /// Chromium（Chrome/Edge 网址栏与网页输入框）、WPF、XAML 均完整实现该接口；
    /// 当零长度折叠选区自身不返回矩形时，扩展至字符单元可高精度还原光标屏幕坐标。
    /// </summary>
    private static bool TryCaretViaExpandUnit(ITextRange caretRange, out CaretRect caret)
    {
        caret = default;
        try
        {
            ITextRange expanded = caretRange.Clone();
            expanded.ExpandToEnclosingUnit(TextUnit.Character);
            System.Drawing.Rectangle[] rects = expanded.GetBoundingRectangles();
            if (rects is { Length: > 0 })
            {
                int x;
                int top;
                int bottom;
                try
                {
                    int cmp = caretRange.CompareEndpoints(TextPatternRangeEndpoint.Start, expanded, TextPatternRangeEndpoint.Start);
                    if (cmp > 0)
                    {
                        var last = rects[^1];
                        x = last.Right;
                        top = last.Top;
                        bottom = last.Bottom;
                    }
                    else
                    {
                        var first = rects[0];
                        x = first.Left;
                        top = first.Top;
                        bottom = first.Bottom;
                    }
                }
                catch
                {
                    var first = rects[0];
                    x = first.Left;
                    top = first.Top;
                    bottom = first.Bottom;
                }

                if (bottom > top)
                {
                    caret = new CaretRect(x, top, x + 1, bottom);
                    return true;
                }
            }
        }
        catch
        {
            // 忽略 ExpandToEnclosingUnit 不支持的异常
        }
        return false;
    }

    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private const uint OBJID_CARET = 0xFFFFFFF8;

    /// <summary>
    /// 从 MSAA OBJID_CARET 提取真实光标位置（用于 Chrome 网址栏 OmniboxViewViews 等
    /// Views 控件在 UIA 下不暴露字符几何矩形、折叠选区端点推算失败时提取光标）。
    /// Chromium 内部维护 AXSystemCaretWin，在网址栏光标移动/点击时实时更新物理坐标。
    /// </summary>
    private static bool TryMsaaCaret(AutomationElement element, IntPtr topHwnd, out CaretRect caret)
    {
        caret = default;
        try
        {
            IntPtr elHwnd = IntPtr.Zero;
            try { elHwnd = (IntPtr)element.Properties.NativeWindowHandle.ValueOrDefault; } catch { }

            if (elHwnd != IntPtr.Zero && TryMsaaCaretHwnd(elHwnd, element, out caret))
                return true;

            if (topHwnd != IntPtr.Zero && topHwnd != elHwnd && TryMsaaCaretHwnd(topHwnd, element, out caret))
                return true;
        }
        catch { }
        return false;
    }

    private static bool TryMsaaCaretHwnd(IntPtr hwnd, AutomationElement element, out CaretRect caret)
    {
        caret = default;
        object? acc = null;
        try
        {
            Guid iid = IID_IAccessible;
            int hr = Win32.NativeMethods.AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out acc);
            if (hr != 0 || acc is null)
                return false;

            dynamic d = acc;
            d.accLocation(out int left, out int top, out int width, out int height, 0);
            if (width < 0 || height <= 0 || (left == 0 && top == 0 && width <= 1 && height <= 1))
                return false;

            // 严格有效性校验：捕获到的光标坐标必须落在当前焦点输入框的屏幕包围盒内（允许 12px 容差）
            var bounds = element.BoundingRectangle;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                int cx = left;
                int cy = top + height / 2;
                if (cx < bounds.Left - 12 || cx > bounds.Right + 12 ||
                    cy < bounds.Top - 12 || cy > bounds.Bottom + 12)
                {
                    return false;
                }
            }

            int w = width <= 0 ? 1 : width;
            caret = new CaretRect(left, top, left + w, top + height);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (acc is not null)
            {
                try { Marshal.ReleaseComObject(acc); } catch { }
            }
        }
    }

    private static double GetDpiScaleFor(int x, int y)
    {
        try
        {
            var pt = new Win32.NativeMethods.POINT { X = x, Y = y };
            IntPtr monitor = Win32.NativeMethods.MonitorFromPoint(pt, Win32.NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && Win32.NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch { }
        return 1.0;
    }

    public void Dispose()
    {
        _quit = true;
        try
        {
            _signal.Release();
        }
        catch (Exception)
        {
            // SemaphoreFull/Disposed：忽略
        }
        try
        {
            _worker.Join(1000);
        }
        catch
        {
            // 忽略等待失败
        }
        try
        {
            _automation?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
        _automation = null;
        _signal.Dispose();
    }
}
