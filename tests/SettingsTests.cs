using System;
using System.IO;
using Arama.App;

namespace Arama.Tests
{
    public static class SettingsTests
    {
        static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "arama_ayar_" + Guid.NewGuid().ToString("N") + ".ini");
        }

        [Test]
        public static void KaydedilenAyarlarGeriOkunur()
        {
            string path = TempPath();
            try
            {
                var s = new Settings();
                s.X = 10; s.Y = 20; s.Width = 800; s.Height = 500;
                s.ColumnWidths = new[] { 100, 200, 80, 120 };
                s.Save(path);
                var t = Settings.Load(path);
                Assert.Equal(10, t.X, "X");
                Assert.Equal(20, t.Y, "Y");
                Assert.Equal(800, t.Width, "genişlik");
                Assert.Equal(500, t.Height, "yükseklik");
                Assert.SequenceEqual(new[] { 100, 200, 80, 120 }, t.ColumnWidths, "sütunlar");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public static void DosyaYoksaVarsayilanlar()
        {
            var s = Settings.Load(TempPath());
            Assert.Equal(-1, s.X, "X");
            Assert.Equal(1000, s.Width, "genişlik");
            Assert.SequenceEqual(new[] { 260, 420, 90, 130 }, s.ColumnWidths, "sütunlar");
        }

        [Test]
        public static void FiltreKaydedilmez()
        {
            // Program her açılışta "Tümü" ile başlar; dosyada Filtre satırı olsa da yok sayılır
            string path = TempPath();
            try
            {
                new Settings().Save(path);
                Assert.True(!File.ReadAllText(path).Contains("Filtre"), "ayar dosyasında filtre olmamalı");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public static void BozukDegerlerYokSayilir()
        {
            string path = TempPath();
            try
            {
                File.WriteAllText(path, "X=abc\nGenislik=50\nFiltre=99\nSutunlar=1,2\nbozuk satır\nY=7\n");
                var s = Settings.Load(path);
                Assert.Equal(-1, s.X, "sayı olmayan X");
                Assert.Equal(1000, s.Width, "çok küçük genişlik");
                Assert.SequenceEqual(new[] { 260, 420, 90, 130 }, s.ColumnWidths, "eksik sütunlar");
                Assert.Equal(7, s.Y, "geçerli Y okunur");
            }
            finally { File.Delete(path); }
        }
    }
}
