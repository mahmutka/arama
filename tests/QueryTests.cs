using System.Collections.Generic;
using System.Diagnostics;
using Arama.Index;
using Arama.Search;

namespace Arama.Tests
{
    public static class QueryTests
    {
        static readonly FileIndex C = SearchFixture.BuildC(true);

        static void Expect(string query, params string[] expected)
        {
            Assert.SequenceEqual(expected, SearchFixture.Match(C, query, FilterKind.All), "sorgu: " + query);
        }

        [Test]
        public static void BoslukVeAnlamindadir()
        {
            Expect("rapor 2024", "rapor 2024.docx");
        }

        [Test]
        public static void DikeyCizgiVeyaAnlamindadir()
        {
            Expect("pdf|docx", "rapor 2023.pdf", "rapor 2024.docx");
            Expect("pdf|docx 2024", "rapor 2024.docx");
        }

        [Test]
        public static void UnlemDegilAnlamindadir()
        {
            Expect("rapor !tmp", "rapor 2023.pdf", "rapor 2024.docx");
        }

        [Test]
        public static void TirnakBosluguKorur()
        {
            Expect("\"rapor 2\"", "rapor 2023.pdf", "rapor 2024.docx");
            Expect("\"rapor 2", "rapor 2023.pdf", "rapor 2024.docx");
        }

        [Test]
        public static void JokerIsminTamamiylaEslesir()
        {
            Expect("*.jpg", "IMG_0012.jpg");
            Expect("*.jp");
            Expect("n*t", "notlar.txt");
            Expect("\"rapor 202?.pdf\"", "rapor 2023.pdf");
        }

        [Test]
        public static void TurkceKarakterlerKatlanir()
        {
            Expect("ogrenci", "Öğrenci Listesi.xlsx");
            Expect("ÖĞRENCİ", "Öğrenci Listesi.xlsx");
            Expect("LİSTESİ", "Öğrenci Listesi.xlsx");
        }

        [Test]
        public static void TersBoluIcerenTerimYoldaAranir()
        {
            Expect(@"users\ali\ .txt", "notlar.txt");
            Expect(@"projeler\", "rapor 2023.pdf", "rapor 2024.docx", "tmp_rapor.tmp");
            Expect(@"c:\projeler\*.pdf", "rapor 2023.pdf");
        }

        [Test]
        public static void RegexIsmeVeKacisliTersBoluyleYolaUygulanir()
        {
            Expect(@"regex:^img_\d+", "IMG_0012.jpg");
            Expect("\"regex:^rapor 20(23|24)\"", "rapor 2023.pdf", "rapor 2024.docx");
            Expect(@"regex:projeler\\rapor", "rapor 2023.pdf", "rapor 2024.docx");
        }

        [Test]
        public static void GecersizRegexHataBildirirVeEslesmez()
        {
            Query q = Query.Parse("regex:([");
            Assert.True(q.Error != null, "hata mesajı");
            Expect("regex:([");
        }

        [Test]
        public static void FelaketRegexAramayiKilitlemez()
        {
            var idx = new FileIndex('c', 16);
            idx.Upsert(30, new string('a', 40) + "!", 5, 0);
            Query q = Query.Parse("regex:^(a+)+$");
            var sw = Stopwatch.StartNew();
            Assert.True(!q.IsMatch(idx, 30), "eşleşmemeli");
            Assert.True(sw.ElapsedMilliseconds < 2000, "zaman aşımı devreye girmeli: " + sw.ElapsedMilliseconds + " ms");
        }

        [Test]
        public static void BosVeAnlamsizSorgularHerSeyiDondurur()
        {
            foreach (string text in new[] { "", "   ", "!", "|", "\"\"", "!|!", "\"" })
            {
                Assert.True(Query.Parse(text).Root == null, "kök yok: [" + text + "]");
                Assert.Equal(C.LiveCount, SearchFixture.Match(C, text, FilterKind.All).Count, "hepsi: [" + text + "]");
            }
            Assert.True(Query.Parse(null).Root == null, "null sorgu");
        }

        [Test]
        public static void FiltrelerTureVeUzantiyaGoreSecer()
        {
            Assert.SequenceEqual(new[] { "ali", "Projeler", "Users" }, SearchFixture.Match(C, "", FilterKind.Folders), "klasörler");
            Assert.SequenceEqual(new[] { "IMG_0012.jpg" }, SearchFixture.Match(C, "", FilterKind.Images), "resimler");
            Assert.SequenceEqual(new[] { "notlar.txt", "Öğrenci Listesi.xlsx", "rapor 2023.pdf", "rapor 2024.docx", "readme.md" },
                SearchFixture.Match(C, "", FilterKind.Documents), "belgeler");
            Assert.Equal(7, SearchFixture.Match(C, "", FilterKind.Files).Count, "dosyalar");
            Assert.Equal(0, SearchFixture.Match(C, "", FilterKind.Video).Count, "video yok");
        }

        [Test]
        public static void UzantiBuyukKucukHarfDuyarsizVeSonUzantiyaBakar()
        {
            var idx = new FileIndex('c', 16);
            idx.Upsert(30, "FOTO.JPEG", 5, 0);
            idx.Upsert(31, "jpeg", 5, 0);
            idx.Upsert(32, "a.jpeg.txt", 5, 0);
            Assert.True(Filters.Accept(FilterKind.Images, idx, 30), "FOTO.JPEG");
            Assert.True(!Filters.Accept(FilterKind.Images, idx, 31), "uzantısız");
            Assert.True(!Filters.Accept(FilterKind.Images, idx, 32), "son uzantı txt");
            Assert.Equal(9, Filters.DisplayNames.Length, "filtre adı sayısı");
        }
    }
}
