using System;
using System.Globalization;

namespace Arama.UI
{
    public static class Format
    {
        public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

        /// <summary>Explorer gibi: KB, yukarı yuvarlanır ("1.234 KB").</summary>
        public static string Size(long bytes)
        {
            long kb = (Math.Max(0, bytes) + 1023) / 1024;
            return kb.ToString("N0", Turkish) + " KB";
        }

        public static string Date(long fileTimeUtc)
        {
            if (fileTimeUtc <= 0) return "";
            try { return DateTime.FromFileTimeUtc(fileTimeUtc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", Turkish); }
            catch (ArgumentOutOfRangeException) { return ""; }
        }
    }
}
