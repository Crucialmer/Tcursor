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
            return false; // 超时：不再等待（晚到的结果会被丢弃）

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
            }
            catch
            {
                // 目标繁忙/元素失效等：按失败处理
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
    /// 防御性保护：当前实现中调用方只在成功分支释放 Done，理论上不会出现在此处释放的情况；
    /// 但若后续改为「超时分支也立即释放」，本方法的 Set() 就会与 Dispose() 竞争——
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

    private static bool CaptureCore(UIA3Automation automation, IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        AutomationElement? element = automation.FromHandle(hwnd);
        if (element is not null && TryElementCaret(element, out caret))
            return true;

        // 退化：全局焦点元素（Chromium 等：顶层窗口无 Text 模式，光标在焦点元素（文档/输入框）上）
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
    /// （仅当选区为“光标形”时采用）。</summary>
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
            if (text is not null)
            {
                ITextRange[] selection = text.GetSelection();
                if (selection is { Length: > 0 }
                    && TryRectOf(selection[0], out CaretRect selRect)
                    && selRect.Right - selRect.Left <= MaxCaretLikeWidthPx)
                {
                    caret = selRect;
                    return true;
                }
            }
        }
        catch
        {
            // 元素失效/模式不可用：按未命中处理
        }
        return false;
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
