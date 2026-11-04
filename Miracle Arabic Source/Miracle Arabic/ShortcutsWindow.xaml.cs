using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Miracle
{
    public partial class ShortcutsWindow : Window
    {
        public List<ShortcutItem> FinalShortcuts { get; set; }
        public bool GlobalActive { get; set; }
        public bool Committed { get; private set; }
        private bool isAR = false; private readonly string HideK1 = "None", HideK2 = "Insert";
        private int _selected = -1;
        private bool _dirty;
        private bool _suppressWarn;
        private bool _loadingEditor;
        private readonly System.Collections.Generic.List<Miracle.ShortcutItem> _origShortcuts = new();
        private bool _origGlobal;
        private bool _isModal;

        public ShortcutsWindow(List<ShortcutItem> cur, string ln, bool globalActive, string hideK1, string hideK2)
        {
            InitializeComponent();
            isAR = (ln == "AR");
            GlobalActive = globalActive;
            HideK1 = hideK1; HideK2 = hideK2;
            BtnGlobalToggle.IsChecked = globalActive;
            BtnGlobalToggle.Click += (s, e) => MarkDirty();
            this.FlowDirection = isAR ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            FinalShortcuts = cur.Select(item => new ShortcutItem
            {
                Key1 = item.Key1,
                Key2 = item.Key2,
                TextToPaste = item.TextToPaste,
                KeepTextAsWritten = item.KeepTextAsWritten
            }).ToList();
            if (FinalShortcuts.Count == 0) for (int i = 0; i < 5; i++) FinalShortcuts.Add(new ShortcutItem());
            ApplyLanguage();
            RenderUI();
            ClearDirty();
            SnapshotOrig();
        }

        private void ClearDirty() { _dirty = false; L_Dirty.Text = ""; }

        private void ApplyLanguage()
        {
            Txt_Header.Text = isAR ? "إعداد مفاتيح الاختصارات" : "HOTKEYS CONFIGURATION";
            L_EditorTitle.Text = isAR ? "تحرير الماكرو" : "EDIT MACRO";
            L_KeepTextAsWritten.Text = isAR ? "إرسال كما كُتب" : "SEND AS WRITTEN";
            L_KeepTextHint.Text = isAR ? "تخطي تشكيل العربية لهذا الماكرو" : "Skip Arabic shaping for this macro";
            Btn_ClosePanel.Content = isAR ? "←" : "→";
            Btn_ClosePanel.ToolTip = isAR ? "إغلاق لوحة التحرير" : "Close editor";
            AutomationProperties.SetName(BtnGlobalToggle, isAR ? "تفعيل اختصارات نافذة الهدف" : "Enable target window shortcuts");
            L_GlobalTitle.Text = isAR ? "دعم اختصارات نافذة الهدف" : "TARGET WINDOW SHORTCUTS SUPPORT";
            AutomationProperties.SetName(ChkKeepTextAsWritten, isAR ? "إرسال الماكرو كما كُتب" : "Send macro exactly as written");
            Btn_Add.Content = isAR ? "+ جديد" : "+ NEW";
            Btn_Cancel.Content = isAR ? "إغلاق" : "CLOSE";
            Btn_Save.Content = isAR ? "حفظ" : "SAVE";
            TT_Delete.Content = isAR ? "حذف هذا الماكرو" : "Delete this macro";
            BtnGlobalToggle.IsChecked = GlobalActive;
            L_Conflict.Text = "";

            GlobalToolTip.Content = isAR
                ? "💡 اختصارات نافذة الهدف:\nعند التفعيل: تعمل اختصارات الماكرو فقط عندما تكون نافذة الهدف (اللعبة) هي الفوكس، حتى لو كان التول مخفيًا في التراي.\nعند الإطفاء: الاختصارات متوقفة تمامًا.\nبعض المفاتيح قد لا تعمل في شات اللعبة."
                : "💡 Target Window Shortcuts:\nOn: macro shortcuts work only while the selected target window (the game) is focused, even when the tool is hidden in the tray.\nOff: shortcuts are disabled.\nSome keys may not work in the game chat.";

            RefreshChars();
        }

        private void RenderUI()
        {
            ShortcutContainer.Children.Clear();
            for (int i = 0; i < FinalShortcuts.Count; i++)
            {
                var itm = FinalShortcuts[i];
                bool dup = HotkeyUtil.SameCombo(itm.Key1, itm.Key2, HideK1, HideK2)
                        || FinalShortcuts.Any(o => !ReferenceEquals(o, itm) && HotkeyUtil.SameCombo(o.Key1, o.Key2, itm.Key1, itm.Key2));
                bool sel = (i == _selected);

                bool keepAsWritten = itm.KeepTextAsWritten;
                Color accent = keepAsWritten
                    ? Color.FromRgb(0xC4, 0xA2, 0xFF)
                    : Color.FromRgb(0x9C, 0x6A, 0xDE);
                var row = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Background = sel
                        ? new LinearGradientBrush(Color.FromRgb(0x2B, 0x1D, 0x40), Color.FromRgb(0x1B, 0x15, 0x27), 90)
                        : new LinearGradientBrush(Color.FromRgb(0x1B, 0x17, 0x23), Color.FromRgb(0x12, 0x11, 0x18), 90),
                    BorderThickness = new Thickness(1.2),
                    BorderBrush = new SolidColorBrush(sel
                        ? Color.FromRgb(0xA8, 0x55, 0xF7)
                        : (keepAsWritten ? Color.FromRgb(0x7C, 0x58, 0xAE) : Color.FromRgb(0x35, 0x2A, 0x43))),
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(9, 8, 9, 8),
                    Focusable = true,
                    ToolTip = isAR ? "اضغط للتحرير" : "Click to edit"
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(MakeKeyPill(FormatKeyName(itm.Key1), dup));
                sp.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0), Foreground = Brushes.Gray, FontSize = 9 });
                sp.Children.Add(MakeKeyPill(FormatKeyName(itm.Key2), dup));
                Grid.SetColumn(sp, 0);
                grid.Children.Add(sp);

                string preview = (itm.TextToPaste ?? "").Replace("\r", " ").Replace("\n", " ");
                if (preview.Length > 90) preview = preview.Substring(0, 90) + "…";
                var pv = new TextBlock
                {
                    Text = preview,
                    FontSize = 10.5,
                    Foreground = keepAsWritten ? new SolidColorBrush(Color.FromRgb(0xD8, 0xB4, 0xFE))
                        : (sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD0, 0xCC, 0xD8))),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FlowDirection = FlowDirection.RightToLeft,
                    TextAlignment = TextAlignment.Left
                };
                Grid.SetColumn(pv, 1);
                grid.Children.Add(pv);

                var num = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xB2)),
                    Margin = new Thickness(8, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(num, 2);
                grid.Children.Add(num);

                row.Child = grid;
                int pick = i;
                AutomationProperties.SetName(row, isAR
                    ? $"الماكرو {i + 1}: {preview}"
                    : $"Macro {i + 1}: {preview}");
                row.MouseLeftButtonUp += (s, e) => SelectRow(pick);
                row.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter || e.Key == Key.Space)
                    {
                        SelectRow(pick);
                        e.Handled = true;
                    }
                };
                row.GotKeyboardFocus += (s, e) => row.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xB4, 0xFE));
                row.LostKeyboardFocus += (s, e) =>
                {
                    row.BorderBrush = new SolidColorBrush(sel
                        ? Color.FromRgb(0xA8, 0x55, 0xF7)
                        : (keepAsWritten ? Color.FromRgb(0x7C, 0x58, 0xAE) : Color.FromRgb(0x35, 0x2A, 0x43)));
                };
                ShortcutContainer.Children.Add(row);
            }
            RefreshConflicts();

            if (_panelOpen)
            {
                if (_selected < 0 || _selected >= FinalShortcuts.Count)
                    _selected = FinalShortcuts.Count > 0 ? 0 : -1;
                if (_selected >= 0 && (MacroEditor.Text ?? "") != (FinalShortcuts[_selected].TextToPaste ?? ""))
                    LoadEditor(_selected);
                else RefreshEditorMeta();
            }
            else
            {
                RefreshEditorMeta();
            }
        }

        private Button MakeKeyPill(string text, bool dup)
        {
            return new Button
            {
                Content = text,
                Width = 66,
                Height = 28,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                Foreground = dup ? new SolidColorBrush(Color.FromRgb(0xE2, 0xC8, 0xFF)) : Brushes.White,
                Background = dup ? new SolidColorBrush(Color.FromRgb(0x35, 0x20, 0x4D)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x27)),
                BorderBrush = dup ? new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)) : new SolidColorBrush(Color.FromRgb(0x43, 0x35, 0x55))
            };
        }

        // Probe-only hook for the off-screen render harness.
        public void SelectRowPublicForProbe(int i) => SelectRow(i);

        private void SelectRow(int i)
        {
            _selected = i;
            RenderUI();
            LoadEditor(i);
            OpenEditor();
            MacroEditor.Focus();
        }

        private bool _panelOpen;
        private double _baseW, _baseL;
        private const double PANEL_W = 322;

        private void OpenEditor()
        {
            if (_panelOpen) { return; }
            _baseW = Width; _baseL = Left;
            EditorPanel.Margin = isAR ? new Thickness(13, 0, 0, 0) : new Thickness(0, 0, 13, 0);
            EditorPanel.Visibility = Visibility.Visible;
            MinWidth = Math.Max(432, _baseW + PANEL_W);
            Width = _baseW + PANEL_W;
            if (!isAR) Left = _baseL - PANEL_W;   // English: the rail sits left, so the width grows left
            _panelOpen = true;
        }

        private void CloseEditor()
        {
            if (!_panelOpen) return;
            EditorPanel.Visibility = Visibility.Collapsed;
            MinWidth = 432;
            Width = _baseW;
            if (!isAR) Left = _baseL;
            _panelOpen = false;
            _selected = -1;
            RenderUI();
        }

        private void ClosePanel_Click(object s, RoutedEventArgs e) => CloseEditor();
        private Button CreateKeyBtn(string cur, Action<string> upd, string pmt, bool dup)
        {
            var b = new Button { Content = FormatKeyName(cur), Width = 70, Height = 32, FontSize = 9, Background = dup ? new SolidColorBrush(Color.FromRgb(58, 16, 16)) : new SolidColorBrush(Color.FromRgb(30, 30, 35)), Foreground = dup ? new SolidColorBrush(Color.FromRgb(255, 69, 58)) : Brushes.White, BorderBrush = dup ? new SolidColorBrush(Color.FromRgb(255, 69, 58)) : new SolidColorBrush(Color.FromRgb(50, 50, 55)) };
            b.Click += (s, e) => {
                b.Content = pmt;
                b.Focus();
                b.PreviewKeyDown += (sx, ex) => {
                    string raw = (ex.Key == Key.System ? ex.SystemKey : ex.Key).ToString();
                    if (ex.Key == Key.Escape) raw = "None";
                    upd(raw); RecheckShortcutKeys();
                    ex.Handled = true;
                    Keyboard.ClearFocus();
                    RenderUI();
                };
            };
            return b;
        }

        private void RecheckShortcutKeys()
        {
            if (_dirty && _selected >= 0 && _selected < FinalShortcuts.Count)
            {
                var s = FinalShortcuts[_selected];
                var o = _origShortcuts[_selected];
                if ((s.Key1 ?? "") == (o.Key1 ?? "") && (s.Key2 ?? "") == (o.Key2 ?? "") && (s.TextToPaste ?? "") == (o.TextToPaste ?? ""))
                    MarkClean();
            }
        }

        private void MarkClean()
        {
            if (!_dirty) return;
            _dirty = false;
            L_Dirty.Text = "";
        }

        private string FormatKeyName(string k)
        {
            if (string.IsNullOrEmpty(k) || k == "None") return isAR ? "لاشيء" : "None";
            if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) return k.Substring(1);
            if (k.StartsWith("NumPad")) return k.Replace("NumPad", isAR ? "رقم " : "Num ");
            return k;
        }

        private void LoadEditor(int i)
        {
            if (i < 0 || i >= FinalShortcuts.Count)
            {
                EditorKeys.Children.Clear();
                _loadingEditor = true;
                MacroEditor.Text = "";
                _loadingEditor = false;
                L_EditorIndex.Text = "";
                RefreshEditorMeta();
                return;
            }
            var itm = FinalShortcuts[i];
            EditorKeys.Children.Clear();
            bool dup = HotkeyUtil.SameCombo(itm.Key1, itm.Key2, HideK1, HideK2)
                    || FinalShortcuts.Any(o => !ReferenceEquals(o, itm) && HotkeyUtil.SameCombo(o.Key1, o.Key2, itm.Key1, itm.Key2));
            EditorKeys.Children.Add(CreateKeyBtn(itm.Key1, v => { itm.Key1 = v; MarkDirty(); }, isAR ? "ممسوك.." : "Hold...", dup));
            EditorKeys.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0), Foreground = Brushes.Gray, FontSize = 10 });
            EditorKeys.Children.Add(CreateKeyBtn(itm.Key2, v => { itm.Key2 = v; MarkDirty(); }, isAR ? "ضغط.." : "Click...", dup));

            _loadingEditor = true;
            MacroEditor.Text = itm.TextToPaste ?? "";
            ChkKeepTextAsWritten.IsChecked = itm.KeepTextAsWritten;
            _loadingEditor = false;
            RefreshEditorMeta();
        }

        private void RefreshEditorMeta()
        {
            if (_selected < 0 || _selected >= FinalShortcuts.Count)
            {
                L_EditorIndex.Text = isAR ? "لا يوجد ماكرو" : "No macros";
                L_Chars.Text = "";
                return;
            }
            L_EditorIndex.Text = isAR
                ? $"الماكرو {_selected + 1} من {FinalShortcuts.Count}"
                : $"Macro {_selected + 1} of {FinalShortcuts.Count}";
            RefreshChars();
        }

        private void RefreshChars()
        {
            string t = (_selected >= 0 && _selected < FinalShortcuts.Count)
                ? (FinalShortcuts[_selected].TextToPaste ?? "")
                : (MacroEditor?.Text ?? "");
            int lines = t.Length == 0 ? 0 : t.Split('\n').Length;
            L_Chars.Text = isAR ? $"{t.Length} حرف · {lines} سطر" : $"{t.Length} chars · {lines} lines";
        }

        private void MacroEditor_TextChanged(object s, TextChangedEventArgs e)
        {
            if (_loadingEditor || _selected < 0 || _selected >= FinalShortcuts.Count) return;
            string t = MacroEditor.Text ?? "";
            FinalShortcuts[_selected].TextToPaste = t;
            RefreshChars();
            MarkDirty();
            RefreshRowPreview(_selected, t);
        }

        private void KeepTextAsWritten_Click(object s, RoutedEventArgs e)
        {
            if (_loadingEditor || _selected < 0 || _selected >= FinalShortcuts.Count) return;
            FinalShortcuts[_selected].KeepTextAsWritten = ChkKeepTextAsWritten.IsChecked == true;
            MarkDirty();
            RenderUI();
        }

        private void RefreshRowPreview(int i, string t)
        {
            if (i < 0 || i >= ShortcutContainer.Children.Count) return;
            if (ShortcutContainer.Children[i] is Border row && row.Child is Grid g && g.Children.Count > 1 && g.Children[1] is TextBlock pv)
            {
                string preview = t.Replace("\r", " ").Replace("\n", " ");
                if (preview.Length > 90) preview = preview.Substring(0, 90) + "…";
                pv.Text = preview;
            }
        }

        private void MarkDirty()
        {
            if (_dirty || _loadingEditor) return;
            _dirty = true;
            L_Dirty.Text = isAR ? "⚠ في تعديلات لم يتم حفظها" : "⚠ Unsaved changes";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_dirty && !_suppressWarn && !ReallyChanged())
            {
                _dirty = false;
                L_Dirty.Text = "";
            }
            if (_dirty && !_suppressWarn)
            {
                int choice = Miracle.NoticeBox.Show3(
                    isAR ? "تنبيه" : "HEADS UP",
                    isAR ? "يوجد تعديلات غير محفوظة.\nاحفظ؟ نعم = حفظ، لا = تجاهل، استمرار = البقاء هنا."
                           : "You have unsaved changes.\nSave? Yes = save, No = discard, Keep editing = stay here.",
                    isAR ? "AR" : "EN");
                if (choice == 1 && ShowConflictWarning()) { e.Cancel = true; return; }
                if (choice == 1) { DoSave(); _suppressWarn = true; _dirty = false; }
                else if (choice == 2) { _suppressWarn = true; _dirty = false; }
                else e.Cancel = true;
            }
            base.OnClosing(e);
        }

        private void SnapshotOrig()
        {
            _origShortcuts.Clear();
            foreach (var s in FinalShortcuts)
                _origShortcuts.Add(new ShortcutItem { Key1 = s.Key1, Key2 = s.Key2, TextToPaste = s.TextToPaste, KeepTextAsWritten = s.KeepTextAsWritten });
            _origGlobal = GlobalActive;
        }

        private static bool SameRow(ShortcutItem a, ShortcutItem b) =>
            (a.Key1 ?? "") == (b.Key1 ?? "") && (a.Key2 ?? "") == (b.Key2 ?? "") &&
            (a.TextToPaste ?? "") == (b.TextToPaste ?? "") && a.KeepTextAsWritten == b.KeepTextAsWritten;

        // True only when rows, texts or the global flag really differ.
        private bool ReallyChanged()
        {
            if ((BtnGlobalToggle.IsChecked ?? false) != _origGlobal) return true;
            if (FinalShortcuts.Count != _origShortcuts.Count) return true;
            for (int i = 0; i < FinalShortcuts.Count; i++)
                if (!SameRow(FinalShortcuts[i], _origShortcuts[i])) return true;
            return false;
        }

        private void DoSave()
        {
            GlobalActive = BtnGlobalToggle.IsChecked ?? false;
            SnapshotOrig();
            // Marks a real save, from either the SAVE button or the unsaved-changes prompt.
            Committed = true;
        }

        private void AddRow_Click(object s, RoutedEventArgs e)
        {
            FinalShortcuts.Add(new ShortcutItem());
            _selected = FinalShortcuts.Count - 1;
            RenderUI();
            LoadEditor(_selected);
            OpenEditor();
            MacroEditor.Focus();
            MarkDirty();
        }

        private void DeleteRow_Click(object s, RoutedEventArgs e)
        {
            if (_selected < 0 || _selected >= FinalShortcuts.Count) return;
            FinalShortcuts.RemoveAt(_selected);
            if (_selected >= FinalShortcuts.Count) _selected = FinalShortcuts.Count - 1;
            RenderUI();
            if (FinalShortcuts.Count == 0) CloseEditor();
            else LoadEditor(_selected);
            MarkDirty();
        }

        private void Save_Click(object s, RoutedEventArgs e)
        {
            if (ShowConflictWarning()) return;
            DoSave();
            _suppressWarn = true;
            _dirty = false;
            L_Dirty.Text = "";
            if (_isModal) DialogResult = true;
            Close();
        }
        private void Cancel_Click(object s, RoutedEventArgs e) => Close();
        // Modal show entry point used by the settings window, remembering it is modal.
        public new bool? ShowDialog() { _isModal = true; return base.ShowDialog(); }

        private List<string> GetShortcutConflicts()
        {
            var conflicts = new List<string>();
            for (int i = 0; i < FinalShortcuts.Count; i++)
            {
                var current = FinalShortcuts[i];
                if (HotkeyUtil.SameCombo(current.Key1, current.Key2, HideK1, HideK2))
                    conflicts.Add(isAR
                        ? $"الماكرو {i + 1} يتعارض مع اختصار إظهار/إخفاء النافذة."
                        : $"Macro {i + 1} conflicts with the show/hide window shortcut.");

                for (int j = i + 1; j < FinalShortcuts.Count; j++)
                {
                    var other = FinalShortcuts[j];
                    if (HotkeyUtil.SameCombo(current.Key1, current.Key2, other.Key1, other.Key2))
                        conflicts.Add(isAR
                            ? $"الماكرو {i + 1} يتعارض مع الماكرو {j + 1}."
                            : $"Macro {i + 1} conflicts with macro {j + 1}.");
                }
            }
            return conflicts;
        }

        private bool ShowConflictWarning()
        {
            var conflicts = GetShortcutConflicts();
            if (conflicts.Count == 0) return false;
            string message = string.Join(Environment.NewLine, conflicts);
            NoticeBox.Show(isAR ? "تعارض الاختصارات" : "SHORTCUT CONFLICT",
                message, isAR ? "AR" : "EN", "⚠");
            return true;
        }

        private void RefreshConflicts()
        {
            var conflicts = GetShortcutConflicts();
            L_Conflict.Text = string.Join("  •  ", conflicts);
            L_Conflict.Visibility = conflicts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Window_MouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null && !ReferenceEquals(current, s))
            {
                if (current is ButtonBase || current is TextBoxBase || current is PasswordBox
                    || current is CheckBox || (current is Border border && border.Focusable))
                    return;
                current = GetParent(current);
            }
            DragMove();
        }

        private static DependencyObject? GetParent(DependencyObject element) =>
            element is Visual || element is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
    }
}
