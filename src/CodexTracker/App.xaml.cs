namespace CodexTracker;
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _shutdownEvent;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var mutexName = GetSingleInstanceMutexName();
        if (e.Args.Contains("--shutdown-existing", StringComparer.OrdinalIgnoreCase))
        {
            SignalExistingInstanceShutdown(mutexName);
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(true, mutexName, out var firstInstance);
        if (!firstInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        var shutdownEventName = GetShutdownEventName();
        _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, shutdownEventName);
        _ = Task.Run(() =>
        {
            _shutdownEvent.WaitOne();
            Dispatcher.Invoke(Shutdown);
        });

        base.OnStartup(e);
        var window = new MainWindow(e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        window.Show();
    }

    private static void SignalExistingInstanceShutdown(string mutexName)
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(GetShutdownEventName());
            shutdownEvent.Set();
            using var mutex = Mutex.OpenExisting(mutexName);
            if (mutex.WaitOne(TimeSpan.FromSeconds(15))) mutex.ReleaseMutex();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (AbandonedMutexException) { }
    }

    private static string GetSingleInstanceMutexName()
    {
        var executablePath = System.IO.Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory).ToUpperInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executablePath));
        return @"Local\CodexTracker." + Convert.ToHexString(hash.AsSpan(0, 12));
    }

    private static string GetShutdownEventName() => GetSingleInstanceMutexName() + ".Shutdown";

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        _shutdownEvent?.Dispose();
        _shutdownEvent = null;
        base.OnExit(e);
    }
}
