namespace Miracle
{
    // Helpers for comparing hotkeys; each shortcut is two keys (Key1 + Key2) and "None" means not set.
    public static class HotkeyUtil
    {
        // Any empty or unset key becomes "None" so comparisons stay valid.
        private static string Norm(string? k) => string.IsNullOrEmpty(k) || k == "None" ? "None" : k!;

        // The second key must be set and equal on both sides; the first key may be unset on one side.
        public static bool SameCombo(string? k1a, string? k2a, string? k1b, string? k2b)
        {
            string t2a = Norm(k2a), t2b = Norm(k2b);
            if (t2a == "None" || t2b == "None" || t2a != t2b) return false;
            string h1 = Norm(k1a), h2 = Norm(k1b);
            return h1 == h2 || h1 == "None" || h2 == "None";
        }
    }
}
