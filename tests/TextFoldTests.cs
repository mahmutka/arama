using Arama.Search;

namespace Arama.Tests
{
    public static class TextFoldTests
    {
        [Test]
        public static void TurkceKarakterlerKatlanir()
        {
            Assert.Equal("ogrenci isleri", TextFold.Fold("Öğrenci İşleri"), "Türkçe katlama");
            Assert.Equal("cigdem", TextFold.Fold("ÇİĞDEM"), "büyük Türkçe harfler");
            Assert.Equal("istanbul", TextFold.Fold("ıSTANBUL"), "ı ve I");
            Assert.Equal("usul", TextFold.Fold("ÜŞUL"), "ü ve ş");
        }

        [Test]
        public static void DigerHarflerKucukHarfeCevrilir()
        {
            Assert.Equal("readme.txt", TextFold.Fold("ReadMe.TXT"), "ASCII");
            Assert.Equal('ж', TextFold.Fold('Ж'), "Kiril");
            Assert.Equal('7', TextFold.Fold('7'), "rakam aynı kalır");
        }
    }
}
