using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
            catch
            {
                // 目标繁忙/元素失效等：按失败处理。
                // 但持续的连续异常意味着自动化连接本身可能已劣化（COM 断开等）——重建后重试；
                // 否则 UIA 捕获会一直失败到进程重启（长时运行后浏览器类窗口不再出现符号的修复点）。
                if (++_consecutiveErrors >= RecycleAfterErrors)
                {
                    Core.Log.Error($"UIA 自动化连接连续异常 {_consecutiveErrors} 次：已重建");
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

    private static bool CaptureCore(UIA3Automation automation, IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        AutomationElement? element = automation.FromHandle(hwnd);
        if (element is not null && TryElementCaret(element, out caret))
            return true;

        // 退化 1：从目标窗口元素子树查找活动焦点控件或编辑框（解决 WPF / Quicker 等顶层窗口无 TextPattern 且由子控件承载输入的问题）
        if (element is not null)
        {
            try
            {
                var edits = element.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
                if (edits is { Length: > 0 })
                {
                    // 仅检查当前明确拥有键盘焦点的编辑控件（杜绝文件管理器未激活的地址栏/搜索框等被误判为光标）
                    foreach (var edit in edits)
                    {
                        if (edit.Properties.HasKeyboardFocus.ValueOrDefault && TryElementCaret(edit, out caret))
                            return true;
                    }
                }
            }
            catch
            {
                // 忽略跨进程 UIPI 或子树查找异常
            }
        }

        // 退化 2：全局焦点元素（Chromium 等：顶层窗口无 Text 模式，光标在焦点元素（文档/输入框）上）
        AutomationElement? focused;
        try
        {
            focused = automation.FocusedElement();
        }
        catch
        {
            focused = null;
        }
        if (focused is not null && TryElementCaret(focused, out caret))
            return true;

        return false;
    }

    /// <summary>在单个元素上尝试取光标：Text2.GetCaretRange 优先，Text.GetSelection 端点退化
    /// （含 WPF 折叠选区无矩形修复与相邻字符推算，失败时按输入控件边界推算）。</summary>
    private static bool TryElementCaret(AutomationElement element, out CaretRect caret)
    {
        caret = default;
        try
        {
            var text2 = element.Patterns.Text2.PatternOrDefault;
            if (text2 is not null)
            {
                ITextRange? range = text2.GetCaretRange(out bool isActive);
                if (TryRectOf(range, out caret))
                    return true;
                // XAML/壳控件的 Text2 光标范围常常没有几何矩形（如 Win11 资源管理器地址栏、搜索框）：
                // 仅在光标处于活动状态时，借相邻字符范围的边缘推算光标位置
                if (range is not null && isActive && TryCaretViaNeighborRange(range, out caret))
                    return true;
            }

            var text = element.Patterns.Text.PatternOrDefault;
            if (text is not null && !IsChromiumPageRoot(element))
            {
                ITextRange[] selection = text.GetSelection();
                if (selection is { Length: > 0 })
                {
                    ITextRange selRange = selection[0];
                    if (TryRectOf(selRange, out CaretRect selRect)
                        && selRect.Right - selRect.Left <= MaxCaretLikeWidthPx)
                    {
                        caret = selRect;
                        return true;
                    }

                    // WPF 关键修复：WPF TextBox 的 TextPattern.GetSelection() 在纯光标闪烁（折叠选区）时，
                    // GetBoundingRectangles() 恒返回空数组！必须通过相邻字符范围边缘推算光标
                    if (TryCaretViaNeighborRange(selRange, out caret))
                        return true;

                    // 若输入框为空或无法通过相邻字符获取，回退到输入控件边界推算
                    if (element.ControlType == ControlType.Edit && TryCaretFromEditBounds(element, out caret))
                        return true;
                }
                else if (element.ControlType == ControlType.Edit && TryCaretFromEditBounds(element, out caret))
                {
                    return true;
                }
            }
            else if (element.ControlType == ControlType.Edit && TryCaretFromEditBounds(element, out caret))
            {
                return true;
            }
        }
        catch
        {
            // 元素失效/模式不可用：按未命中处理
        }
        return false;
    }

    /// <summary>从编辑框自身几何边界推算光标位置（用于空文本框或未暴露文本几何的自绘/WPF 输入框，仅在拥有键盘焦点时生效）</summary>
    private static bool TryCaretFromEditBounds(AutomationElement edit, out CaretRect caret)
    {
        caret = default;
        try
        {
            // 严格验证：必须明确具有键盘焦点，防止未处于编辑状态的空控件或被动控件被误判为光标
            if (!edit.Properties.HasKeyboardFocus.ValueOrDefault)
                return false;

            var bounds = edit.BoundingRectangle;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                int padX = Math.Min(6, (int)bounds.Width / 4);
                bool isSearchOrAddress = false;
                try
                {
                    string name = edit.Properties.Name.ValueOrDefault ?? "";
                    string className = edit.Properties.ClassName.ValueOrDefault ?? "";
                    string autoId = edit.Properties.AutomationId.ValueOrDefault ?? "";
                    if (name.Contains("搜索", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("地址", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Breadcrumb", StringComparison.OrdinalIgnoreCase) ||
                        autoId.Contains("Search", StringComparison.OrdinalIgnoreCase))
                    {
                        isSearchOrAddress = true;
                    }

                    // 探测左侧图标/子元素（如文件管理器搜索框左侧的 🔍 放大镜图标，避开图标区域）
                    var children = edit.FindAllChildren();
                    if (children is { Length: > 0 })
                    {
                        foreach (var child in children)
                        {
                            var cb = child.BoundingRectangle;
                            if (cb.Width > 0 && cb.Left >= bounds.Left && cb.Right <= bounds.Left + 60)
                            {
                                if (child.ControlType == ControlType.Image || child.ControlType == ControlType.Button)
                                {
                                    padX = Math.Max(padX, (int)(cb.Right - bounds.Left) + 4);
                                    isSearchOrAddress = true;
                                }
                            }
                        }
                    }
                }
                catch { }

                // 若属于搜索栏/地址栏且无子元素测量值，使用通用图标安全内边距 28px（杜绝文件管理器窗口偏左）
                if (isSearchOrAddress && padX < 28)
                {
                    padX = 28;
                }

                int padY = Math.Min(4, (int)bounds.Height / 4);
                // 紧凑单行框（<=36px）贴合控件高度；多行输入框（如微信聊天框、Notepad、高度 > 36px）
                // 首行光标只占标准单行高度（20px），绝不能将整个多行区域（100~200px）算作光标高度（杜绝微信偏下）
                int caretHeight = bounds.Height <= 36
                    ? Math.Max(12, (int)bounds.Height - padY * 2)
                    : 20;

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
                caret = new CaretRect(r.Left, r.Top, r.Right, r.Bottom);
                return true;
            }
        }
        return false;
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
