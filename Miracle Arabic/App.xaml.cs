using System.Runtime.InteropServices;
using System.Windows;

namespace Miracle_Arabic
{
    public partial class App : Application
    {
        private const string InstanceMutexName = "MiracleArabicFix.SingleInstance";
        private const string ShowEventName = "MiracleArabicFix.ShowWindow";

        private const int ASFW_ANY = -1;

        // Lets the running instance bring its window to the front when a new copy asks for it.
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private Mutex? _instanceMutex;
        private EventWaitHandle? _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                SignalRunningInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            base.OnStartup(e);

            var main = new Miracle.MainWindow();
            MainWindow = main;
            main.Show();
            ListenForShowRequests();
        }

        private static void SignalRunningInstance()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? ev) && ev != null)
                {
                    using (ev)
                    {
                        AllowSetForegroundWindow(ASFW_ANY);
                        ev.Set();
                        Thread.Sleep(150);
                    }
                }
            }
            catch { }
        }

        private void ListenForShowRequests()
        {
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_showEvent == null || !_showEvent.WaitOne()) continue;
                        Dispatcher.Invoke(() => (MainWindow as Miracle.MainWindow)?.ShowFromExternal());
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "MiracleShowListener" };
            listener.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showEvent?.Dispose();
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
