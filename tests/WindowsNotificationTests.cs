using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Kachalka;

static class WindowsNotificationTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string message);

    public static async Task Run()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
                var enabled = false;
                var clock = DateTime.UtcNow;
                var activated = 0;
                var format = typeof(WindowsNotifications).GetMethod("FormatBody", BindingFlags.Static | BindingFlags.NonPublic)!;
                var hostile = new DownloadNotice(DownloadNoticeKind.Error, "long-notice", new string('я', 99) + "😀\0", new string('м', 300));
                var body = (string)format.Invoke(null, [hostile])!;
                Check(body.Length <= 255 && !body.Contains('\0') && !char.IsHighSurrogate(body[^1]) && !body.Contains('\ud83d'),
                    "native notification buffers bound long Russian names and control characters without splitting a supplementary glyph");
                window = new Window { Title = "Качалка: проверка уведомлений", Width = 200, Height = 100, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
                window.Show();
                var constructor = typeof(WindowsNotifications).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(Action), typeof(Func<bool>), typeof(Func<DateTime>)], null)!;
                using var channel = (WindowsNotifications)constructor.Invoke([window, (Action)(() => activated++), (Func<bool>)(() => enabled), (Func<DateTime>)(() => clock)]);
                Check(!channel.Test() && channel.LastDelivery == NotificationDelivery.Disabled && !channel.HasPendingNotification,
                    "disabled Windows notifications never create a tray icon or submit a banner");
                enabled = true;
                Check(channel.Test() && channel.LastDelivery == NotificationDelivery.Submitted && channel.HasPendingNotification,
                    "Windows Shell accepts a native notification with the real application icon (visible banners remain controlled by Focus Assist)");
                channel.Dismiss();
                Check(!channel.HasPendingNotification, "dismissing notifications removes the transient tray entry");
                var completion = new DownloadNotice(DownloadNoticeKind.Completed, "native-notice-test", "Проверка уведомлений", "Тестовый отчёт, без запуска загрузки.");
                Check(channel.Show(completion) && !channel.Show(completion) && channel.LastDelivery == NotificationDelivery.Repeated,
                    "completed download notifications are delivered once per task in a session");
                var error = completion with { Kind = DownloadNoticeKind.Error, Message = "Тестовая ошибка подключения." };
                Check(channel.Show(error) && !channel.Show(error), "repeated download errors are throttled per task");
                var other = error with { DownloadId = "other-native-notice-test" };
                Check(channel.Show(other), "one failing download does not suppress another download's warning");
                var space = completion with { Kind = DownloadNoticeKind.LowSpace, Message = "Тестовая проверка свободного места." };
                Check(channel.Show(space) && !channel.Show(space), "low-space notices use an independent per-task throttle");
                clock = clock.AddMinutes(6);
                Check(channel.Show(error) && !channel.Show(completion), "error notifications can be retried after five minutes while completion stays deduplicated");
                channel.Dismiss();
                enabled = false;
                Check(!channel.Show(other) && channel.LastDelivery == NotificationDelivery.Disabled && !channel.HasPendingNotification,
                    "turning notifications off cancels pending native notices");
                enabled = true;
                Check(channel.Test(), "test notification uses the same accepted native delivery channel");
                var handle = new WindowInteropHelper(window).Handle;
                // Remove only this fixture's Shell entry, then deliver the
                // message Windows sends after Explorer recreates its taskbar.
                var data = typeof(WindowsNotifications).GetMethod("Data", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(channel, null);
                var remove = typeof(WindowsNotifications).GetMethod("ShellNotifyIcon", BindingFlags.Static | BindingFlags.NonPublic)!;
                Check((bool)remove.Invoke(null, [(uint)2, data])!, "fixture Shell entry removed before testing a taskbar recreation");
                SendMessage(handle, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
                Check(!channel.HasPendingNotification && channel.Test(), "notification callback handles taskbar recreation and submits a new transient Shell entry");
                // Windows's version-4 notification callback: LOWORD is the event,
                // HIWORD is the icon ID. This tests the actual HwndSource hook,
                // rather than directly invoking an application event handler.
                SendMessage(handle, 0x8000 + 0x341, IntPtr.Zero, new IntPtr(0x10000 | 0x405));
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                var deadline = DateTime.UtcNow.AddSeconds(2);
                timer.Tick += (_, _) => { if (activated != 0 || DateTime.UtcNow > deadline) { timer.Stop(); frame.Continue = false; } };
                timer.Start(); Dispatcher.PushFrame(frame);
                Check(activated == 1 && !channel.HasPendingNotification, "native notification activation opens the existing Downloads view and clears the transient icon");
                Check(channel.Test(), "notification channel remains usable after activation");
                channel.Dispose();
                Check(!channel.HasPendingNotification && !channel.Test() && channel.LastDelivery == NotificationDelivery.Unavailable,
                    "closing the notification channel releases its Shell entry, native icon and HWND callback");
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
            finally { window?.Close(); }
        }) { IsBackground = true, Name = "Kachalka native notification smoke" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(20))) != done.Task)
            throw new TimeoutException("Native notification smoke did not complete within 20 seconds.");
        await done.Task;
    }
}
