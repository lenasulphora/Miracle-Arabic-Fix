using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Miracle
{
    public static class ArabicEngine
    {
        // Outbound fix: shapes Arabic letters and orders them visually for display inside the game.
        public static async Task<string> Fix(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return await Task.Run(() =>
            {
                string shaped = ArabicShaper.Shape(input);

                return ProcessBiDi(shaped);
            });
        }

        public static async Task<string> ToLogicalTextAsync(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            if (!ContainsPresentationForms(input))
                return input;

            if (AnalyzePresentationOrder(input) == PresentationOrder.Visual)
            {
                string fixedText = await Fix(input);
                return fixedText.Normalize(NormalizationForm.FormKC);
            }

            return input.Normalize(NormalizationForm.FormKC);
        }

        // Order of shaped text: ambiguous, logical, or visual.
        private enum PresentationOrder { Ambiguous, Logical, Visual }

        // Letter position within a word: isolated, initial, medial, or final.
        [Flags]
        private enum ArabicForm : byte
        {
            None = 0,
            Isolated = 1,
            Initial = 2,
            Medial = 4,
            Final = 8
        }

        // Position flags for every presentation-form char, built once at load time.
        private static readonly Dictionary<char, ArabicForm> PresentationPositions =
            ArabicShaper.PresentationPositionBits()
                .ToDictionary(pair => pair.Key, pair => (ArabicForm)pair.Value);

        // Does the text contain any char from the two Arabic Presentation Forms ranges?
        private static bool ContainsPresentationForms(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
                    return true;
            }
            return false;
        }

        private static PresentationOrder AnalyzePresentationOrder(string input)
        {
            int logicalWords = 0;
            int visualWords = 0;

            string normalizedLines = input.Replace("\r\n", "\n").Replace("\r", "\n");
            foreach (string line in normalizedLines.Split('\n'))
            {
                foreach (string word in ArabicWords(line))
                {
                    var forms = new List<ArabicForm>(word.Length);
                    bool hasBaseLetter = false;
                    foreach (char c in word)
                    {
                        if (PresentationPositions.TryGetValue(c, out ArabicForm form))
                            forms.Add(form);
                        else if (IsBaseArabicLetter(c))
                            hasBaseLetter = true;
                    }

                    if (hasBaseLetter) continue;

                    switch (AnalyzeWordOrder(forms))
                    {
                        case PresentationOrder.Logical: logicalWords++; break;
                        case PresentationOrder.Visual: visualWords++; break;
                    }
                }
            }

            if (logicalWords > 0 && visualWords == 0) return PresentationOrder.Logical;
            if (visualWords > 0 && logicalWords == 0) return PresentationOrder.Visual;
            return PresentationOrder.Ambiguous;
        }

        private static PresentationOrder AnalyzeWordOrder(List<ArabicForm> forms)
        {
            if (forms.Count == 0) return PresentationOrder.Ambiguous;

            ArabicForm first = forms[0];
            ArabicForm last = forms[^1];

            bool edgeLogical = Allows(first, ArabicForm.Isolated | ArabicForm.Initial)
                               && Allows(last, ArabicForm.Isolated | ArabicForm.Final);
            bool edgeVisual = Allows(first, ArabicForm.Isolated | ArabicForm.Final)
                              && Allows(last, ArabicForm.Isolated | ArabicForm.Initial);

            if (!edgeLogical && !edgeVisual) return PresentationOrder.Ambiguous;

            int logicalPairs = 0;
            int visualPairs = 0;
            for (int i = 0; i + 1 < forms.Count; i++)
            {
                int order = PairOrder(forms[i], forms[i + 1]);
                if (order > 0) logicalPairs++;
                else if (order < 0) visualPairs++;
            }

            if (edgeLogical && !edgeVisual)
            {
                // A clear logical edge with only visual inner pairs conflicts, so do not override.
                return visualPairs > 0 && logicalPairs == 0
                    ? PresentationOrder.Ambiguous
                    : PresentationOrder.Logical;
            }

            if (edgeVisual && !edgeLogical)
            {
                return logicalPairs > 0 && visualPairs == 0
                    ? PresentationOrder.Ambiguous
                    : PresentationOrder.Visual;
            }

            if (logicalPairs > 0 && visualPairs == 0) return PresentationOrder.Logical;
            if (visualPairs > 0 && logicalPairs == 0) return PresentationOrder.Visual;
            return PresentationOrder.Ambiguous;
        }

        // Is this form one of the positions allowed by the mask?
        private static bool Allows(ArabicForm form, ArabicForm allowed)
            => (form & allowed) != 0;

        private static int PairOrder(ArabicForm first, ArabicForm second)
        {
            if (!IsSingleForm(first) || !IsSingleForm(second)) return 0;

            bool firstForward = first is ArabicForm.Initial or ArabicForm.Medial;
            bool secondBackward = second is ArabicForm.Medial or ArabicForm.Final;
            bool secondForward = second is ArabicForm.Initial or ArabicForm.Medial;
            bool firstBackward = first is ArabicForm.Medial or ArabicForm.Final;

            bool logical = firstForward == secondBackward;
            bool visual = secondForward == firstBackward;

            if (logical == visual) return 0;
            return logical ? 1 : -1;
        }

        // Is the form a single position only (no ambiguity)?
        private static bool IsSingleForm(ArabicForm form)
        {
            return form is ArabicForm.Isolated or ArabicForm.Initial
                or ArabicForm.Medial or ArabicForm.Final;
        }

        // Splits a line into consecutive Arabic words (letters, presentation forms, or marks).
        private static IEnumerable<string> ArabicWords(string line)
        {
            var word = new StringBuilder();
            foreach (char c in line)
            {
                if (IsArabicWordChar(c))
                {
                    word.Append(c);
                }
                else if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }
            }
            if (word.Length > 0) yield return word.ToString();
        }

        // Is the char part of an Arabic word (presentation form, base letter, or mark)?
        private static bool IsArabicWordChar(char c)
            => PresentationPositions.ContainsKey(c) || IsBaseArabicLetter(c) || IsArabicMark(c);

        // Base Arabic letters (including tatweel) and extended letters in the supplementary ranges.
        private static bool IsBaseArabicLetter(char c)
        {
            return (c >= 0x0621 && c <= 0x064A)   // core Arabic letters + tatweel
                   || (c >= 0x066E && c <= 0x06D3)
                   || (c >= 0x06FA && c <= 0x06FC)
                   || (c >= 0x0750 && c <= 0x077F)
                   || (c >= 0x08A0 && c <= 0x08FF);
        }

        // Arabic diacritics and marks, counted as part of the word when classifying.
        private static bool IsArabicMark(char c)
        {
            return (c >= 0x0610 && c <= 0x061A)
                   || (c >= 0x064B && c <= 0x065F)
                   || c == 0x0670
                   || (c >= 0x06D6 && c <= 0x06ED)
                   || (c >= 0x08D3 && c <= 0x08E9);
        }

        private static string ProcessBiDi(string input)
        {
            input = input.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = input.Split('\n');
            var result = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { result.Add(line); continue; }

                var blocks = SplitIntoDirectionalBlocks(line);
                blocks.Reverse();

                StringBuilder sb = new StringBuilder();
                foreach (var block in blocks)
                {
                    if (IsArabicBlock(block))
                        sb.Append(ReverseText(block));
                    else
                        sb.Append(block);
                }
                result.Add(sb.ToString());
            }

            return string.Join(Environment.NewLine, result);
        }

        // Splits a line into consecutive uniform blocks: Arabic, then non-Arabic, and so on.
        private static List<string> SplitIntoDirectionalBlocks(string text)
        {
            var blocks = new List<string>();
            if (string.IsNullOrEmpty(text)) return blocks;

            StringBuilder currentBlock = new StringBuilder();
            bool isCurrentArabic = IsArabicChar(text[0]);

            foreach (char c in text)
            {
                bool isArabic = IsArabicChar(c);
                if (isArabic == isCurrentArabic)
                {
                    currentBlock.Append(c);
                }
                else
                {
                    blocks.Add(currentBlock.ToString());
                    currentBlock.Clear();
                    currentBlock.Append(c);
                    isCurrentArabic = isArabic;
                }
            }
            blocks.Add(currentBlock.ToString());
            return blocks;
        }

        // Is the char Arabic (base letters or presentation forms)?
        private static bool IsArabicChar(char c)
        {
            return (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFEFF);
        }

        // Does the block contain any Arabic char?
        private static bool IsArabicBlock(string block)
        {
            return block.Any(c => IsArabicChar(c));
        }

        // Reverses text char by char, swapping brackets and paired marks so they still read right.
        private static string ReverseText(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);

            for (int i = 0; i < charArray.Length; i++)
            {
                if (charArray[i] == '(') charArray[i] = ')';
                else if (charArray[i] == ')') charArray[i] = '(';
                else if (charArray[i] == '[') charArray[i] = ']';
                else if (charArray[i] == ']') charArray[i] = '[';
                else if (charArray[i] == '{') charArray[i] = '}';
                else if (charArray[i] == '}') charArray[i] = '{';
                else if (charArray[i] == '<') charArray[i] = '>';
                else if (charArray[i] == '>') charArray[i] = '<';
                else if (charArray[i] == '«') charArray[i] = '»';
                else if (charArray[i] == '»') charArray[i] = '«';
            }
            return new string(charArray);
        }
    }

    internal static class ArabicShaper
    {
        // The letter's four forms within a word.
        private struct ArabicGlyph
        {
            public char Isolated, Initial, Medial, Final;
            public ArabicGlyph(char iso, char ini, char med, char fin)
            { Isolated = iso; Initial = ini; Medial = med; Final = fin; }
        }

        // Letter form table. Lam-alef ligatures are treated as one unit of two characters.
        private static Dictionary<char, ArabicGlyph> Glyphs = new Dictionary<char, ArabicGlyph>() {
            {'ا', new ArabicGlyph('ﺍ', 'ﺍ', 'ﺎ', 'ﺎ')},
            {'ب', new ArabicGlyph('ﺏ', 'ﺑ', 'ﺒ', 'ﺐ')},
            {'ت', new ArabicGlyph('ﺕ', 'ﺗ', 'ﺘ', 'ﺖ')},
            {'ث', new ArabicGlyph('ﺙ', 'ﺛ', 'ﺜ', 'ﺚ')},
            {'ج', new ArabicGlyph('ﺝ', 'ﺟ', 'ﺠ', 'ﺞ')},
            {'ح', new ArabicGlyph('ﺡ', 'ﺣ', 'ﺤ', 'ﺢ')},
            {'خ', new ArabicGlyph('ﺥ', 'ﺧ', 'ﺨ', 'ﺦ')},
            {'د', new ArabicGlyph('ﺩ', 'ﺩ', 'ﺪ', 'ﺪ')},
            {'ذ', new ArabicGlyph('ﺫ', 'ﺫ', 'ﺬ', 'ﺬ')},
            {'ر', new ArabicGlyph('ﺭ', 'ﺭ', 'ﺮ', 'ﺮ')},
            {'ز', new ArabicGlyph('ﺯ', 'ﺯ', 'ﺰ', 'ﺰ')},
            {'س', new ArabicGlyph('ﺱ', 'ﺳ', 'ﺴ', 'ﺲ')},
            {'ش', new ArabicGlyph('ﺵ', 'ﺷ', 'ﺸ', 'ﺶ')},
            {'ص', new ArabicGlyph('ﺹ', 'ﺻ', 'ﺼ', 'ﺺ')},
            {'ض', new ArabicGlyph('ﺽ', 'ﺿ', 'ﻀ', 'ﺾ')},
            {'ط', new ArabicGlyph('ﻁ', 'ﻃ', 'ﻄ', 'ﻂ')},
            {'ظ', new ArabicGlyph('ﻅ', 'ﻅ', 'ﻈ', 'ﻆ')},
            {'ع', new ArabicGlyph('ﻉ', 'ﻋ', 'ﻌ', 'ﻊ')},
            {'غ', new ArabicGlyph('ﻍ', 'ﻏ', 'ﻐ', 'ﻎ')},
            {'ف', new ArabicGlyph('ﻑ', 'ﻓ', 'ﻔ', 'ﻒ')},
            {'ق', new ArabicGlyph('ﻕ', 'ﻗ', 'ﻘ', 'ﻖ')},
            {'ك', new ArabicGlyph('ﻙ', 'ﻛ', 'ﻜ', 'ﻚ')},
            {'ل', new ArabicGlyph('ﻝ', 'ﻟ', 'ﻠ', 'ﻞ')},
            {'م', new ArabicGlyph('ﻡ', 'ﻣ', 'ﻤ', 'ﻢ')},
            {'ن', new ArabicGlyph('ﻥ', 'ﻧ', 'ﻨ', 'ﻦ')},
            {'ه', new ArabicGlyph('ﻩ', 'ﻫ', 'ﻬ', 'ﻪ')},
            {'و', new ArabicGlyph('ﻭ', 'ﻭ', 'ﻮ', 'ﻮ')},
            {'ي', new ArabicGlyph('ﻱ', 'ﻳ', 'ﻴ', 'ﻲ')},
            {'آ', new ArabicGlyph('ﺁ', 'ﺁ', 'ﺂ', 'ﺂ')},
            {'أ', new ArabicGlyph('ﺃ', 'ﺃ', 'ﺄ', 'ﺄ')},
            {'ؤ', new ArabicGlyph('ﺅ', 'ﺅ', 'ﺆ', 'ﺆ')},
            {'إ', new ArabicGlyph('ﺇ', 'ﺇ', 'ﺈ', 'ﺈ')},
            {'ئ', new ArabicGlyph('ﺉ', 'ﺋ', 'ﺌ', 'ﺊ')},
            {'ة', new ArabicGlyph('ﺓ', 'ﺓ', 'ﺔ', 'ﺔ')},
            {'ى', new ArabicGlyph('ﻯ', 'ﻯ', 'ﻰ', 'ﻰ')},
            {'گ', new ArabicGlyph('ﮒ', 'ﮔ', 'ﮕ', 'ﮓ')},
            {'پ', new ArabicGlyph('ﭖ', 'ﭘ', 'ﭙ', 'ﭗ')},
            {'چ', new ArabicGlyph('ﭺ', 'ﭼ', 'ﭽ', 'ﭻ')},
            {'ڤ', new ArabicGlyph('ﭪ', 'ﭬ', 'ﭭ', 'ﭫ')},
            {'ژ', new ArabicGlyph('ﮊ', 'ﮊ', 'ﮋ', 'ﮋ')},
            {'ﻻ', new ArabicGlyph('ﻻ', 'ﻻ', 'ﻼ', 'ﻼ')},
            {'ﻷ', new ArabicGlyph('ﻷ', 'ﻷ', 'ﻸ', 'ﻸ')},
            {'ﻹ', new ArabicGlyph('ﻹ', 'ﻹ', 'ﻺ', 'ﻺ')},
            {'ﻵ', new ArabicGlyph('ﻵ', 'ﻵ', 'ﻶ', 'ﻶ')}
        };

        // Letters that do not connect to what follows, plus ligatures ending in a non-joining letter.
        private static bool Disconnects(char c)
        {
            return "ادذرزوآأإؤةى".Contains(c) || c == 'ﻻ' || c == 'ﻷ' || c == 'ﻹ' || c == 'ﻵ';
        }

        public static Dictionary<char, byte> PresentationPositionBits()
        {
            var positions = new Dictionary<char, byte>();

            void Add(char c, byte bit)
            {
                positions.TryGetValue(c, out byte existing);
                positions[c] = (byte)(existing | bit);
            }

            foreach (ArabicGlyph glyph in Glyphs.Values)
            {
                Add(glyph.Isolated, 1);   // isolated
                Add(glyph.Initial, 2);    // initial
                Add(glyph.Medial, 4);     // medial
                Add(glyph.Final, 8);      // final
            }
            return positions;
        }

        public static string Shape(string input)
        {
            input = input.Replace("لآ", "ﻵ").Replace("لأ", "ﻷ").Replace("لإ", "ﻹ").Replace("لا", "ﻻ");

            char[] chars = input.ToCharArray();
            char[] output = new char[chars.Length];

            for (int i = 0; i < chars.Length; i++)
            {
                char current = chars[i];
                if (!Glyphs.ContainsKey(current))
                {
                    output[i] = current;
                    continue;
                }

                bool prevConnects = (i > 0 && Glyphs.ContainsKey(chars[i - 1]) && !Disconnects(chars[i - 1]));
                bool nextConnects = (i < chars.Length - 1 && Glyphs.ContainsKey(chars[i + 1]));

                if (prevConnects && nextConnects) output[i] = Glyphs[current].Medial;
                else if (prevConnects) output[i] = Glyphs[current].Final;
                else if (nextConnects) output[i] = Glyphs[current].Initial;
                else output[i] = Glyphs[current].Isolated;
            }
            return new string(output);
        }
    }
}
