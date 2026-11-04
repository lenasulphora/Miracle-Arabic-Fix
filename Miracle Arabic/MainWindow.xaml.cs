using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Miracle
{
    public partial class MainWindow : Window
    {
        public const string CurrentVersion = "1.4.1";
        private const double TransparentOpacity = 0.35;
        // Point this at your own version.json if you want the update checker to work.
        private const string UpdateUrl = "";
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOW = 5, SW_RESTORE = 9;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? c, string? w);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int m);
        // Owner of a window, so the hook can tell our own UI from anybody else's window.
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        // Physical key state, the same source a global hook sees, independent of who has focus.
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static string rootFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Miracle Arabic Fix");
        private string configPath = Path.Combine(rootFolder, "config.json");
        public Config Data { get; set; } = new Config();
        private DispatcherTimer clipTimer = new DispatcherTimer();
        private DispatcherTimer _saveTimer = new DispatcherTimer();
        private string lastClip = "";
        private int _clipIngestVersion;
        private CancellationTokenSource? _autoTranslateCts;
        private ClipboardMonitor? _clipMonitor;
        private readonly ClipboardGate _clipGate = new ClipboardGate();
        private readonly SemaphoreSlim _clipApplyGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _typeTimer;
        private readonly List<TextSpan> _preservedInputSpans = new();
        private string _trackedInputText = "";
        private SettingsWindow? _settingsWin;
        private TrayIcon? _tray;
        private bool _mini;
        private bool _exiting;
        // Serializes Easy Mode sends so two Enter presses cannot interleave.
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        // Title/handle the cached game handle belongs to; changing either invalidates the handle.
        private string _lastTargetTitle = "";
        private string _lastTargetExe = "";
        private readonly SendCycle _cycle = new SendCycle();
        // Cached "UI is Arabic" flag, for text this window draws outside settings.
        private bool _lang;
        private double _lastWidth;
        private readonly record struct TextSpan(int Start, int End);

        public MainWindow()
        {
            if (!Directory.Exists(rootFolder)) Directory.CreateDirectory(rootFolder);
            // Fresh error log for the "chat via game engine" route, plus the CSV decode list.
            BridgeErrorLog.StartSession(rootFolder);
            BridgeErrorCatalog.WriteCsvToDownloads();
            InitializeComponent();
            VerTag.Text = "v" + CurrentVersion;
            LoadSettings();
            AutoPickTargetIfEmpty();
            this.Closed += (s, e) =>
            {
                CancelAutoTranslate();
                UnhookWindowsHookEx(_hookID);
                if (_clipMonitor != null) _clipMonitor.ClipboardUpdated -= OnClipboardUpdated;
                _clipMonitor?.Dispose();
                clipTimer.Stop();
                _tray?.Dispose();
                Application.Current.Shutdown();
            };
            this.Closing += (s, e) => { if (_exiting) return; e.Cancel = true; HideToTray(); };
            _proc = HookCallback; _hookID = SetHook(_proc);
            _clipMonitor = new ClipboardMonitor();
            if (_clipMonitor.Start())
            {
                _clipMonitor.ClipboardUpdated += OnClipboardUpdated;
            }
            else
            {
                _clipMonitor.Dispose();
                _clipMonitor = null;
                clipTimer.Interval = TimeSpan.FromMilliseconds(500);
                clipTimer.Tick += async (s, e) => await OnClipboardObservationAsync();
                clipTimer.Start();
            }
            _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveSettings(); };
            this.SizeChanged += (s, e) =>
            {
                ScheduleAutoSave();
                // A width change rewraps the text, which changes the natural height.
                if (Math.Abs(ActualWidth - _lastWidth) > 0.5) { _lastWidth = ActualWidth; SyncSizing(); }
            };
            this.LocationChanged += (s, e) => ScheduleAutoSave();
            TrayDispatch.Register(action => Dispatcher.Invoke(action));
            _tray = new TrayIcon("Miracle Arabic Fix", new TrayFlags
            {
                Mini = Data.Mini,
                Translate = Data.Translate,
                Easy = Data.Easy,
                Correct = Data.Trans,
                Transparent = Data.Transparent
            });
            _tray.OnToggle = () => Dispatcher.Invoke(ToggleWindowVisibility);
            _tray.OnExit = () => Dispatcher.Invoke(() => { _exiting = true; Application.Current.Shutdown(); });
            _tray.OnFlagsChanged = f => Dispatcher.Invoke(() => ApplyTrayFlags(f));
            _tray.SyncChecks(Data.Mini, Data.Translate, Data.Easy, Data.Trans, Data.Transparent);
            this.IsVisibleChanged += (s, e) => _tray?.SetShownLabel(IsVisible);
            InBox.GotKeyboardFocus += (s, e) => InGlowOn(true);
            InBox.LostKeyboardFocus += (s, e) => InGlowOn(false);
            BtnTranslate.Unchecked += (s, e) => CancelAutoTranslate();
            BtnCorrect.Unchecked += (s, e) => CancelAutoTranslate();
            _ = Task.Run(() => CheckForUpdatesAsync(false));
            // Snap the height to the exact natural value once real text measurements exist.
            Loaded += (s, e) => SyncSizing();
        }

        private void TrackPreservedInputChanges(string text)
        {
            string previous = _trackedInputText;
            if (previous == text) return;

            int prefix = 0;
            while (prefix < previous.Length && prefix < text.Length && previous[prefix] == text[prefix])
                prefix++;

            int suffix = 0;
            while (suffix < previous.Length - prefix && suffix < text.Length - prefix
                   && previous[previous.Length - suffix - 1] == text[text.Length - suffix - 1])
                suffix++;

            int oldEnd = previous.Length - suffix;
            int newEnd = text.Length - suffix;
            int delta = text.Length - previous.Length;
            var updated = new List<TextSpan>(_preservedInputSpans.Count + 1);

            foreach (var span in _preservedInputSpans)
            {
                if (span.End <= prefix)
                {
                    updated.Add(span);
                }
                else if (span.Start >= oldEnd)
                {
                    updated.Add(new TextSpan(span.Start + delta, span.End + delta));
                }
                else
                {
                    if (span.Start < prefix)
                        updated.Add(new TextSpan(span.Start, prefix));
                    if (span.End > oldEnd)
                        updated.Add(new TextSpan(newEnd, span.End + delta));
                }
            }

            _preservedInputSpans.Clear();
            _preservedInputSpans.AddRange(updated);
            _trackedInputText = text;
        }

        private async Task<string> FixInputPreservingMacrosAsync(string input)
        {
            var spans = _preservedInputSpans
                .Where(span => span.Start >= 0 && span.End > span.Start && span.End <= input.Length)
                .OrderBy(span => span.Start)
                .ToList();
            if (spans.Count == 0) return await ArabicEngine.Fix(input);

            var result = new System.Text.StringBuilder(input.Length);
            int position = 0;
            foreach (var span in spans)
            {
                if (span.Start < position) continue;
                if (span.Start > position)
                    result.Append(await ArabicEngine.Fix(input.Substring(position, span.Start - position)));
                result.Append(input, span.Start, span.End - span.Start);
                position = span.End;
            }
            if (position < input.Length)
                result.Append(await ArabicEngine.Fix(input.Substring(position)));
            return result.ToString();
        }

        private void ScheduleAutoSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        private async void OnClipboardUpdated() => await OnClipboardObservationAsync();

        private async Task OnClipboardObservationAsync()
        {
            try
            {
                uint sequence = ClipboardWorker.SequenceNumber();
                bool copiedInGame = BtnCorrect.IsChecked == true
                                    && (!string.IsNullOrEmpty(Data.Target) || !string.IsNullOrEmpty(Data.GameExe))
                                    && IsGameForeground();
                await ApplyClipboardUpdateAsync(sequence, copiedInGame);
            }
            catch { }
        }

        private async Task ApplyClipboardUpdateAsync(uint sequence, bool copiedInGame)
        {
            await _clipApplyGate.WaitAsync();
            try
            {
                if (!_clipGate.Observe(sequence, copiedInGame)) return;

                string? text = await ClipboardWorker.GetTextAsync();
                if (string.IsNullOrEmpty(text) || text == lastClip) return;

                if (sequence != 0 && sequence != ClipboardWorker.SequenceNumber()) return;

                lastClip = text;

                if (BtnTranslate.IsChecked == true)
                {
                    StartAutoTranslate(text);
                }
                else
                {
                    // A translation from an earlier mode may still be live; this capture is newer.
                    _clipIngestVersion++;
                    InBox.Text = text;
                }
            }
            catch { }
            finally { _clipApplyGate.Release(); }
        }

        private void StartAutoTranslate(string raw)
        {
            CancelAutoTranslate();
            var cts = new CancellationTokenSource();
            _autoTranslateCts = cts;
            int version = ++_clipIngestVersion;
            _ = RunAutoTranslateAsync(raw, version, cts);
        }

        private void CancelAutoTranslate()
        {
            // Invalidate any request that outlived its cancellation and was not applied yet.
            _clipIngestVersion++;
            var cts = _autoTranslateCts;
            _autoTranslateCts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            // The owning task releases it in its finally block.
        }

        private async Task RunAutoTranslateAsync(string raw, int version, CancellationTokenSource cts)
        {
            CancellationToken token = cts.Token;
            string logical = string.Empty;
            string targetLanguage = Data.SrcLang;
            string provider = Data.Translator;
            string? deepLKey = Data.DeepLKey;
            try
            {
                logical = await ArabicEngine.ToLogicalTextAsync(raw);
                if (version != _clipIngestVersion) return;

                if (!string.Equals(InBox.Text, logical, StringComparison.Ordinal))
                    InBox.Text = logical;

                if (TextLanguage.IsUnfitForTranslation(logical)) return;
                if (TextLanguage.IsSameLanguageByScript(targetLanguage, logical)) return;

                AutoTranslation? result = await TranslateService.TranslateAutoAsync(
                    logical, targetLanguage, provider, deepLKey, token);

                if (version != _clipIngestVersion) return;
                if (!string.Equals(targetLanguage, Data.SrcLang, StringComparison.OrdinalIgnoreCase))
                    return;

                if (result is not { } translation)
                {
                    if (string.Equals(InBox.Text, logical, StringComparison.Ordinal))
                        Toast(_lang
                            ? "تعذرت ترجمة النص — تم عرض النص الأصلي ✕"
                            : "COULD NOT TRANSLATE — SHOWING THE ORIGINAL ✕");
                    return;
                }

                if (TextLanguage.CodesMatch(translation.DetectedLanguage, targetLanguage)
                    && !TextLanguage.ScriptClearlyDiffers(targetLanguage, logical)) return;
                if (string.IsNullOrWhiteSpace(translation.Text)) return;
                if (string.Equals(translation.Text, logical, StringComparison.Ordinal)) return;

                // Never overwrite what the user typed while the request was running.
                if (!TextLanguage.CanApplyLateTranslation(InBox.Text, logical, version, _clipIngestVersion))
                    return;

                InBox.Text = translation.Text;
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (version != _clipIngestVersion) return;
                bool recoveredRaw = false;
                if (string.IsNullOrEmpty(logical) && InBox.Text.Length == 0 && raw.Length > 0)
                {
                    InBox.Text = raw;
                    recoveredRaw = true;
                }
                if (recoveredRaw || string.Equals(InBox.Text, logical, StringComparison.Ordinal))
                    Toast(_lang
                        ? "تعذرت ترجمة النص — تم عرض النص الأصلي ✕"
                        : "COULD NOT TRANSLATE — SHOWING THE ORIGINAL ✕");
            }
            finally
            {
                if (ReferenceEquals(_autoTranslateCts, cts)) _autoTranslateCts = null;
                cts.Dispose();
            }
        }

        // True when the selected game window is currently in the foreground.
        private bool IsGameForeground()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;

            IntPtr target = GameSender.ResolveTarget(Data.Target, Data.GameExe);
            if (target != IntPtr.Zero) return h == target;

            if (!string.IsNullOrWhiteSpace(Data.GameExe)) return false;

            var title = new System.Text.StringBuilder(256);
            return GetWindowText(h, title, 256) > 0 && title.ToString() == Data.Target;
        }

        private bool IsTargetForeground(IntPtr h)
        {
            if (string.IsNullOrEmpty(Data.Target) && string.IsNullOrEmpty(Data.GameExe)) return false;

            IntPtr target = GameSender.ResolveTarget(Data.Target, Data.GameExe);
            if (target != IntPtr.Zero) return h == target;

            // An install identity is saved but its game is not running: no title fallback.
            if (!string.IsNullOrWhiteSpace(Data.GameExe)) return false;

            var title = new System.Text.StringBuilder(256);
            return GetWindowText(h, title, 256) > 0 && title.ToString().Trim() == Data.Target;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
                return SetWindowsHookEx(13, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)0x0100)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                string k = ((Key)KeyInterop.KeyFromVirtualKey(vkCode)).ToString();
                if (!string.IsNullOrEmpty(Data.HideKey2) && Data.HideKey2 != "None" && k == Data.HideKey2
                    && IsHoldDown(Data.HideKey1))
                {
                    Dispatcher.Invoke(ToggleWindowVisibility);
                    return (IntPtr)1;
                }
                if (ShouldServeMacros(k) && TryServeMacro(k)) return (IntPtr)1;
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private bool ShouldServeMacros(string k)
        {
            if (string.IsNullOrEmpty(k) || k == "None") return false;
            if (!Data.Shortcuts.Any(x => !string.IsNullOrEmpty(x.Key2) && x.Key2 != "None" && x.Key2 == k)) return false;
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || IsOwnWindow(h)) return false;
            return Data.GlobalShortcuts && IsTargetForeground(h);
        }

        // True when the window belongs to this process: tool window, settings or hotkeys window.
        private static bool IsOwnWindow(IntPtr h)
        {
            GetWindowThreadProcessId(h, out uint pid);
            return pid != 0 && pid == (uint)Environment.ProcessId;
        }

        private static bool IsHoldDown(string? name)
        {
            if (string.IsNullOrEmpty(name) || name == "None") return true;
            if (!Enum.TryParse<Key>(name, out Key key)) return false;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private bool TryServeMacro(string k)
        {
            foreach (var sc in Data.Shortcuts.Where(x => x.Key2 == k))
            {
                if (IsHoldDown(sc.Key1))
                {
                    _ = Task.Run(async () =>
                    {
                        string f = sc.KeepTextAsWritten ? sc.TextToPaste : await ArabicEngine.Fix(sc.TextToPaste);
                        Dispatcher.Invoke(() => lastClip = f);
                        await ClipboardWorker.SetTextAsync(f);
                        GameSender.PasteIntoForeground();
                    });
                    return true;
                }
            }
            return false;
        }

        private void OpenSettings_Click(object s, RoutedEventArgs e)
        {
            if (_settingsWin != null && _settingsWin.IsLoaded) { _settingsWin.Activate(); return; }
            _settingsWin = new SettingsWindow(this) { Owner = this };
            _settingsWin.Closed += (sender, args) =>
            {
                LoadSettings();
                ReFitLater();
                _settingsWin = null;
            };
            _settingsWin.Show();
        }

        public class UpdateData { public string version { get; set; } = ""; public string url { get; set; } = ""; }
        public class RootUpdate { public UpdateData? miracle_arabic_fix { get; set; } }
        public async Task CheckForUpdatesAsync(bool manual)
        {
            if (string.IsNullOrWhiteSpace(UpdateUrl))
            {
                if (manual) Dispatcher.Invoke(() => NoticeBox.Show((Data.Lang == "AR") ? "نظام ميراكل" : "MIRACLE SYSTEM", (Data.Lang == "AR") ? "فحص التحديثات غير مُفعّل في هذه النسخة." : "Update checking is not configured in this build.", Data.Lang, "❖"));
                return;
            }
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    string json = await client.GetStringAsync(UpdateUrl + "?t=" + DateTime.Now.Ticks);
                    var root = JsonSerializer.Deserialize<RootUpdate>(json);
                    var update = root?.miracle_arabic_fix;
                    if (update != null && new Version(update.version) > new Version(CurrentVersion))
                    {
                        Dispatcher.Invoke(() => {
                            string title = (Data.Lang == "AR") ? "تحديث متاح" : "UPDATE AVAILABLE";
                            string msg = (Data.Lang == "AR") ? $"إصدار جديد {update.version} متوفر الآن. هل تريد التحديث؟" : $"A new version {update.version} is out! Update?";
                            if (NoticeBox.Show(title, msg, Data.Lang, "↑", true)) Process.Start(new ProcessStartInfo(update.url) { UseShellExecute = true });
                        });
                    }
                    else if (manual) Dispatcher.Invoke(() => NoticeBox.Show((Data.Lang == "AR") ? "نظام ميراكل" : "MIRACLE SYSTEM", (Data.Lang == "AR") ? "أنت تستخدم أحدث إصدار بالفعل." : "Latest version active.", Data.Lang, "❖"));
                }
            }
            catch { if (manual) Dispatcher.Invoke(() => MessageBox.Show("Error")); }
        }

        private void Window_MouseDown(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void InBox_TextChanged(object s, TextChangedEventArgs e)
        {
            if (InBox.Text.Length == 0)
            {
                CancelAutoTranslate();
            }
            TrackPreservedInputChanges(InBox.Text);
            _typeTimer?.Cancel();
            var cts = new CancellationTokenSource();
            _typeTimer = cts;
            if (_cycle.RecoverUncertain)
            {
                if (InBox.Text.Length > 0) HideRestorePill();
                else ShowRestorePill();
            }
            _ = RefreshPreviewAsync(cts.Token);
        }
        private async Task RefreshPreviewAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested) return;
                string preview = _mini ? "" : await FixInputPreservingMacrosAsync(InBox.Text);
                // The box may have changed during Fix(); a newer preview now owns it.
                if (token.IsCancellationRequested) return;
                OutBox.Text = preview;
                SyncSizing();
            }
            catch { }
        }
        private void SyncSizing()
        {

            // Nothing is synced while minimized/restored, or before the visual tree is measured.
            if (!IsLoaded || WindowState != WindowState.Normal || ActualHeight <= 0) return;

            Grid? shell = Shell.Child as Grid;
            Grid? body = shell?.Children.OfType<Grid>().FirstOrDefault(g => g.RowDefinitions.Count > 1);
            if (body == null) return;

            body.Measure(new Size(MeasureWidth(body), double.PositiveInfinity));
            double need = body.DesiredSize.Height;
            body.InvalidateMeasure();
            if (double.IsNaN(need) || need <= 0) return;

            double h = Math.Max(MinHeight, Math.Min(1200, need));
            if (Math.Abs(Height - h) > 0.5) Height = h;
        }

        public void ReFitLater()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SyncSizing();
                    Dispatcher.BeginInvoke(new Action(SyncSizing), DispatcherPriority.Loaded);
                }), DispatcherPriority.ContextIdle);
            }), DispatcherPriority.ApplicationIdle);
        }

        private double MeasureWidth(FrameworkElement body)
        {
            double w = (Shell.Child as FrameworkElement)?.ActualWidth ?? ActualWidth;
            if (double.IsNaN(w) || w <= 0) w = ActualWidth;
            double m = body.Margin.Left + body.Margin.Right;
            w -= m;
            if (double.IsNaN(w) || w <= 0) w = Width;
            return w;
        }

        private void InGlowOn(bool on) => InGlow.Opacity = on ? 1 : 0;

        private async Task SendToGameAsync(string outText, string sourceText, string typedText, long cycle)
        {
            string target = Data.Target;
            string gameExe = Data.GameExe;
            if (target != _lastTargetTitle || gameExe != _lastTargetExe)
            { GameSender.ForgetTarget(); _lastTargetTitle = target; _lastTargetExe = gameExe; }

            SendResult result;
            if (GameSender.ResolveTarget(target, gameExe) == IntPtr.Zero)
            {
                result = SendResult.TargetNotFound;
            }
            else
            {
                try
                {
                    result = await GameSender.SendAsync(
                        outText,
                        Data.SendMode,
                        Data.Speed,
                        t => { HideTool(); return Task.CompletedTask; },
                        () => { RestoreAfterSend(); return Task.CompletedTask; },
                        typedText,
                        sourceText);
                }
                catch { result = SendResult.Failed; }
            }

            SettleSend(sourceText, cycle, SendOutcome.From(result));
        }

        private void SettleSend(string sourceText, long cycle, SendOutcome outcome)
        {
            CycleResult v = _cycle.Complete(cycle, sourceText, outcome);

            if (v.Stale) return;

            if (v.Confirmed)
            {
                HideRestorePill();
                Toast(_lang
                    ? (v.Notice == CycleNotice.SentDirect ? "تم الإرسال (مباشر) ✔" : "تم الإرسال إلى اللعبة ✔")
                    : (v.Notice == CycleNotice.SentDirect ? "SENT (DIRECT) ✔" : "SENT TO GAME ✔"));
                return;
            }

            switch (v.Notice)
            {
                case CycleNotice.TargetMissing: Toast(_lang ? "لم يتم العثور على نافذة اللعبة ✕" : "GAME WINDOW NOT FOUND ✕"); break;
                case CycleNotice.FocusFailed: Toast(_lang ? "تعذر تفعيل نافذة اللعبة ✕" : "COULD NOT FOCUS GAME ✕"); break;
                case CycleNotice.Unverified: Toast(_lang ? "تحقق يدوي؟ ↺" : "UNVERIFIED ↺"); break;
                default: Toast(_lang ? "فشل الإرسال إلى اللعبة ✕" : "SEND TO GAME FAILED ✕"); break;
            }

            if (v.Refill != null)
            {
                if (InBox.Text.Length == 0)
                {
                    InBox.Text = v.Refill;
                }
                HideRestorePill();
                return;
            }

            // Uncertain delivery: show the restore pill instead of silently re-arming the message.
            if (v.Uncertain) ShowRestorePill();
        }

        // Move the app aside so the game can activate without a focus fight.
        private void HideTool() { if (IsVisible) Hide(); }

        private void RestoreAfterSend()
        {
            if (!IsVisible) Show();
            if (!WindowState.Equals(WindowState.Minimized)) Activate();
            if (IsVisible) InBox.Focus();
        }

        private void Toast(string message) => ShowNotification(message);

        private async void InBox_PreviewKeyDown(object s, KeyEventArgs e)
        {
            string key = (e.Key == Key.System ? e.SystemKey : e.Key).ToString();
            foreach (var sc in Data.Shortcuts.Where(x => !string.IsNullOrEmpty(x.Key2) && x.Key2 != "None"))
            {
                if (key == sc.Key2 && (string.IsNullOrEmpty(sc.Key1) || sc.Key1 == "None" || Keyboard.IsKeyDown((Key)Enum.Parse(typeof(Key), sc.Key1))))
                {
                    e.Handled = true;
                    int caret = InBox.SelectionStart;
                    string macroText = sc.TextToPaste ?? "";
                    InBox.Text = InBox.Text.Insert(caret, macroText);
                    InBox.SelectionStart = caret + macroText.Length;
                    if (sc.KeepTextAsWritten && macroText.Length > 0)
                        _preservedInputSpans.Add(new TextSpan(caret, caret + macroText.Length));
                    return;
                }
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                string input = InBox.Text;
                if (string.IsNullOrWhiteSpace(input)) return;

                if (!await _sendGate.WaitAsync(TimeSpan.FromSeconds(1))) { Toast(_lang ? "هناك عملية إرسال جارية ✕" : "A SEND IS ALREADY IN PROGRESS ✕"); return; }
                bool easy = BtnEasy.IsChecked == true
                            && (!string.IsNullOrEmpty(Data.Target) || !string.IsNullOrEmpty(Data.GameExe));
                try
                {
                    string sourceText;
                    if (Data.Translate)
                    {
                        string? translated = await TranslateService.TranslateAsync(input, Data.SrcLang, Data.DstLang, Data.Translator, Data.DeepLKey);
                        if (string.IsNullOrWhiteSpace(translated))
                        {
                            Toast(_lang ? "فشلت الترجمة — تحقق من الاتصال أو المفتاح ✕" : "TRANSLATION FAILED — CHECK CONNECTION OR API KEY ✕");
                            return;   // The box is untouched, so the user can simply retry
                        }
                        sourceText = translated;
                    }
                    else
                    {
                        sourceText = input;
                    }

                    string outText = string.Equals(sourceText, input, StringComparison.Ordinal)
                        ? await FixInputPreservingMacrosAsync(input)
                        : await ArabicEngine.Fix(sourceText);
                    if (outText.Length == 0) return;

                    string previousClip = lastClip;
                    lastClip = outText;
                    if (!await ClipboardWorker.SetTextAsync(outText))
                    {
                        lastClip = previousClip;
                        Toast(_lang ? "تعذر نسخ النص إلى الحافظة ✕" : "COULD NOT COPY TEXT TO CLIPBOARD ✕");
                        return;
                    }

                    InBox.Clear(); OutBox.Text = ""; SyncSizing(); if (IsVisible) InBox.Focus();

                    if (easy)
                    {
                        long cycle = _cycle.Begin();
                        await SendToGameAsync(outText, sourceText, input, cycle);
                    }
                    else Toast(Data.Translate
                        ? (_lang ? "تمت الترجمة والنسخ — لم يُرسل للعبة" : "TRANSLATED AND COPIED — NOT SENT TO GAME")
                        : (_lang ? "تم النسخ فقط — لم يُرسل للعبة" : "COPIED ONLY — NOT SENT TO GAME"));
                }
                finally { _sendGate.Release(); }
            }
        }

        public void ApplyTextDirection()
        {
            FlowDirection dir = Data.RtlSupport ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            InBox.FlowDirection = dir;
            OutBox.FlowDirection = dir;
        }

        public void ApplyMiniMode(bool mini)
        {
            _mini = mini;
            OutBorder.Visibility = FooterArea.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
            if (mini) OutBox.Text = "";
            SyncSizing();
        }

        private void ApplyTrayFlags(TrayFlags f)
        {
            Data.Mini = f.Mini;
            Data.Easy = f.Easy;
            Data.Trans = f.Correct;
            Data.Transparent = f.Transparent;
            Data.Translate = f.Translate;
            BtnEasy.IsChecked = f.Easy;
            BtnCorrect.IsChecked = f.Correct;
            BtnTranslate.IsChecked = f.Translate;
            ApplyMiniMode(f.Mini);
            ApplyTransparent(f.Transparent);
            _tray?.SyncChecks(_mini, Data.Translate, Data.Easy, Data.Trans, Data.Transparent);
            SaveSettings();
        }

        public void ApplyTransparent(bool on)
        {
            Data.Transparent = on;
            Opacity = on ? TransparentOpacity : 1.0;
        }

        public void ShowFromExternal()
        {
            ShowInTaskbar = true;
            IntPtr h = new WindowInteropHelper(this).Handle;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, WindowState == WindowState.Normal ? SW_SHOW : SW_RESTORE);
                SetForegroundWindow(h);
            }
            Activate();
            Topmost = false; Topmost = true;
            InBox.Focus();
            _tray?.SetShownLabel(true);
        }

        public void SaveSettings()
        {
            Rect b = (WindowState == WindowState.Normal) ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            Data.WinW = b.Width; Data.WinH = b.Height; Data.WinX = b.Left; Data.WinY = b.Top;
            Data.Easy = BtnEasy.IsChecked == true; Data.Trans = BtnCorrect.IsChecked == true; Data.Translate = BtnTranslate.IsChecked == true;
            Data.Mini = _mini;
            Data.Transparent = Opacity < 1.0;
            try { Data.DeepLKeyProtected = SecretProtection.Protect(Data.DeepLKey); }
            catch (CryptographicException)
            {
                ShowNotification(Data.Lang == "AR" ? "تعذر حماية مفتاح DeepL — لم يتم حفظ الإعدادات ✕" : "Could not protect the DeepL key; settings were not saved ✕");
                return;
            }
            if (Data.Mini) File.WriteAllText(configPath, JsonSerializer.Serialize(Data));
            else
            {
                double h = Data.WinH;
                Data.WinH = 0;
                File.WriteAllText(configPath, JsonSerializer.Serialize(Data));
                Data.WinH = h;
            }
        }
        public void LoadSettings()
        {
            if (File.Exists(configPath))
            {
                try
                {
                    Config loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) ?? Data;
                    if (!string.IsNullOrEmpty(loaded.DeepLKeyProtected))
                    {
                        try { loaded.DeepLKey = SecretProtection.Unprotect(loaded.DeepLKeyProtected); }
                        catch (CryptographicException)
                        {
                            loaded.DeepLKey = "";
                            loaded.DeepLKeyProtected = "";
                            Dispatcher.BeginInvoke(new Action(() => ShowNotification(
                                Data.Lang == "AR" ? "تعذر فك تشفير مفتاح DeepL المحفوظ ✕" : "Could not decrypt the saved DeepL key ✕")));
                        }
                    }
                    Data = loaded;
                    if (Data.SendMode == SendMode.ChatDirect) Data.SendMode = SendMode.Bridge;
                    // The direct-paste route is no longer offered, so an old choice becomes Automatic.
                    if (Data.SendMode == SendMode.Direct) Data.SendMode = SendMode.Auto;
                }
                catch (JsonException)
                {
                    ShowNotification(Data.Lang == "AR" ? "ملف الإعدادات غير صالح ✕" : "Settings file is invalid ✕");
                }
                catch (IOException)
                {
                    ShowNotification(Data.Lang == "AR" ? "تعذر قراءة ملف الإعدادات ✕" : "Could not read settings file ✕");
                }
            }
            InBox.FontSize = OutBox.FontSize = Data.FS;
            Width = Data.WinW > 0 ? Data.WinW : 452;
            Height = Data.WinH > 0 ? Data.WinH : 150;
            if (Data.WinX is double x && Data.WinY is double y &&
                x >= SystemParameters.VirtualScreenLeft && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                y >= SystemParameters.VirtualScreenTop && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = x; Top = y; }
            BtnEasy.IsChecked = Data.Easy; BtnCorrect.IsChecked = Data.Trans; BtnTranslate.IsChecked = Data.Translate;
            ApplyLanguageToolTips();
            ApplyTextDirection();
            ApplyMiniMode(Data.Mini);
            ApplyTransparent(Data.Transparent);
            _tray?.SyncChecks(_mini, Data.Translate, Data.Easy, Data.Trans, Data.Transparent);
        }

        private void AutoPickTargetIfEmpty()
        {
            if (!string.IsNullOrWhiteSpace(Data.Target) || !string.IsNullOrWhiteSpace(Data.GameExe)) return;
            var games = GameLibrary.DetectRunningGames();
            if (games.Count == 0) return;
            Data.Target = games[0].WindowTitle;
            Data.GameExe = games[0].ExePath;
            GameSender.ForgetTarget();
            // Persist through the existing debounced save, once startup layout is settled.
            Dispatcher.BeginInvoke(new Action(ScheduleAutoSave), DispatcherPriority.ApplicationIdle);
        }

        private void ApplyLanguageToolTips()
        {
            bool ar = (Data.Lang == "AR");
            _lang = ar;
            TT_Easy.Content = ar ? "  يجب ات يكون شات اللعبه بوضع الكتابه اثناء الاستعمال مسؤولة عن إرسال العربي المكتوب إلى الويندو الخاصة باللعبة (يجب اختيارها بالإعدادات)" : "Sends fixed Arabic text directly to the game window (Target must be set in settings).";
            TT_Correct.Content = ar
                ? "عندما تفعل هذه الميزة سوف تستقبل أي نص عربي معكوس منسوخ من اللعبة وسيتم إصلاحه تلقائياً هنا. مع تفعيل TRANSLATE يُترجم النص الأجنبي المنسوخ إلى لغة الإدخال."
                : "Fixes reversed Arabic copied text from the game automatically in this area. With TRANSLATE on, copied foreign text is translated into your input language.";
            string from = Languages.Get(Data.SrcLang).Name(ar), to = Languages.Get(Data.DstLang).Name(ar);
            TT_Translate.Content = ar
                ? $"عند تفعيل هذه الميزة يتم إرسال نصك للترجمة من {from} إلى {to} ثم يُنسخ الناتج في الكليبورد (تختار اللغتين من الإعدادات). مع تفعيل CORRECT يُترجم نص اللعبة المنسوخ تلقائياً إلى {from}."
                : $"Translates what you type from {from} to {to} and copies the result to the clipboard (choose both languages in settings). With CORRECT on, game text you copy is translated automatically into {from}.";
            TT_Clear.Content = ar ? "حذف محتوى مكان الكتابة" : "Delete everything in the typing area.";
            RestorePillText.Text = ar ? "استرجاع" : "RESTORE";
            TT_Restore.Content = _cycle.RecoverUncertain
                ? (ar ? "لم نتأكد أن اللعبة استلمت الرسالة الأخيرة. اضغط لاسترجاعها إلى مكان الكتابة (قد تكون وصلت بالفعل)."
                      : "Delivery of the last message could not be confirmed. Click to pull it back into the typing area - it may already have arrived.")
                : (ar ? "استرجاع آخر رسالة لم يتم إرسالها" : "Brings back the last message that was not delivered.");
        }

        public void UpdateFont(double v)
        {
            InBox.FontSize = OutBox.FontSize = Data.FS = v;
            SyncSizing();
            ReFitLater();
        }
        public async void ShowNotification(string message = "COPIED ✔")
        {
            string t = message.Trim();
            bool bad = t.EndsWith("✕");
            ToastIcon.Text = bad ? "⚠" : "✔";
            ToastIcon.Foreground = bad
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x63))
                : new SolidColorBrush(Color.FromRgb(0xD8, 0xB4, 0xFE));
            ToastText.Text = t;
            ToastHost.BorderBrush = bad
                ? new SolidColorBrush(Color.FromRgb(0x8A, 0x2A, 0x2A))
                : (Brush)FindResource("B_Accent");
            ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            await Task.Delay(1500);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
            fade.Completed += (s, e) => ToastHost.BeginAnimation(OpacityProperty, null);
            ToastHost.BeginAnimation(OpacityProperty, fade);
        }
        public void ToggleTranslate_Click(object s, RoutedEventArgs e)
        {
            Data.Translate = BtnTranslate.IsChecked == true;
            _tray?.SyncChecks(_mini, Data.Translate, Data.Easy, Data.Trans, Data.Transparent);
            SaveSettings();
        }
        private void HideToTray() { Hide(); ShowInTaskbar = false; }
        private void ToggleWindowVisibility()
        {
            if (IsVisible && WindowState != WindowState.Minimized) HideToTray();
            else { ShowInTaskbar = true; WindowState = WindowState.Normal; Show(); Activate(); InBox.Focus(); }
            _tray?.SetShownLabel(IsVisible);
        }
        private void Clear_Click(object s, RoutedEventArgs e) { InBox.Clear(); OutBox.Text = ""; SyncSizing(); }

        // ---- Restore of an unverified message: deliberately manual, the user decides ----
        private void ShowRestorePill()
        {
            RestorePill.Visibility = Visibility.Visible;
            ApplyLanguageToolTips();
        }

        private void HideRestorePill() => RestorePill.Visibility = Visibility.Collapsed;

        // Put the unverified message back in the box on the user's explicit request.
        private void Restore_Click(object s, RoutedEventArgs e)
        {
            string kept = _cycle.TakeRecoverText();
            if (kept.Length == 0) { HideRestorePill(); return; }
            InBox.Text = InBox.Text.Length == 0 ? kept : InBox.Text + kept;
            InBox.SelectionStart = InBox.Text.Length;
            HideRestorePill();
            if (IsVisible) InBox.Focus();
        }

        private void Close_Click(object s, RoutedEventArgs e) => HideToTray();
        private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    }
}
