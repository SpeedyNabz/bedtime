using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using static Bedtime.Interop.NativeMethods;

namespace Bedtime.Services;

internal sealed record TrayMenuItem(uint Id, string Text, bool IsChecked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "");
}

/// <summary>
/// The moon in the notification area. Uses Shell_NotifyIcon directly with a hidden window, so it has no
/// dependency on a tray library. Events are raised on the UI thread via the dispatcher.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string WindowClass = "Bedtime.TrayWindow";
    private const uint IconId = 1;
    private const uint CallbackMessage = WM_APP + 1;

    private readonly DispatcherQueue _dispatcher;
    private readonly WndProc _wndProc; // must stay referenced for the life of the window
    private readonly uint _taskbarCreated;
    private readonly IntPtr _hwnd;
    private IntPtr _icon;
    private string _tooltip = "Bedtime";
    private bool _added;
    private bool _disposed;

    /// <summary>Left click, or Enter/Space when the icon has keyboard focus.</summary>
    public event EventHandler? Invoked;
    public event EventHandler<POINT>? ContextMenuRequested;
    public event EventHandler? ShellThemeChanged;
    public event EventHandler? TimeChanged;

    public TrayIcon(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _wndProc = WindowProc;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClass,
        };
        RegisterClassExW(ref windowClass);

        // A hidden top-level window rather than a message-only one: only top-level windows receive the
        // TaskbarCreated and WM_SETTINGCHANGE broadcasts.
        _hwnd = CreateWindowExW(0, WindowClass, "Bedtime", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the tray window");
    }

    public void Show()
    {
        if (_icon == IntPtr.Zero)
            LoadIcon();

        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data) || Shell_NotifyIconW(NIM_MODIFY, ref data);
        if (!_added)
        {
            Log.Error("Shell_NotifyIcon could not add the tray icon");
            return;
        }

        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    public void SetTooltip(string text)
    {
        text = text.Length > 127 ? text[..127] : text;
        if (text == _tooltip)
            return;

        _tooltip = text;
        if (_added)
        {
            var data = CreateData(NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Shows a native popup menu (in the taskbar's light/dark theme) and returns the chosen id, or 0.</summary>
    public uint ShowMenu(IReadOnlyList<TrayMenuItem> items, POINT at)
    {
        ApplyMenuTheme();
        var menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.Id == 0)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                AppendMenuW(menu, MF_STRING | (item.IsChecked ? MF_CHECKED : 0), item.Id, item.Text);
                if (item.IsDefault)
                    SetMenuDefaultItem(menu, item.Id, 0);
            }

            // Documented requirement for notification-icon menus: without it the menu won't close when
            // the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN, at.X, at.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
            DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    private NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void LoadIcon()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", ShellTheme.IsLight() ? "TrayLight.ico" : "TrayDark.ico");
        var dpi = GetDpiForWindow(_hwnd);
        var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96 : dpi);
        var icon = LoadImageW(IntPtr.Zero, file, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (icon == IntPtr.Zero)
        {
            Log.Error($"Could not load tray icon {file} (error {Marshal.GetLastWin32Error()})");
            return;
        }

        var old = _icon;
        _icon = icon;
        if (old != IntPtr.Zero)
            DestroyIcon(old);
    }

    private void RefreshIcon()
    {
        LoadIcon();
        if (_added)
        {
            var data = CreateData(NIF_ICON);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    private static void ApplyMenuTheme()
    {
        try
        {
            SetPreferredAppMode(ShellTheme.IsLight() ? AppModeForceLight : AppModeForceDark);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Falls back to the default (light) menu.
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == CallbackMessage)
            {
                // NOTIFYICON_VERSION_4: LOWORD(lParam) is the event, wParam holds the anchor point.
                var notification = (uint)(lParam.ToInt64() & 0xFFFF);
                if (notification is NIN_SELECT or NIN_KEYSELECT)
                {
                    Post(() => Invoked?.Invoke(this, EventArgs.Empty));
                }
                else if (notification == WM_CONTEXTMENU)
                {
                    var anchor = new POINT { X = (short)(wParam.ToInt64() & 0xFFFF), Y = (short)((wParam.ToInt64() >> 16) & 0xFFFF) };
                    Post(() => ContextMenuRequested?.Invoke(this, anchor));
                }
                return IntPtr.Zero;
            }

            if (msg == _taskbarCreated && msg != 0)
            {
                // Explorer restarted, taking our icon with it.
                _added = false;
                LoadIcon();
                Show();
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WM_SETTINGCHANGE when lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                    RefreshIcon();
                    Post(() => ShellThemeChanged?.Invoke(this, EventArgs.Empty));
                    break;
                case WM_TIMECHANGE:
                    Post(() => TimeChanged?.Invoke(this, EventArgs.Empty));
                    break;
                case WM_DPICHANGED:
                case WM_DISPLAYCHANGE:
                    RefreshIcon();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Tray window message 0x{msg:X} failed", ex);
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void Post(Action action) => _dispatcher.TryEnqueue(() => action());
}
