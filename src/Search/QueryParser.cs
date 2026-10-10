using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Arama.Index;

namespace Arama.Search
{
    public abstract class Matcher
    {
        public abstract bool IsMatch(FileIndex index, int id);

        /// <summary>Yalnızca isim üzerinden eşleştirme (yol terimi içermeyen sorgularda, sıralı tampon taraması için).</summary>
        public abstract bool IsMatchName(char[] buffer, int offset, int length);
    }

    sealed class SubstringMatcher : Matcher
    {
        readonly string needle;
        readonly bool onPath;

        public SubstringMatcher(string foldedNeedle, bool onPath)
        {
            needle = foldedNeedle;
            this.onPath = onPath;
        }

        public override bool IsMatch(FileIndex index, int id)
        {
            if (onPath)
            {
                int length;
                char[] path = TextMatch.PathChars(index, id, out length);
                return TextMatch.Contains(path, 0, length, needle);
            }
            return IsMatchName(index.names, index.nameOffset[id], index.nameLength[id]);
        }

        public override bool IsMatchName(char[] buffer, int offset, int length)
        {
            return TextMatch.Contains(buffer, offset, length, needle);
        }
    }

    sealed class WildcardMatcher : Matcher
    {
        readonly string pattern;
        readonly bool onPath;

        public WildcardMatcher(string foldedPattern, bool onPath)
        {
            pattern = foldedPattern;
            this.onPath = onPath;
        }

        public override bool IsMatch(FileIndex index, int id)
        {
            if (onPath)
            {
                int length;
                char[] path = TextMatch.PathChars(index, id, out length);
                return TextMatch.Wildcard(path, 0, length, pattern);
            }
            return IsMatchName(index.names, index.nameOffset[id], index.nameLength[id]);
        }

        public override bool IsMatchName(char[] buffer, int offset, int length)
        {
            return TextMatch.Wildcard(buffer, offset, length, pattern);
        }
    }

    sealed class RegexMatcher : Matcher
    {
        readonly Regex regex;
        readonly bool onPath;

        public RegexMatcher(Regex regex, bool onPath)
        {
            this.regex = regex;
            this.onPath = onPath;
        }

        public override bool IsMatch(FileIndex index, int id)
        {
            return Test(onPath ? index.GetFullPath(id) : index.GetName(id));
        }

        public override bool IsMatchName(char[] buffer, int offset, int length)
        {
            return Test(new string(buffer, offset, length));
        }

        bool Test(string text)
        {
            try { return regex.IsMatch(text); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }

    sealed class NeverMatcher : Matcher
    {
        public static readonly NeverMatcher Instance = new NeverMatcher();
        public override bool IsMatch(FileIndex index, int id) { return false; }
        public override bool IsMatchName(char[] buffer, int offset, int length) { return false; }
    }

    sealed class NotMatcher : Matcher
    {
        readonly Matcher inner;
        public NotMatcher(Matcher inner) { this.inner = inner; }
        public override bool IsMatch(FileIndex index, int id) { return !inner.IsMatch(index, id); }
        public override bool IsMatchName(char[] buffer, int offset, int length) { return !inner.IsMatchName(buffer, offset, length); }
    }

    sealed class AnyMatcher : Matcher
    {
        readonly Matcher[] items;
        public AnyMatcher(Matcher[] items) { this.items = items; }

        public override bool IsMatch(FileIndex index, int id)
        {
            foreach (var m in items)
                if (m.IsMatch(index, id)) return true;
            return false;
        }

        public override bool IsMatchName(char[] buffer, int offset, int length)
        {
            foreach (var m in items)
                if (m.IsMatchName(buffer, offset, length)) return true;
            return false;
        }
    }

    sealed class AllMatcher : Matcher
    {
        readonly Matcher[] items;
        public AllMatcher(Matcher[] items) { this.items = items; }

        public override bool IsMatch(FileIndex index, int id)
        {
            foreach (var m in items)
                if (!m.IsMatch(index, id)) return false;
            return true;
        }

        public override bool IsMatchName(char[] buffer, int offset, int length)
        {
            foreach (var m in items)
                if (!m.IsMatchName(buffer, offset, length)) return false;
            return true;
        }
    }

    /// <summary>Katlanmış (TextFold) karşılaştırmalı metin eşleştirme yardımcıları.</summary>
    static class TextMatch
    {
        [ThreadStatic] static char[] pathBuffer;

        public static bool Contains(char[] s, int offset, int length, string needle)
        {
            int n = needle.Length;
            if (n == 0) return true;
            if (n > length) return false;
            char first = needle[0];
            int last = offset + length - n;
            for (int i = offset; i <= last; i++)
            {
                if (TextFold.Fold(s[i]) != first) continue;
                int j = 1;
                while (j < n && TextFold.Fold(s[i + j]) == needle[j]) j++;
                if (j == n) return true;
            }
            return false;
        }

        /// <summary>'*' herhangi bir dizi, '?' tek karakter; desen metnin tamamıyla eşleşmeli.</summary>
        public static bool Wildcard(char[] s, int offset, int length, string pattern)
        {
            int si = 0, pi = 0, star = -1, mark = 0;
            while (si < length)
            {
                if (pi < pattern.Length && (pattern[pi] == '?' || pattern[pi] == TextFold.Fold(s[offset + si])))
                {
                    si++;
                    pi++;
                }
                else if (pi < pattern.Length && pattern[pi] == '*')
                {
                    star = pi++;
                    mark = si;
                }
                else if (star >= 0)
                {
                    pi = star + 1;
                    si = ++mark;
                }
                else return false;
            }
            while (pi < pattern.Length && pattern[pi] == '*') pi++;
            return pi == pattern.Length;
        }

        /// <summary>Tam yolu iş parçacığına özel tampona kopyalar (char[] tabanlı eşleştiriciler için).</summary>
        public static char[] PathChars(FileIndex index, int id, out int length)
        {
            string path = index.GetFullPath(id);
            length = path.Length;
            char[] buffer = pathBuffer;
            if (buffer == null || buffer.Length < length) pathBuffer = buffer = new char[Math.Max(length, 1024)];
            path.CopyTo(0, buffer, 0, length);
            return buffer;
        }
    }

    /// <summary>
    /// Arama metni: boşluk = VE, | = VEYA (VE'den önce bağlar), ! = DEĞİL, "..." = boşluklu ifade,
    /// * ve ? = joker (ismin tamamı), \ içeren terim = tam yol, regex: = düzenli ifade.
    /// </summary>
    public sealed class Query
    {
        public readonly Matcher Root;
        public readonly string Error;
        public readonly bool NeedsPath;

        Query(Matcher root, string error, bool needsPath)
        {
            Root = root;
            Error = error;
            NeedsPath = needsPath;
        }

        public bool IsMatch(FileIndex index, int id)
        {
            return Root == null || Root.IsMatch(index, id);
        }

        /// <summary>Yalnızca isimle eşleştirme; NeedsPath doğruysa kullanılmamalı.</summary>
        public bool IsMatchName(char[] buffer, int offset, int length)
        {
            return Root == null || Root.IsMatchName(buffer, offset, length);
        }

        static readonly char[] SpecialChars = { '|', '!', '"', '*', '?', '\\', ':' };

        /// <summary>
        /// Yeni sorgu öncekinin düz metin uzantısıysa (harf ya da terim eklendi), yeni sonuçlar öncekilerin
        /// alt kümesidir; yalnızca önceki sonuçlar taranabilir. Özel sözdiziminde (| ! " * ? \ regex:) bu garanti yoktur.
        /// </summary>
        public static bool CanNarrow(string previousText, string newText)
        {
            if (string.IsNullOrWhiteSpace(previousText) || newText == null) return false;
            if (newText.Length <= previousText.Length || !newText.StartsWith(previousText, StringComparison.Ordinal)) return false;
            return newText.IndexOfAny(SpecialChars) < 0;
        }

        public static Query Parse(string text)
        {
            var groups = new List<Matcher>();
            string error = null;
            bool needsPath = false;
            foreach (string token in Split(text ?? "", ' '))
            {
                var alternatives = new List<Matcher>();
                foreach (string alternative in Split(token, '|'))
                {
                    string term = alternative;
                    bool negate = false;
                    if (term.StartsWith("!"))
                    {
                        negate = true;
                        term = term.Substring(1);
                    }
                    term = term.Replace("\"", "");
                    if (term.Length == 0) continue;
                    string termError;
                    bool termOnPath;
                    Matcher matcher = CreateTerm(term, out termError, out termOnPath);
                    if (termError != null && error == null) error = termError;
                    needsPath |= termOnPath;
                    alternatives.Add(negate ? new NotMatcher(matcher) : matcher);
                }
                if (alternatives.Count == 1) groups.Add(alternatives[0]);
                else if (alternatives.Count > 1) groups.Add(new AnyMatcher(alternatives.ToArray()));
            }
            Matcher root = groups.Count == 0 ? null : groups.Count == 1 ? groups[0] : new AllMatcher(groups.ToArray());
            return new Query(root, error, needsPath);
        }

        static Matcher CreateTerm(string term, out string error, out bool onPath)
        {
            error = null;
            if (term.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                string pattern = term.Substring(6);
                onPath = pattern.Contains("\\\\");
                try
                {
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    return new RegexMatcher(regex, onPath);
                }
                catch (ArgumentException)
                {
                    error = "Geçersiz regex: " + pattern;
                    return NeverMatcher.Instance;
                }
            }
            onPath = term.IndexOf('\\') >= 0;
            string folded = TextFold.Fold(term);
            if (term.IndexOf('*') >= 0 || term.IndexOf('?') >= 0) return new WildcardMatcher(folded, onPath);
            return new SubstringMatcher(folded, onPath);
        }

        /// <summary>Tırnak dışındaki ayraçtan böler (boşluk için tüm boşluk karakterleri); tırnaklar korunur.</summary>
        static List<string> Split(string text, char separator)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool inQuote = false;
            foreach (char c in text)
            {
                bool isSeparator = separator == ' ' ? char.IsWhiteSpace(c) : c == separator;
                if (c == '"') inQuote = !inQuote;
                if (isSeparator && !inQuote)
                {
                    if (current.Length > 0) parts.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }
    }
}
