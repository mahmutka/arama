using System;
using System.IO;
using Arama.App;

namespace Arama.Tests
{
    public static class ErrorLogTests
    {
        [Test]
        public static void MesajVeHataDosyayaEklenir()
        {
            string path = Path.Combine(Path.GetTempPath(), "arama_hata_" + Guid.NewGuid().ToString("N") + ".log");
            ErrorLog.FileOverride = path;
            try
            {
                ErrorLog.Write("deneme mesajı");
                ErrorLog.Write(new InvalidOperationException("örnek hata"));
                string text = File.ReadAllText(path);
                Assert.True(text.Contains("deneme mesajı"), "mesaj yazıldı");
                Assert.True(text.Contains("InvalidOperationException") && text.Contains("örnek hata"), "hata yazıldı");
            }
            finally
            {
                ErrorLog.FileOverride = null;
                File.Delete(path);
            }
        }
    }
}
