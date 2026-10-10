using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Arama.App
{
    /// <summary>
    /// Pencere konumu/boyutu ve sütun genişlikleri. Bozuk/eksik değerlerde varsayılan kullanılır.
    /// Filtre bilerek saklanmaz: program her açılışta "Tümü" ile başlar (unutulmuş bir filtre dosyaları gizlemesin).
    /// </summary>
    public sealed class Settings
    {
        public int X = -1, Y = -1, Width = 1000, Height = 640;
        public int[] ColumnWidths = { 260, 420, 90, 130 };

        public static Settings Load(string path)
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(path)) return s;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    int n;
                    switch (key)
                    {
                        case "X": if (TryInt(value, out n)) s.X = n; break;
                        case "Y": if (TryInt(value, out n)) s.Y = n; break;
                        case "Genislik": if (TryInt(value, out n) && n >= 300) s.Width = n; break;
                        case "Yukseklik": if (TryInt(value, out n) && n >= 200) s.Height = n; break;
                        case "Sutunlar": ParseColumns(value, s); break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = new StringBuilder();
                text.AppendLine("X=" + X);
                text.AppendLine("Y=" + Y);
                text.AppendLine("Genislik=" + Width);
                text.AppendLine("Yukseklik=" + Height);
                text.AppendLine("Sutunlar=" + string.Join(",", ColumnWidths));
                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static void ParseColumns(string value, Settings s)
        {
            string[] parts = value.Split(',');
            if (parts.Length != 4) return;
            var widths = new int[4];
            for (int i = 0; i < 4; i++)
                if (!TryInt(parts[i], out widths[i]) || widths[i] < 20 || widths[i] > 4000) return;
            s.ColumnWidths = widths;
        }

        static bool TryInt(string text, out int value)
        {
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
