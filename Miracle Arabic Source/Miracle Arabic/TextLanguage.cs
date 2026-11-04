using System;
using System.Collections.Generic;
using System.Text;

namespace Miracle
{
    // Script a text is written in; used as a quick offline hint.
    internal enum TextScript
    {
        None,
        Latin,
        Arabic,
        Cyrillic,
        Devanagari,
        Han,
        Kana,
        Hangul,
        Other
    }

    internal static class TextLanguage
    {
        private static readonly HashSet<string> IgnoredTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gg", "ggwp", "wp", "ez", "op", "nt", "hi", "ok", "lol", "brb", "afk", "omg",
            "ty", "thx", "np", "gz", "gl", "hf", "ff", "sus", "bot", "lag", "ping",
            "fps", "gm", "gn", "sup", "yo"
        };

        public static bool IsUnfitForTranslation(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            int letters = 0;
            foreach (char c in trimmed)
                if (char.IsLetter(c)) letters++;
            if (letters < 2) return true;

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                return true;

            var token = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
                if (char.IsLetterOrDigit(c)) token.Append(c);

            return IgnoredTokens.Contains(token.ToString());
        }

        public static bool IsSameLanguageByScript(string? inputLanguage, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            TextScript expected = ScriptFor(inputLanguage);
            if (expected == TextScript.Latin || expected == TextScript.Other || expected == TextScript.None)
                return false;

            TextScript dominant = DominantScript(text);

            // Japanese uses Han + Kana, so the text counts as Japanese when it contains either.
            if (expected == TextScript.Kana)
                return dominant == TextScript.Han || dominant == TextScript.Kana;

            return dominant == expected;
        }

        public static bool ScriptClearlyDiffers(string? inputLanguage, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            TextScript expected = ScriptFor(inputLanguage);
            if (expected == TextScript.Latin || expected == TextScript.None || expected == TextScript.Other)
                return false;

            TextScript dominant = DominantScript(text);
            if (dominant == TextScript.None || dominant == TextScript.Other) return false;

            if (expected == TextScript.Kana)
                return dominant != TextScript.Han && dominant != TextScript.Kana;

            return dominant != expected;
        }

        // True when the detected language code is the configured input language.
        public static bool CodesMatch(string? detectedLanguage, string? inputLanguage)
        {
            if (string.IsNullOrWhiteSpace(detectedLanguage) || string.IsNullOrWhiteSpace(inputLanguage))
                return false;
            return Normalize(detectedLanguage) == Normalize(inputLanguage);
        }

        public static bool CanApplyLateTranslation(
            string? boxText, string? preparedText, int requestVersion, int currentVersion)
        {
            return requestVersion == currentVersion
                   && string.Equals(boxText, preparedText, StringComparison.Ordinal);
        }

        // Normalizes provider codes: "EN-US" becomes "en", "zh-CN" becomes "zh", "PT-PT" becomes "pt".
        public static string Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            string value = code.Trim().ToLowerInvariant();
            int dash = value.IndexOf('-');
            if (dash > 0) value = value.Substring(0, dash);
            return value;
        }

        // Maps a language code to its expected script, or None for an unknown code.
        private static TextScript ScriptFor(string? code) => Normalize(code) switch
        {
            "ar" => TextScript.Arabic,
            "ru" => TextScript.Cyrillic,
            "hi" => TextScript.Devanagari,
            "zh" => TextScript.Han,
            "ja" => TextScript.Kana,
            "ko" => TextScript.Hangul,
            "en" or "fr" or "de" or "es" or "tr" or "it" or "pt" or "id" => TextScript.Latin,
            _ => TextScript.None
        };

        // Dominant script of the text, counting letters only (punctuation is ignored).
        private static TextScript DominantScript(string text)
        {
            var counts = new Dictionary<TextScript, int>();
            foreach (char c in text)
            {
                if (!char.IsLetter(c)) continue;
                TextScript script = ScriptOf(c);
                if (script == TextScript.None || script == TextScript.Other) continue;
                counts.TryGetValue(script, out int current);
                counts[script] = current + 1;
            }

            TextScript best = TextScript.None;
            int bestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return best;
        }

        // Determines the script of a character from its Unicode range.
        private static TextScript ScriptOf(char c)
        {
            int v = c;
            if ((v >= 0x0600 && v <= 0x06FF) || (v >= 0x0750 && v <= 0x077F)
                || (v >= 0x08A0 && v <= 0x08FF) || (v >= 0xFB50 && v <= 0xFDFF)
                || (v >= 0xFE70 && v <= 0xFEFF))
                return TextScript.Arabic;

            if ((v >= 0x0400 && v <= 0x04FF) || (v >= 0x0500 && v <= 0x052F))
                return TextScript.Cyrillic;

            if (v >= 0x0900 && v <= 0x097F) return TextScript.Devanagari;
            if (v >= 0x3040 && v <= 0x30FF) return TextScript.Kana;   // Hiragana + Katakana
            if ((v >= 0x3400 && v <= 0x4DBF) || (v >= 0x4E00 && v <= 0x9FFF)) return TextScript.Han;
            if ((v >= 0x1100 && v <= 0x11FF) || (v >= 0xAC00 && v <= 0xD7AF)) return TextScript.Hangul;

            if ((v >= 0x0041 && v <= 0x005A) || (v >= 0x0061 && v <= 0x007A)
                || (v >= 0x00C0 && v <= 0x024F))
                return TextScript.Latin;

            return TextScript.Other;
        }
    }
}
