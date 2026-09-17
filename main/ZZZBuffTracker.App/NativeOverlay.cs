using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ZZZBuffTracker.App;

internal static class NativeOverlay
{
    private const int GwlExStyle = -20;
    private const long Transparent = 0x20, NoActivate = 0x08000000, ToolWindow = 0x80;

    public static void SetPlayMode(IntPtr hwnd, bool play)
    {
        var style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() : GetWindowLong(hwnd, GwlExStyle);
        style |= ToolWindow;
        style = play ? style | Transparent | NoActivate : style & ~(Transparent | NoActivate);
        Marshal.SetLastPInvokeError(0);
        var previous = IntPtr.Size == 8 ? SetWindowLongPtr(hwnd, GwlExStyle, new(style))
            : new IntPtr(SetWindowLong(hwnd, GwlExStyle, (int)style));
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        // Preserve geometry and focus while committing changed extended styles.
        if (!SetWindowPos(hwnd, new(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, Action> actions = [];
    public GlobalHotkeys(IntPtr hwnd) { source = HwndSource.FromHwnd(hwnd)!; source.AddHook(HandleMessage); }

    public bool Register(int id, uint key, Action action)
    {
        if (!RegisterHotKey(source.Handle, id, 0x0001 | 0x0002 | 0x4000, key)) return false; // Alt + Ctrl + NoRepeat
        actions.Add(id, action);
        return true;
    }

    private IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && actions.TryGetValue(wParam.ToInt32(), out var action))
        { handled = true; action(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(source.Handle, id);
        actions.Clear();
        source.RemoveHook(HandleMessage);
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
