namespace Miracle
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    // Settings serialised to config.json; every property here maps to a saved key.
    public class Config
    {
        // Font size of the typing box.
        public double FS { get; set; } = 22;
        public string Target { get; set; } = "";
        public string GameExe { get; set; } = "";
        public int Speed { get; set; } = 300;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SendMode SendMode { get; set; } = SendMode.Auto;
        public double WinW { get; set; } = 452;
        public double WinH { get; set; } = 196;
        public double? WinX { get; set; } = null;
        public double? WinY { get; set; } = null;
        // Easy Mode (send to the game) and CORRECT (fix copied game text) start enabled.
        public bool Easy { get; set; } = true;
        public bool Trans { get; set; } = true;
        public bool Translate { get; set; } = false;
        public string SrcLang { get; set; } = "ar";
        public string DstLang { get; set; } = "en";
        public bool GlobalShortcuts { get; set; } = false;
        [JsonPropertyName("AutoArabicKeyboard")]
        public bool RtlSupport { get; set; } = true;
        public bool Mini { get; set; } = true;
        public bool Transparent { get; set; } = false;
        public string HideKey1 { get; set; } = "None";
        public string HideKey2 { get; set; } = "Insert";
        public string Lang { get; set; } = "EN";
        public string Translator { get; set; } = "Google";
        // DeepL key kept in memory only; the file stores the encrypted DeepLKeyProtected value.
        [JsonIgnore]
        public string DeepLKey { get; set; } = "";

        // Legacy key: old configs saved the DeepL key in plain text, reading moves it into DeepLKey.
        [JsonPropertyName("DeepLKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyDeepLKey
        {
            get => null;
            set { if (value != null) DeepLKey = value; }
        }
        // The DPAPI-encrypted DeepL key; this is what is actually written to disk.
        public string DeepLKeyProtected { get; set; } = "";

        // User-defined shortcuts, each one pairs a key combo with ready text.
        public List<ShortcutItem> Shortcuts { get; set; } = new List<ShortcutItem>();
    }

    // One shortcut: a key combo (Key1 + Key2) and the text pasted when it is pressed.
    public class ShortcutItem
    {
        public string Key1 { get; set; } = "None";
        public string Key2 { get; set; } = "None";

        // Text sent to the game when the shortcut fires.
        public string TextToPaste { get; set; } = "";

        // When enabled the text is sent as written, without any Arabic correction.
        public bool KeepTextAsWritten { get; set; }
    }
}
