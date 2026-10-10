namespace Arama.Search
{
    /// <summary>
    /// Büyük/küçük harf ve Türkçe karakter duyarsız karşılaştırma için 65.536 elemanlı katlama tablosu.
    /// </summary>
    public static class TextFold
    {
        static readonly char[] Table = BuildTable();

        public static char Fold(char c)
        {
            return Table[c];
        }

        public static string Fold(string s)
        {
            var chars = new char[s.Length];
            for (int i = 0; i < s.Length; i++) chars[i] = Table[s[i]];
            return new string(chars);
        }

        static char[] BuildTable()
        {
            var table = new char[65536];
            for (int i = 0; i < table.Length; i++) table[i] = char.ToLowerInvariant((char)i);
            table['I'] = 'i'; table['İ'] = 'i'; table['ı'] = 'i';
            table['Ç'] = 'c'; table['ç'] = 'c';
            table['Ğ'] = 'g'; table['ğ'] = 'g';
            table['Ö'] = 'o'; table['ö'] = 'o';
            table['Ş'] = 's'; table['ş'] = 's';
            table['Ü'] = 'u'; table['ü'] = 'u';
            return table;
        }
    }
}
