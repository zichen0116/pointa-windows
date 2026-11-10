using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Pointa.Windows;

internal sealed class TrayMenu : System.Windows.Controls.ContextMenu
{
    public TrayMenu()
    {
        SetResourceReference(StyleProperty, typeof(System.Windows.Controls.ContextMenu));
        MinWidth = 288; FontSize = 14;
        Placement = PlacementMode.MousePoint;
        Opened += (_, _) =>
        {
            // A tray popup must own foreground focus to dismiss outside and receive keyboard navigation.
            if (System.Windows.PresentationSource.FromVisual(this) is HwndSource source) SetForegroundWindow(source.Handle);
            Focus();
        };
    }
    public MenuItem Add(string text, Action action)
    {
        var item = new MenuItem { Header = text, MinHeight = 34 };
        item.Click += (_, _) => { IsOpen = false; action(); };
        Items.Add(item);
        return item;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
