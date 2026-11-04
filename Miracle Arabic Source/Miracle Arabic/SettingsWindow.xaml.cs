using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Miracle
{
    public partial class SettingsWindow : Window
    {
        MainWindow main; bool isAR;
        private bool _dirty;
        private bool _suppressWarn;
        private string _origTarget = "";
        private double _origFS; private int _origSpeed; private string _origLang = "";
        private string _origTrans = ""; private string _origDeepL = "";
        private bool _origRtl; private bool _origMini; private string _origSrc = ""; private string _origDst = "";
        private SendMode _origSendMode = SendMode.Auto;
        private string _origHideKey1 = "", _origHideKey2 = "";
        private bool _origGlobalShortcuts;
        private readonly List<ShortcutItem> _origShortcuts = new();

        // Running games offered in the target box, and the display label each one is shown under.
        private readonly List<GameInstance> _games = new();
        private readonly Dictionary<string, GameInstance> _labelToGame = new(StringComparer.Ordinal);
        // Separates the game name from its install path when two titles would collide.
        private const string GameLabelSeparator = "  —  ";

        public SettingsWindow(MainWindow owner)
        {
            InitializeComponent(); main = owner; Refresh(); SelectSavedGame(); _origTarget = ComboWin.Text ?? ""; TxtVersion.Text = "v" + MainWindow.CurrentVersion;
            foreach (ComboBoxItem itm in ComboFS.Items) if (itm.Content?.ToString() == main.Data.FS.ToString()) ComboFS.SelectedItem = itm;
            foreach (ComboBoxItem itm in ComboLang.Items) if (itm.Tag?.ToString() == (main.Data.Lang ?? "EN")) ComboLang.SelectedItem = itm;
            AttachHideKeys(); AttachDirtyTracking(); ApplyLanguage(main.Data.Lang ?? "EN");
            ChkAutoArabic.IsChecked = main.Data.RtlSupport;

            ComboSrc.ItemsSource = Languages.All;
            ComboDst.ItemsSource = Languages.All;
            ComboSrc.SelectedItem = Languages.Get(main.Data.SrcLang);
            ComboDst.SelectedItem = Languages.Get(main.Data.DstLang);
            ChkMini.IsChecked = main.Data.Mini;
            TxtDeepLKey.Password = main.Data.DeepLKey ?? "";
            foreach (ComboBoxItem itm in ComboTranslator.Items) if (itm.Tag?.ToString() == (main.Data.Translator ?? "Google")) { ComboTranslator.SelectedItem = itm; break; }
            if (ComboTranslator.SelectedItem == null) ComboTranslator.SelectedIndex = 0;
            SelectSendMode(main.Data.SendMode);
            RefreshDeepLPanel();
            RefreshLanguageDisplay();
            ClearDirty();
            SnapshotSettings();
            ShowTab(0);
        }
        private void SnapshotSettings()
        {
            _origTarget = ComboWin.Text ?? "";
            if (ComboFS.SelectedItem is ComboBoxItem fs && double.TryParse(fs.Content?.ToString(), out double f)) _origFS = f;
            if (ComboSpeed.SelectedItem is ComboBoxItem sp && int.TryParse(sp.Tag?.ToString(), out int s)) _origSpeed = s;
            _origLang = (ComboLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            _origTrans = (ComboTranslator.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            _origDeepL = (TxtDeepLKey.Password ?? "").Trim();
            _origRtl = ChkAutoArabic.IsChecked == true;
            _origMini = ChkMini.IsChecked == true;
            _origSrc = CurrentSrc; _origDst = CurrentDst;
            _origSendMode = SelectedSendMode();
            _origHideKey1 = main.Data.HideKey1 ?? "";
            _origHideKey2 = main.Data.HideKey2 ?? "";
            _origGlobalShortcuts = main.Data.GlobalShortcuts;
            _origShortcuts.Clear();
            _origShortcuts.AddRange(main.Data.Shortcuts.Select(CloneShortcut));
        }

        private static ShortcutItem CloneShortcut(ShortcutItem item) => new ShortcutItem
        {
            Key1 = item.Key1, Key2 = item.Key2, TextToPaste = item.TextToPaste,
            KeepTextAsWritten = item.KeepTextAsWritten
        };

        private bool ValuesMatch()
        {
            double fs = _origFS;
            if (ComboFS.SelectedItem is ComboBoxItem cfs && double.TryParse(cfs.Content?.ToString(), out double cf)) fs = cf;
            int sp = _origSpeed;
            if (ComboSpeed.SelectedItem is ComboBoxItem csp && int.TryParse(csp.Tag?.ToString(), out int cs)) sp = cs;
            return (ComboWin.Text ?? "") == _origTarget
                && fs == _origFS && sp == _origSpeed
                && ((ComboLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "") == _origLang
                && ((ComboTranslator.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "") == _origTrans
                && ((TxtDeepLKey.Password ?? "").Trim()) == _origDeepL
                && (ChkAutoArabic.IsChecked == true) == _origRtl
                && (ChkMini.IsChecked == true) == _origMini
                && CurrentSrc == _origSrc && CurrentDst == _origDst
                && SelectedSendMode() == _origSendMode
                && (main.Data.HideKey1 ?? "") == _origHideKey1
                && (main.Data.HideKey2 ?? "") == _origHideKey2
                && main.Data.GlobalShortcuts == _origGlobalShortcuts
                && main.Data.Shortcuts.Count == _origShortcuts.Count
                && main.Data.Shortcuts.Zip(_origShortcuts).All(pair => SameShortcut(pair.First, pair.Second));
        }

        private static bool SameShortcut(ShortcutItem a, ShortcutItem b) =>
            (a.Key1 ?? "") == (b.Key1 ?? "") && (a.Key2 ?? "") == (b.Key2 ?? "")
            && (a.TextToPaste ?? "") == (b.TextToPaste ?? "")
            && a.KeepTextAsWritten == b.KeepTextAsWritten;

        // Clears the amber warning (and the prompt) once live values match the snapshot.
        private void RecheckDirty()
        {
            if (ValuesMatch()) ClearDirty();
            else if (!_dirty) MarkDirty();
        }

        private void AttachDirtyTracking()
        {
            // Read-only combos: SelectionChanged fires only on a real value change.
            foreach (var cb in new[] { ComboFS, ComboSpeed, ComboLang, ComboTranslator, ComboSrc, ComboDst, ComboSendMode })
                cb.SelectionChanged += (s, e) => MarkDirty();
            // Editable target box: compare the committed text with what we loaded.
            ComboWin.LostFocus += (s, e) => { if ((ComboWin.Text ?? "") != _origTarget) MarkDirty(); };
            foreach (var chk in new[] { ChkAutoArabic, ChkMini }) chk.Click += (s, e) => MarkDirty();
            TxtDeepLKey.PasswordChanged += (s, e) => MarkDirty();
            BtnSwap.Click += (s, e) => MarkDirty();
        }

        private void MarkDirty()
        {
            if (_dirty) return;
            _dirty = true;
            L_Dirty.Text = isAR ? "⚠ في تعديلات لم يتم حفظها" : "⚠ Unsaved changes";
        }

        private void ClearDirty() { _dirty = false; L_Dirty.Text = ""; }

        private void DoSave()
        {
            main.Data.SrcLang = CurrentSrc; main.Data.DstLang = CurrentDst;
            main.ApplyMiniMode(ChkMini.IsChecked == true);
            ApplyTargetSelection();
            if (ComboFS.SelectedItem is ComboBoxItem fs) { double.TryParse(fs.Content.ToString(), out double fsv); main.UpdateFont(fsv); }
            if (ComboSpeed.SelectedItem is ComboBoxItem sp) { int.TryParse(sp.Tag.ToString(), out int sv); main.Data.Speed = sv; }
            if (ComboLang.SelectedItem is ComboBoxItem ln) main.Data.Lang = ln.Tag.ToString()!;
            main.Data.Translator = (ComboTranslator.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Google";
            main.Data.DeepLKey = TxtDeepLKey.Password?.Trim() ?? "";
            main.Data.RtlSupport = ChkAutoArabic.IsChecked == true;
            main.ApplyTextDirection();
            main.Data.SendMode = SelectedSendMode();
            GameSender.ForgetTarget();
            main.SaveSettings();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            RecheckDirty();
            if (_dirty && !_suppressWarn)
            {
                string ln = main.Data.Lang ?? "EN";
                int choice = Miracle.NoticeBox.Show3(
                    ln == "AR" ? "تنبيه" : "HEADS UP",
                    ln == "AR" ? "يوجد تعديلات غير محفوظة.\nاحفظ؟ نعم = حفظ، لا = تجاهل، استمرار = البقاء هنا."
                               : "You have unsaved changes.\nSave? Yes = save, No = discard, Keep editing = stay here.",
                    ln);
                if (choice == 1) { DoSave(); _suppressWarn = true; ClearDirty(); }
                else if (choice == 2) { _suppressWarn = true; _dirty = false; }
                else e.Cancel = true;
            }
            base.OnClosing(e);
        }

        // Switches the visible settings panel and highlights the active tab.
        public void ShowTabPublicForProbe(int i) => ShowTab(i);private void ShowTab(int idx)
        {
            PanelGeneral.Visibility   = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelTranslate.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelShortcuts.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

            var active = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7));
            var idle = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x23));
            var idleBd = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x38));
            TabGeneral.Background = idx == 0 ? active : idle;   TabGeneral.Foreground = Brushes.White;   TabGeneral.BorderBrush = idx == 0 ? active : idleBd;
            TabTranslate.Background = idx == 1 ? active : idle; TabTranslate.Foreground = Brushes.White; TabTranslate.BorderBrush = idx == 1 ? active : idleBd;
            TabShortcuts.Background = idx == 2 ? active : idle; TabShortcuts.Foreground = Brushes.White; TabShortcuts.BorderBrush = idx == 2 ? active : idleBd;
        }
        private void TabGeneral_Click(object s, RoutedEventArgs e) => ShowTab(0);
        private void TabTranslate_Click(object s, RoutedEventArgs e) => ShowTab(1);
        private void TabShortcuts_Click(object s, RoutedEventArgs e) => ShowTab(2);

        private void ApplyLanguage(string ln)
        {
            bool ar = (ln == "AR"); isAR = ar; this.FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            ChkAutoArabic.Content = ar ? "دعم الاتجاه من اليمين لليسار" : "RTL SUPPORT";
            TabGeneral.Content = ar ? "عام" : "GENERAL";
            TabTranslate.Content = ar ? "الترجمة" : "TRANSLATION";
            TabShortcuts.Content = ar ? "الاختصارات" : "SHORTCUTS";
            L_ShortcutsHint.Text = ar
                ? "اضغط الزر بالأسفل لفتح نافذة اختصارات الكيبورد، حيث تضيف مفاتيح الهولد + زر التفعيل والنص الذي يُلصق. الاختصارات تعمل فقط عندما تكون نافذة الهدف (اللعبة) هي الفوكس. فعّل خيار «دعم اختصارات نافذة الهدف» لتفعيلها."
                : "Press the button below to open the keyboard hotkeys window, where you add hold + trigger keys and the text to paste. The shortcuts only work while the selected target (game) window is focused. Turn on 'Target window shortcuts support' to enable them.";
            TT_AutoArabic.Content = ar ? "يضبط اتجاه الكتابة فقط: عند التفعيل يكون الاتجاه من اليمين لليسار، وعند الإطفاء من الشمال لليمين. لا يغير لغة لوحة المفاتيح إطلاقاً." : "Controls the text direction only: ON makes the text flow right-to-left, OFF makes it left-to-right. It never changes the keyboard language.";

            TT_TargetWin.Content = ar
                ? "هنا تختار اللعبة. يظهر كل إصدار 3DXChat شغّال (الأصلي أو فورك زي Match3D Reborn)، ويتم التعرّف على كل إصدار من مسار الملف التنفيذي الخاص به، فلا يحصل لبس حتى لو تشابهت أسماء النوافذ."
                : "Pick the game here. Every running 3DXChat build is listed (the original or a fork like Match3D Reborn); each build is identified by its executable path, so identical window titles never cause a mix-up.";
            TT_Refresh.Content = ar ? "تحديث حالات النوافذ" : "Update window list states.";
            TT_ShortcutGame.Content = ar ? "إنشاء اختصار للعبة يلغي التايتل بار الخاص بها" : "Create a game shortcut that disables its Title Bar.";
            TT_Speed.Content = ar ? "سرعة إرسال الشات من مكان الكتابة إلى شات اللعبة في وضع الأيزي مود" : "The speed of sending text from input to game chat in Easy Mode.";

            L_Translate.Text = ar ? "الترجمة" : "TRANSLATE";
            L_TranslateHint.Text = ar ? "اختر لغة النص الذي تكتبه ولغة الترجمة (زر ⇄ يبدل بينهما)" : "Choose the language you type and the one to translate to (⇄ swaps them).";
            TT_Src.Content = ar ? "اللغة التي تكتب بها" : "The language you type in.";
            TT_Dst.Content = ar ? "اللغة المترجم إليها" : "The language the text is translated into.";
            TT_Swap.Content = ar ? "تبديل اللغتين" : "Swap the two languages."; L_HideHint.Text = ar ? "التول يفضل موجود في شريط النظام" : "Tool stays in the system tray";
            ChkMini.Content = ar ? "الوضع المصغر" : "MINI MODE";
            TT_Mini.Content = ar ? "تصغير واجهة الكتابة وإخفاء صندوق المعاينة والفوتر" : "Collapses the typing view and hides the preview box and footer.";

            TT_Translator.Content = ar ? "اختر محرّك الترجمة: Google مجاني للجميع، أو DeepL (يحتاج مفتاح API خاص بك)" : "Choose the translation engine: Google (free for everyone) or DeepL (needs your own API key).";
            TT_DeepLKey.Content = ar ? "اكتب مفتاح DeepL API الخاص بك هنا (يُحفظ حتى لا تعيد كتابته)" : "Enter your DeepL API key here (it is saved so you don't type it again).";
            TT_DeepLCheck.Content = ar ? "اضغط للتحقق من صلاحية المفتاح" : "Click to verify the API key is valid.";
            TT_DeepLFree.Content = ar ? "افتح موقع DeepL للحصول على مفتاح مجاني" : "Open the DeepL website to get a free API key.";
            BtnDeepLFree.Content = ar ? "مفتاح مجاني" : "FREE KEY";
            L_SendMode.Text = ar ? "طريقة إرسال الأيزي مود" : "EASY MODE SENDING";
            RefreshLanguageDisplay();

            string cur = main.Data.Speed.ToString(); ComboSpeed.Items.Clear(); AddSp("10", ar ? "فوري (الأسرع)" : "Instant"); AddSp("30", ar ? "تربو" : "Turbo"); AddSp("100", ar ? "سريع" : "Fast"); AddSp("300", ar ? "عادي" : "Normal"); AddSp("500", ar ? "بطيء" : "Slow"); AddSp("800", ar ? "أبطأ" : "Slower");
            foreach (ComboBoxItem i in ComboSpeed.Items) if (i.Tag.ToString() == cur) ComboSpeed.SelectedItem = i; if (ComboSpeed.SelectedItem == null) ComboSpeed.SelectedIndex = 3;
            FillSendModeCombo();
            RefreshHideKeyButtons();
        }

        private void FillSendModeCombo()
        {
            SendMode keep = SelectedSendMode();
            ComboSendMode.Items.Clear();
            ComboSendMode.Items.Add(new ComboBoxItem { Content = isAR ? "تلقائي (موصى به)" : "Automatic (recommended)", Tag = SendMode.Auto });
            ComboSendMode.Items.Add(new ComboBoxItem { Content = isAR ? "شات عبر محرك اللعبة (موصى به للعربي)" : "Chat via game engine (best for Arabic)", Tag = SendMode.Bridge });
            ComboSendMode.Items.Add(new ComboBoxItem { Content = isAR ? "مفاتيح الكيبورد (توافق أوسع)" : "Keyboard keys (widest support)", Tag = SendMode.Keyboard });
            SelectSendMode(keep);
            TT_SendMode.Content = isAR
                ? "تلقائي: يستخدم «شات عبر محرك اللعبة» فقط مع ألعاب Unity Mono (زي النسخة الأصلية 3DXChat)؛ أما الألعاب المبنية بـIL2CPP (زي فورك Match3D Reborn) فيستخدم «مفاتيح الكيبورد» مباشرة، لأن الجسر لا يعمل معها ولأن حقنها قد يستفز حمايتها. أول مرة قد تأخذ لحظات لحقن الموديول والتحقق منه، وهذا ليس فشلاً. شات عبر محرك اللعبة: الطريقة الوحيدة التي يوصل بها العربي كما هو (يكتب داخل مربع دردشة اللعبة نفسها ويرسله بميثود الإرسال الخاصة باللعبة)، ويحتاج أن تكون اللعبة مفتوحة وداخل غرفة. مفاتيح الكيبورد: يجلب نافذة اللعبة للأمام ويرسل Ctrl+V ثم Enter بمفاتيح حقيقية (توافق أوسع، لكن اللعبة تظهر أثناء الإرسال ويجب أن يكون مربع الشات مفتوحاً)."
                : "Automatic: uses Chat via game engine only with Mono Unity games (like the original 3DXChat); for IL2CPP games (like the Match3D Reborn fork) it uses Keyboard keys straight away, because the bridge cannot run there and injecting could provoke their protection. The very first time may take a moment to inject and verify the module - that is not a failure. Chat via game engine: the only route that delivers Arabic exactly as typed (writes inside the game's own chat box and submits through the game's own send method), and it needs the game open and inside a room. Keyboard keys: brings the game forward and sends Ctrl+V + Enter as real key presses (widest support, but the game appears while sending and the chat box must be open).";
        }

        private void SelectSendMode(SendMode mode)
        {
            foreach (ComboBoxItem i in ComboSendMode.Items)
                if (i.Tag is SendMode m && m == mode) { ComboSendMode.SelectedItem = i; return; }
            if (ComboSendMode.Items.Count > 0) ComboSendMode.SelectedIndex = 0;
        }

        private SendMode SelectedSendMode()
            => (ComboSendMode.SelectedItem as ComboBoxItem)?.Tag is SendMode m ? m : SendMode.Auto;

        private void ComboSendMode_SelectionChanged(object s, SelectionChangedEventArgs e) { }
        private void AddSp(string tg, string tx) => ComboSpeed.Items.Add(new ComboBoxItem { Content = tx, Tag = tg });
        private void AttachHideKeys()
        {
            BtnHideK1.Click += (s, e) => CaptureKey(BtnHideK1, v => CaptureHideKey1(v));
            BtnHideK2.Click += (s, e) => CaptureKey(BtnHideK2, v => CaptureHideKey2(v));
        }
        private void RefreshHideKeyButtons()
        {
            BtnHideK1.Content = FormatKeyName(main.Data.HideKey1);
            BtnHideK2.Content = FormatKeyName(main.Data.HideKey2);
            bool dup = !string.IsNullOrEmpty(main.Data.HideKey2) && main.Data.HideKey2 != "None"
                    && main.Data.Shortcuts.Any(s => HotkeyUtil.SameCombo(s.Key1, s.Key2, main.Data.HideKey1, main.Data.HideKey2));
            var bg = dup ? new SolidColorBrush(Color.FromRgb(58, 16, 16)) : new SolidColorBrush(Color.FromRgb(22, 22, 26));
            var fg = dup ? new SolidColorBrush(Color.FromRgb(255, 69, 58)) : Brushes.White;
            BtnHideK1.Background = bg; BtnHideK2.Background = bg;
            BtnHideK1.Foreground = fg; BtnHideK2.Foreground = fg;
        }
        private void CaptureKey(Button b, Action<string> set)
        {
            b.Content = isAR ? "اضغط.." : "Press...";
            b.Focus();
            KeyEventHandler? h = null;
            h = (s, e) => {
                string raw = (e.Key == Key.System ? e.SystemKey : e.Key).ToString();
                if (e.Key == Key.Escape) raw = "None";
                set(raw); RefreshHideKeyButtons(); e.Handled = true;
                b.PreviewKeyDown -= h; Keyboard.ClearFocus();
            };
            b.PreviewKeyDown += h;
        }
        // Stores a captured show/hide key only when it really changed.
        private void CaptureHideKey1(string v) { if ((main.Data.HideKey1 ?? "") != v) { main.Data.HideKey1 = v; MarkDirty(); } }
        private void CaptureHideKey2(string v) { if ((main.Data.HideKey2 ?? "") != v) { main.Data.HideKey2 = v; MarkDirty(); } }

        private string FormatKeyName(string k)
        {
            if (string.IsNullOrEmpty(k) || k == "None") return isAR ? "لاشيء" : "None";
            if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) return k.Substring(1);
            if (k.StartsWith("NumPad")) return k.Replace("NumPad", isAR ? "رقم " : "Num ");
            return k;
        }
        private void ComboLang_SelectionChanged(object s, SelectionChangedEventArgs e) { if (ComboLang.SelectedItem is ComboBoxItem i) ApplyLanguage(i.Tag.ToString()!); }

        private void RefreshLanguageDisplay()
        {
            foreach (var lang in Languages.All) lang.Display = lang.Name(isAR);
            object? src = ComboSrc.SelectedItem, dst = ComboDst.SelectedItem;
            ComboSrc.SelectedItem = null; ComboSrc.SelectedItem = src;
            ComboDst.SelectedItem = null; ComboDst.SelectedItem = dst;
        }

        private string CurrentSrc => (ComboSrc.SelectedItem as Language)?.Code ?? main.Data.SrcLang;
        private string CurrentDst => (ComboDst.SelectedItem as Language)?.Code ?? main.Data.DstLang;

        private void SwapLangs_Click(object s, RoutedEventArgs e)
        {
            object? tmp = ComboSrc.SelectedItem;
            ComboSrc.SelectedItem = ComboDst.SelectedItem;
            ComboDst.SelectedItem = tmp;
        }

        private void ChkMini_Click(object s, RoutedEventArgs e) => main.ApplyMiniMode(ChkMini.IsChecked == true);
        private void ChkAutoArabic_Click(object s, RoutedEventArgs e)
        {
            main.Data.RtlSupport = ChkAutoArabic.IsChecked == true;
            main.ApplyTextDirection();
            main.Data.SendMode = SelectedSendMode();
            GameSender.ForgetTarget();
        }
        private void ComboTranslator_SelectionChanged(object s, SelectionChangedEventArgs e) => RefreshDeepLPanel();
        private void RefreshDeepLPanel()
        {
            bool deepL = (ComboTranslator.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "DeepL";
            DeepLPanel.Visibility = deepL ? Visibility.Visible : Visibility.Collapsed;
            L_DeepLStatus.Text = "";
        }
        private async void DeepLCheck_Click(object s, RoutedEventArgs e)
        {
            string key = TxtDeepLKey.Password?.Trim() ?? "";
            if (string.IsNullOrEmpty(key)) { SetDeepLStatus(isAR ? "اكتب المفتاح أولاً ✕" : "Enter a key first ✕", false); return; }
            BtnDeepLCheck.IsEnabled = false;
            SetDeepLStatus(isAR ? "جاري الفحص..." : "Checking...", null);
            var (ok, msg) = await TranslateService.DeepLCheckAsync(key);
            BtnDeepLCheck.IsEnabled = true;
            if (ok) { main.Data.DeepLKey = key; SetDeepLStatus(isAR ? "المفتاح صالح ✓ جاهز للاستخدام" : "Key valid ✓ Ready to use", true); }
            else SetDeepLStatus(isAR ? ("فشل: " + msg + " ✕") : ("Failed: " + msg + " ✕"), false);
        }
        private void SetDeepLStatus(string text, bool? ok)
        {
            L_DeepLStatus.Text = text;
            L_DeepLStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            L_DeepLStatus.Foreground = ok == null ? Brushes.Gray : (ok.Value ? (Brush)new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
        }
        private void DeepLFree_Click(object s, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://www.deepl.com/pro-api") { UseShellExecute = true }); } catch { }
        }
        private void Window_MouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null && !ReferenceEquals(current, s))
            {
                if (current is ButtonBase || current is TextBoxBase || current is PasswordBox
                    || current is CheckBox)
                    return;
                current = GetParent(current);
            }
            DragMove();
        }
        private static DependencyObject? GetParent(DependencyObject element) =>
            element is Visual || element is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        private void Refresh()
        {
            _games.Clear();
            _labelToGame.Clear();

            var games = GameLibrary.DetectRunningGames();

            string savedExe = GameLibrary.NormalizePath(main.Data.GameExe);
            if (savedExe.Length > 0 && !games.Any(g => GameLibrary.NormalizePath(g.ExePath) == savedExe))
            {
                games.Add(new GameInstance
                {
                    ExePath = main.Data.GameExe ?? "",
                    ExeName = Path.GetFileName(main.Data.GameExe ?? ""),
                    WindowTitle = main.Data.Target ?? ""
                });
            }
            _games.AddRange(games);

            // Count how often each title appears; a shared title is disambiguated by install path.
            var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _games)
            {
                titleCounts.TryGetValue(g.WindowTitle ?? "", out int n);
                titleCounts[g.WindowTitle ?? ""] = n + 1;
            }

            var labels = new List<string>();
            // Games first, each carrying its install identity.
            foreach (var g in _games)
            {
                string label = GameLabel(g, titleCounts);
                if (_labelToGame.ContainsKey(label)) continue;
                _labelToGame[label] = g;
                labels.Add(label);
            }
            foreach (var title in OtherWindowTitles())
            {
                if (_labelToGame.ContainsKey(title)) continue;
                _labelToGame[title] = new GameInstance { WindowTitle = title };
                labels.Add(title);
            }
            ComboWin.ItemsSource = labels;
        }

        private IEnumerable<string> OtherWindowTitles()
        {
            var gameTitles = new HashSet<string>(_games.Select(g => g.WindowTitle ?? ""), StringComparer.OrdinalIgnoreCase);
            int ownPid;
            try { ownPid = Process.GetCurrentProcess().Id; }
            catch { ownPid = -1; }

            List<string> titles;
            try
            {
                titles = Process.GetProcesses()
                    .Where(p => { try { return p.Id != ownPid && !string.IsNullOrEmpty(p.MainWindowTitle); } catch { return false; } })
                    .Select(p => (p.MainWindowTitle ?? "").Trim())
                    .Where(t => t.Length > 0 && !gameTitles.Contains(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return Array.Empty<string>(); }
            return titles;
        }

        private static string GameLabel(GameInstance game, Dictionary<string, int> titleCounts)
        {
            string baseName = string.IsNullOrWhiteSpace(game.WindowTitle) ? game.ExeName : game.WindowTitle;
            titleCounts.TryGetValue(game.WindowTitle ?? "", out int count);
            if (count <= 1 || string.IsNullOrWhiteSpace(game.ExePath)) return baseName;
            string folder;
            try { folder = Path.GetDirectoryName(game.ExePath) ?? game.ExePath; }
            catch { folder = game.ExePath; }
            return baseName + GameLabelSeparator + folder;
        }

        // Selects the saved game in the freshly filled target box.
        private void SelectSavedGame()
        {
            string savedExe = GameLibrary.NormalizePath(main.Data.GameExe);
            GameInstance? pick = null;
            if (savedExe.Length > 0)
                pick = _games.FirstOrDefault(g => GameLibrary.NormalizePath(g.ExePath) == savedExe);
            if (pick == null && !string.IsNullOrWhiteSpace(main.Data.Target))
                pick = _games.FirstOrDefault(g => string.Equals(g.WindowTitle, main.Data.Target, StringComparison.OrdinalIgnoreCase));

            if (pick != null)
            {
                foreach (var pair in _labelToGame)
                {
                    if (ReferenceEquals(pair.Value, pick)
                        || GameLibrary.NormalizePath(pair.Value.ExePath) == GameLibrary.NormalizePath(pick.ExePath))
                    { ComboWin.Text = pair.Key; return; }
                }
            }
            ComboWin.Text = main.Data.Target ?? "";
        }

        private void ApplyTargetSelection()
        {
            string label = (ComboWin.Text ?? "").Trim();
            if (_labelToGame.TryGetValue(label, out var game))
            {
                main.Data.Target = game.WindowTitle;
                main.Data.GameExe = game.ExePath;
            }
            else
            {
                main.Data.Target = label;
                main.Data.GameExe = "";
            }
        }
        private void OpenShortcuts_Click(object s, RoutedEventArgs e)
        {
            string currentLang = (ComboLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "EN";
            var win = new ShortcutsWindow(main.Data.Shortcuts, currentLang, main.Data.GlobalShortcuts, main.Data.HideKey1, main.Data.HideKey2);
            win.Owner = this;
            if (win.ShowDialog() == true || win.Committed)
            {
                main.Data.Shortcuts = win.FinalShortcuts;
                main.Data.GlobalShortcuts = win.GlobalActive;
                RecheckDirty();
            }
        }
        private void Save_Click(object s, RoutedEventArgs e)
        {
            DoSave();
            _origTarget = ComboWin.Text ?? "";
            SnapshotSettings();
            _suppressWarn = true; ClearDirty(); this.Close();
        }
        private void Shortcut_Click(object s, RoutedEventArgs e) { var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Exe (*.exe)|*.exe" }; if (ofd.ShowDialog() == true) try { string scp = Path.Combine(Path.GetDirectoryName(ofd.FileName)!, Path.GetFileNameWithoutExtension(ofd.FileName) + ".lnk"); string pwr = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{scp}');$s.TargetPath='{ofd.FileName}';$s.Arguments='-popupwindow';$s.Save()"; Process.Start(new ProcessStartInfo("powershell.exe", pwr) { CreateNoWindow = true, UseShellExecute = false }); NoticeBox.Show((main.Data.Lang == "AR" ? "نجاح" : "SUCCESS"), (main.Data.Lang == "AR" ? "تم الإنشاء!" : "Done!"), main.Data.Lang, "★"); } catch (Exception ex) { NoticeBox.Show("Error", ex.Message, main.Data.Lang, "⚠"); } }
        private void CheckUpdate_Click(object s, RoutedEventArgs e) => _ = main.CheckForUpdatesAsync(true);

        private void Cancel_Click(object s, RoutedEventArgs e) => this.Close();
        private void Refresh_Click(object s, RoutedEventArgs e) => Refresh();
    }
}
