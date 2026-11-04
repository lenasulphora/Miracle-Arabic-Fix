using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace Miracle
{
    // One language for the translation lists: service code, both names and the flag image.
    public sealed class Language
    {
        public string Code { get; }
        public string NameEn { get; }
        public string NameAr { get; }
        public string FlagFile { get; }

        private string _display = "";
        private BitmapImage? _flag;

        // Flag image from the assembly resources, loaded once on first use.
        public BitmapImage Flag => _flag ??= LoadFlag(FlagFile);

        // Text shown in the list, falling back to the English name.
        public string Display
        {
            get => string.IsNullOrEmpty(_display) ? NameEn : _display;
            set => _display = value;
        }

        public Language(string code, string nameEn, string nameAr, string flagFile)
        {
            Code = code; NameEn = nameEn; NameAr = nameAr; FlagFile = flagFile;
        }

        // Name in the current UI language: Arabic or English.
        public string Name(bool arabic) => arabic ? NameAr : NameEn;

        public override string ToString() => Display;

        // Loads the flag from its pack URI and freezes it so it can be shared across threads.
        private static BitmapImage LoadFlag(string file)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/Flags/{file}.png", UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }

    // Supported translation languages; the first entry (Arabic) is the fallback.
    public static class Languages
    {
        // Every available language with its translation code and flag file.
        public static readonly List<Language> All = new List<Language>
        {
            new Language("ar",    "Arabic",     "العربية",     "sa"),
            new Language("en",    "English",    "الإنجليزية",  "gb"),
            new Language("fr",    "French",     "الفرنسية",    "fr"),
            new Language("de",    "German",     "الألمانية",   "de"),
            new Language("es",    "Spanish",    "الإسبانية",   "es"),
            new Language("tr",    "Turkish",    "التركية",     "tr"),
            new Language("ru",    "Russian",    "الروسية",     "ru"),
            new Language("it",    "Italian",    "الإيطالية",   "it"),
            new Language("pt",    "Portuguese", "البرتغالية",  "pt"),
            new Language("hi",    "Hindi",      "الهندية",     "in"),
            new Language("zh-CN", "Chinese",    "الصينية",     "cn"),
            new Language("ja",    "Japanese",   "اليابانية",   "jp"),
            new Language("ko",    "Korean",     "الكورية",     "kr"),
            new Language("id",    "Indonesian", "الإندونيسية", "id")
        };

        // Finds a language by code (case-insensitive) and falls back to Arabic.
        public static Language Get(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return All[0];
            var hit = All.Find(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
            return hit ?? All[0];
        }
    }
}
