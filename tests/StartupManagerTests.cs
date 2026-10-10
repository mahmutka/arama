using System.Linq;
using System.Xml.Linq;
using Arama.App;

namespace Arama.Tests
{
    public static class StartupManagerTests
    {
        static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        [Test]
        public static void GorevXmliDogruAyarlariIcerir()
        {
            string xml = StartupManager.BuildTaskXml(@"C:\Program Files\R&D\Arama.exe", @"PC\kullanıcı");
            XDocument doc = XDocument.Parse(xml);
            Assert.Equal(@"C:\Program Files\R&D\Arama.exe", Single(doc, "Command"), "komut (XML kaçışlı)");
            Assert.Equal("/tray", Single(doc, "Arguments"), "argüman");
            Assert.Equal("HighestAvailable", Single(doc, "RunLevel"), "yetki");
            Assert.Equal("PT0S", Single(doc, "ExecutionTimeLimit"), "süre sınırı yok");
            Assert.Equal("false", Single(doc, "DisallowStartIfOnBatteries"), "pilde başlar");
            Assert.Equal("false", Single(doc, "StopIfGoingOnBatteries"), "pilde durmaz");
            Assert.Equal(@"PC\kullanıcı", doc.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId").Value, "tetikleyici kullanıcısı");
        }

        static string Single(XDocument doc, string name)
        {
            return doc.Descendants(Ns + name).Single().Value;
        }

        [AdminTest]
        public static void GorevOlusturulupSilinir()
        {
            const string name = "Arama_Test";
            try
            {
                Assert.True(StartupManager.Enable(name, @"C:\Windows\notepad.exe"), "oluşturuldu");
                Assert.True(StartupManager.IsEnabled(name), "sorgulanınca var");
            }
            finally { StartupManager.Disable(name); }
            Assert.True(!StartupManager.IsEnabled(name), "silindi");
        }
    }
}
