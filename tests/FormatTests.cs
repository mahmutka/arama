using System;
using Arama.UI;

namespace Arama.Tests
{
    public static class FormatTests
    {
        [Test]
        public static void BoyutExplorerGibiKbYukariYuvarlanir()
        {
            Assert.Equal("0 KB", Format.Size(0), "sıfır");
            Assert.Equal("1 KB", Format.Size(1), "1 bayt");
            Assert.Equal("1 KB", Format.Size(1024), "tam 1 KB");
            Assert.Equal("2 KB", Format.Size(1025), "yukarı yuvarlama");
            Assert.Equal("1.205.633 KB", Format.Size(1234567890), "binlik ayırıcı nokta");
        }

        [Test]
        public static void TarihYerelSaatleGunAyYilBicimindedir()
        {
            var local = new DateTime(2024, 3, 5, 14, 7, 0, DateTimeKind.Local);
            Assert.Equal("05.03.2024 14:07", Format.Date(local.ToFileTime()), "tarih");
            Assert.Equal("", Format.Date(0), "bilinmeyen tarih");
            Assert.Equal("", Format.Date(-5), "geçersiz tarih");
        }
    }
}
