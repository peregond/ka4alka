using System.ComponentModel;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Kachalka;

public enum NotificationDelivery { None, Submitted, Disabled, Repeated, Unavailable }

/// <summary>
/// Native Windows Shell notifications. Windows controls their presentation and
/// respects Do Not Disturb / Focus Assist. The temporary notification-area icon
/// exists only while notifications are pending; no tray mode or background app
/// is introduced. Clicking a notice calls the existing window, never a command,
/// protocol handler or a second instance of the application.
/// </summary>
public sealed class WindowsNotifications : IDisposable
{
    const uint CallbackMessage = 0x8000 + 0x341;
    const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, InfoFlag = 0x10;
    const uint UserIcon = 4, LargeIcon = 0x20;
    const int BalloonShow = 0x402, BalloonHide = 0x403, BalloonTimeout = 0x404, BalloonClick = 0x405;
    const string AppId = "Ka4alka.Desktop";
    static readonly Guid IconId = new("d26d2d1b-a15a-4d56-b7fc-948ed42eef36");
    readonly Window owner;
    readonly Action onClick;
    readonly Func<bool> enabled;
    readonly DispatcherTimer watchdog;
    readonly Dictionary<(DownloadNoticeKind Kind, string Id), DateTime> delivered = new();
    readonly Func<DateTime> now;
    HwndSource? source;
    IntPtr handle, icon;
    uint taskbarCreated;
    bool registered, disposed;
    int pending;
    public NotificationDelivery LastDelivery { get; private set; }
    public bool HasPendingNotification => registered;

    public WindowsNotifications(Window owner, Action onClick, Func<bool> enabled)
        : this(owner, onClick, enabled, () => DateTime.UtcNow) { }

    internal WindowsNotifications(Window owner, Action onClick, Func<bool> enabled, Func<DateTime> now)
    {
        this.owner = owner; this.onClick = onClick; this.enabled = enabled; this.now = now;
        owner.Dispatcher.VerifyAccess();
        watchdog = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromSeconds(45) };
        watchdog.Tick += WatchdogExpired;
        owner.SourceInitialized += OwnerInitialized;
        owner.Closed += OwnerClosed;
        AttachWindow();
    }

    /// <returns>True only when Windows accepted the native notification request.
    /// It does not guarantee that Focus Assist permits a visible banner.</returns>
    public bool Show(DownloadNotice notice)
    {
        owner.Dispatcher.VerifyAccess();
        if (disposed) { LastDelivery = NotificationDelivery.Unavailable; return false; }
        if (!enabled()) { LastDelivery = NotificationDelivery.Disabled; ClearPending(); return false; }
        var stamp = now();
        var key = (notice.Kind, notice.DownloadId ?? "");
        if (delivered.TryGetValue(key, out var previous) &&
            (notice.Kind == DownloadNoticeKind.Completed || stamp - previous < TimeSpan.FromMinutes(5)))
        { LastDelivery = NotificationDelivery.Repeated; return false; }
        var title = notice.Kind switch
        {
            DownloadNoticeKind.Completed => "Загрузка завершена",
            DownloadNoticeKind.LowSpace => "Недостаточно места",
            _ => "Не удалось продолжить загрузку"
        };
        if (!Submit(title, FormatBody(notice))) return false;
        // Failed submissions and disabled notifications never consume an event.
        // Completed tasks are also deduplicated by DownloadService across restarts.
        delivered[key] = stamp;
        foreach (var stale in delivered.Where(x => x.Key.Kind != DownloadNoticeKind.Completed && stamp - x.Value >= TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray())
            delivered.Remove(stale);
        return true;
    }

    public bool Test()
    {
        owner.Dispatcher.VerifyAccess();
        if (disposed) { LastDelivery = NotificationDelivery.Unavailable; return false; }
        if (!enabled()) { LastDelivery = NotificationDelivery.Disabled; ClearPending(); return false; }
        return Submit("Уведомления Качалки", "Здесь появятся сообщения о завершении загрузки, ошибках и свободном месте. Нажми, чтобы открыть загрузки.");
    }

    public void Dismiss()
    {
        owner.Dispatcher.VerifyAccess();
        ClearPending();
    }

    static string FormatBody(DownloadNotice notice)
    {
        var name = Clean(notice.Name, 100);
        var message = Clean(notice.Message, 250);
        return Clean(name.Length == 0 ? message : message.Length == 0 ? name : name + "\n" + message, 255);
    }

    static string Clean(string? text, int length)
    {
        // Bound native fixed buffers, retain Russian text and avoid splitting a
        // surrogate pair when a long release name contains supplementary glyphs.
        var clean = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        if (clean.Length <= length) return clean;
        var end = Math.Max(0, length - 1);
        if (end > 0 && char.IsHighSurrogate(clean[end - 1])) end--;
        return clean[..end] + "…";
    }

    bool Submit(string title, string body)
    {
        try
        {
            AttachWindow();
            if (handle == IntPtr.Zero || source == null) return Unavailable("window-not-ready");
            if (icon == IntPtr.Zero) icon = LoadAppIcon();
            if (icon == IntPtr.Zero) return Unavailable("app-icon-unavailable");
            if (!registered)
            {
                var add = Data(); add.Flags = MessageFlag | IconFlag | TipFlag;
                if (!ShellNotifyIcon(Add, ref add)) return Unavailable("shell-not-available");
                registered = true;
                var version = Data(); version.TimeoutOrVersion = 4;
                // A rejected version negotiation still leaves a working legacy
                // callback; WndProc accepts both v3 and v4 message layouts.
                ShellNotifyIcon(SetVersion, ref version);
            }
            var balloon = Data(); balloon.Flags = InfoFlag;
            balloon.InfoTitle = Clean(title, 63); balloon.Info = Clean(body, 255);
            balloon.InfoFlags = UserIcon | LargeIcon;
            balloon.BalloonIcon = icon;
            if (!ShellNotifyIcon(Modify, ref balloon)) { ClearPending(); return Unavailable("shell-rejected-notification"); }
            pending++;
            // Explorer queues notices; do not use NIF_REALTIME, which silently
            // drops a completion while a preceding notification is visible.
            watchdog.Stop(); watchdog.Start();
            LastDelivery = NotificationDelivery.Submitted;
            DiagnosticLog.Write("notification-submitted", new { Channel = "WindowsShell", VisibilityControlledByWindows = true });
            return true;
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or MissingManifestResourceException or DllNotFoundException or EntryPointNotFoundException)
        {
            ClearPending(); ErrorLog.Write(error); return Unavailable(error.GetType().Name);
        }
    }

    bool Unavailable(string reason)
    {
        LastDelivery = NotificationDelivery.Unavailable;
        DiagnosticLog.Write("notification-unavailable", new { Channel = "WindowsShell", Reason = reason, Win32Error = Marshal.GetLastWin32Error() });
        return false;
    }

    void OwnerInitialized(object? sender, EventArgs e) => AttachWindow();
    void OwnerClosed(object? sender, EventArgs e) => Dispose();
    void WatchdogExpired(object? sender, EventArgs e) => ClearPending();

    void AttachWindow()
    {
        if (disposed || source != null) return;
        handle = new WindowInteropHelper(owner).Handle;
        if (handle == IntPtr.Zero) return;
        source = HwndSource.FromHwnd(handle);
        if (source == null) return;
        source.AddHook(WindowMessage);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        // Explicit identity is restricted to this process. It does not create
        // COM registrations, modify Open-Shell or change existing shortcuts.
        SetCurrentProcessExplicitAppUserModelID(AppId);
    }

    IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            // Explorer restarted: its old entry is gone. The next notice will
            // add a new one; never recreate a permanent icon after a restart.
            registered = false; pending = 0; watchdog.Stop();
            return IntPtr.Zero;
        }
        if (message != CallbackMessage) return IntPtr.Zero;
        if (!registered) return IntPtr.Zero;
        var code = (int)(lParam.ToInt64() & 0xffff);
        if (code == BalloonShow) { watchdog.Stop(); watchdog.Start(); }
        if (code is BalloonHide or BalloonTimeout or BalloonClick)
        {
            pending = Math.Max(0, pending - 1);
            if (pending == 0) ClearPending();
            else { watchdog.Stop(); watchdog.Start(); }
        }
        if (code == BalloonClick && !disposed)
        {
            // Let the native callback return first. No file, external URL or
            // release-provided data ever participates in activation.
            _ = owner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { if (!disposed && enabled()) onClick(); }));
        }
        handled = true;
        return IntPtr.Zero;
    }

    NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1,
        CallbackMessage = CallbackMessage, Icon = icon, Tip = "Качалка",
        Info = "", InfoTitle = "", Guid = IconId
    };

    void ClearPending()
    {
        watchdog.Stop();
        if (registered) { var data = Data(); ShellNotifyIcon(Delete, ref data); registered = false; }
        pending = 0;
    }

    public void Dispose()
    {
        owner.Dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true; ClearPending();
        watchdog.Tick -= WatchdogExpired;
        owner.SourceInitialized -= OwnerInitialized; owner.Closed -= OwnerClosed;
        if (source != null) { source.RemoveHook(WindowMessage); source = null; }
        if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
    }

    static IntPtr LoadAppIcon()
    {
        if (File.Exists(WindowsIntegration.Executable))
        {
            var large = new IntPtr[1]; var small = new IntPtr[1];
            ExtractIconEx(WindowsIntegration.Executable, 0, large, small, 1);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
            if (large[0] != IntPtr.Zero) return large[0];
        }
        // Also works from the test host / dotnet DLL: use the same embedded icon
        // as the published EXE, rather than a generic system information icon.
        var resources = new ResourceManager("Kachalka.g", typeof(WindowsNotifications).Assembly);
        using var stream = resources.GetStream("assets/app.ico");
        if (stream == null) return IntPtr.Zero;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var data = bytes.ToArray();
        if (data.Length < 6 || BitConverter.ToUInt16(data, 2) != 1) return IntPtr.Zero;
        var count = BitConverter.ToUInt16(data, 4);
        if (data.Length < 6 + count * 16) return IntPtr.Zero;
        var best = -1; var score = int.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var entry = 6 + index * 16; var width = data[entry] == 0 ? 256 : data[entry];
            if (Math.Abs(width - 48) < score) { best = entry; score = Math.Abs(width - 48); }
        }
        if (best < 0) return IntPtr.Zero;
        var size = BitConverter.ToInt32(data, best + 8); var offset = BitConverter.ToInt32(data, best + 12);
        if (size <= 0 || offset < 0 || (long)offset + size > data.Length) return IntPtr.Zero;
        var payload = data.AsSpan(offset, size).ToArray();
        return CreateIconFromResourceEx(payload, (uint)payload.Length, true, 0x30000, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
}
