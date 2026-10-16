using System.Drawing;
using System.Windows.Forms;

namespace Tcursor.Ui;

/// <summary>
/// macOS 风格托盘弹出菜单：
/// 1. 重写 MaxItemSize 强制所有条目填满宽屏（默认 268px+，自适应 DPI）；
/// 2. 统一承载 MacMenuRenderer，消除传统 Windows 菜单左侧灰边。
/// </summary>
internal sealed class MacContextMenuStrip : ContextMenuStrip
{
    public MacContextMenuStrip()
    {
        Renderer = new MacMenuRenderer();
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Font = UiTheme.FontMenu;
        Padding = new Padding(5, 5, 5, 5);
    }

    protected override Size MaxItemSize
    {
        get
        {
            int minW = UiTheme.Dpi(this, 168);
            return new Size(Math.Max(base.MaxItemSize.Width, minW), base.MaxItemSize.Height);
        }
    }
}
