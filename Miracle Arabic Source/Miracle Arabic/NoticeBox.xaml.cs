using System.Windows;
using System.Windows.Input;

namespace Miracle
{
    public partial class NoticeBox : Window
    {
        // Result of the primary button (OK/SAVE).
        public bool UserResponse { get; private set; } = false;

        // Three-way result: 1 = OK/SAVE, 2 = discard, 0 = cancel or window closed.
        public int Result { get; private set; } = 0;

        public NoticeBox(string t, string m, string i, bool sC, string oT, string cT, bool ar)
        {
            InitializeComponent();
            FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TxtTitle.Text = t;
            TxtMsg.Text = m;
            TxtIcon.Text = i;
            BtnOk.Content = oT;
            BtnCancel.Content = cT;
            BtnCancel.Visibility = sC ? Visibility.Visible : Visibility.Collapsed;
        }
        private void Ok_Click(object s, RoutedEventArgs e) { UserResponse = true; Result = 1; Close(); }
        private void Discard_Click(object s, RoutedEventArgs e) { UserResponse = false; Result = 2; Close(); }
        private void Cancel_Click(object s, RoutedEventArgs e) { UserResponse = false; Result = 0; Close(); }
        private void Window_MouseDown(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        public static bool Show(string t, string m, string l = "EN", string i = "◆", bool sc = false)
        {
            bool ar = (l == "AR");
            NoticeBox w = new NoticeBox(t, m, i, sc, ar ? "موافق" : "OK", ar ? "إلغاء" : "CANCEL", ar);
            w.ShowDialog();
            return w.UserResponse;
        }
        public static int Show3(string t, string m, string l = "EN")
        {
            bool ar = (l == "AR");
            NoticeBox w = new NoticeBox(t, m, "⚠", true, ar ? "حفظ" : "SAVE", ar ? "استمرار" : "KEEP EDITING", ar);
            w.BtnDiscard.Visibility = Visibility.Visible;
            w.BtnDiscard.Content = ar ? "تجاهل" : "DISCARD";
            w.Topmost = true;
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
            return w.Result;
        }
    }
}
