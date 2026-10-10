using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Arama.Index;
using Arama.Search;

namespace Arama.Tests
{
    /// <summary>Artımlı güncelleme, aynı değişiklik sonrası tam aramayla birebir aynı sonucu vermeli.</summary>
    public static class SearchUpdateTests
    {
        static SearchResult Full(FileIndex[] indexes, string query, FilterKind filter, SortColumn sort, bool descending)
        {
            return SearchEngine.Run(indexes, Query.Parse(query), filter, sort, descending, CancellationToken.None);
        }

        static SearchResult Update(SearchResult before, int volume, int[] changed, string query, FilterKind filter, SortColumn sort, bool descending)
        {
            return SearchEngine.Update(before, volume, changed, Query.Parse(query), filter, sort, descending);
        }

        static List<string> Names(SearchResult result)
        {
            var names = new List<string>();
            foreach (var item in result.Items) names.Add(result.Indexes[item.Volume].GetName(item.Id));
            return names;
        }

        static void SameAsFull(SearchResult updated, FileIndex[] indexes, string query, FilterKind filter, SortColumn sort, bool descending, string message)
        {
            Assert.True(updated != null, message + ": artımlı güncelleme yapılmalıydı");
            SearchResult full = Full(indexes, query, filter, sort, descending);
            Assert.SequenceEqual(Names(full), Names(updated), message);
            Assert.Equal(full.TotalLive, updated.TotalLive, message + ": toplam nesne");
        }

        [Test]
        public static void YeniEslesenDosyaDogruYereEklenir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "rapor", FilterKind.All, SortColumn.Name, false);
            idx[0].Upsert(47, "rapor 2000.txt", 32, 0x20);
            var after = Update(before, 0, new[] { 47 }, "rapor", FilterKind.All, SortColumn.Name, false);
            SameAsFull(after, idx, "rapor", FilterKind.All, SortColumn.Name, false, "yeni dosya");
        }

        [Test]
        public static void IlgisizDegisiklikAyniSonucuDondurur()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "rapor", FilterKind.All, SortColumn.Name, false);
            idx[0].Upsert(48, "log.tmp", 32, 0x20);
            idx[0].SetInfo(40, 99, 99, 0x20);
            var after = Update(before, 0, new[] { 48, 40 }, "rapor", FilterKind.All, SortColumn.Name, false);
            Assert.True(ReferenceEquals(before, after), "ilgisiz değişiklik sonucu değiştirmemeli (yeni dizi yok)");
        }

        [Test]
        public static void SilinenDosyaCikarilir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "rapor", FilterKind.All, SortColumn.Name, false);
            idx[0].Remove(45);
            var after = Update(before, 0, new[] { 45 }, "rapor", FilterKind.All, SortColumn.Name, false);
            SameAsFull(after, idx, "rapor", FilterKind.All, SortColumn.Name, false, "silme");
        }

        [Test]
        public static void EslesmeyenAdaGecenCikarilirEslesenAdaGecenEklenir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "rapor", FilterKind.All, SortColumn.Name, false);
            idx[0].Upsert(44, "x.docx", 32, 0x20);
            idx[0].Upsert(40, "rapor notları.txt", 31, 0x20);
            var after = Update(before, 0, new[] { 44, 40 }, "rapor", FilterKind.All, SortColumn.Name, false);
            SameAsFull(after, idx, "rapor", FilterKind.All, SortColumn.Name, false, "yeniden adlandırma");
        }

        [Test]
        public static void BoyutDegisinceSiralamadakiYeriDegisir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "rapor", FilterKind.All, SortColumn.Size, false);
            idx[0].SetInfo(46, 999999, 200, 0x20);
            var after = Update(before, 0, new[] { 46 }, "rapor", FilterKind.All, SortColumn.Size, false);
            SameAsFull(after, idx, "rapor", FilterKind.All, SortColumn.Size, false, "boyuta göre");
        }

        [Test]
        public static void TersSiralamadaDogruYereEklenir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "", FilterKind.Files, SortColumn.Name, true);
            idx[0].Upsert(47, "rapor 2025.pdf", 32, 0x20);
            idx[0].Upsert(48, "aaa.txt", 31, 0x20);
            var after = Update(before, 0, new[] { 47, 48 }, "", FilterKind.Files, SortColumn.Name, true);
            SameAsFull(after, idx, "", FilterKind.Files, SortColumn.Name, true, "ters sıralama");
        }

        [Test]
        public static void IkinciDiskteDegisiklik()
        {
            var idx = new[] { SearchFixture.BuildC(true), SearchFixture.BuildD() };
            var before = Full(idx, "not", FilterKind.All, SortColumn.Name, false);
            idx[1].Upsert(40, "notdefteri.txt", 5, 0x20);
            var after = Update(before, 1, new[] { 40 }, "not", FilterKind.All, SortColumn.Name, false);
            SameAsFull(after, idx, "not", FilterKind.All, SortColumn.Name, false, "D: diski");
            Assert.Equal(1, after.Items[1].Volume, "notdefteri.txt D: diskinde");
        }

        [Test]
        public static void YolaBagliSorgudaKlasorDegisikligiTamAramaIster()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, @"projeler\", FilterKind.All, SortColumn.Name, false);
            idx[0].Upsert(32, "Arsiv", 5, 0x10);
            Assert.True(Update(before, 0, new[] { 32 }, @"projeler\", FilterKind.All, SortColumn.Name, false) == null, "yol sorgusu: tam arama gerekir");
            Assert.True(Query.Parse(@"projeler\").NeedsPath, "yol terimi");
            Assert.True(!Query.Parse("rapor").NeedsPath, "isim terimi");
        }

        [Test]
        public static void BuyukSonuctaArtimliGuncellemeHizlidir()
        {
            var idx = new FileIndex('c', 1000100);
            idx.LoadRecord(30, true, 1, 0x10);
            idx.LoadName(30, "klasor", 5);
            for (int i = 0; i < 1000000; i++)
            {
                idx.LoadRecord(100 + i, false, i, 0x20);
                idx.LoadName(100 + i, "dosya" + (i * 7919 % 1000000).ToString("D7") + ".txt", 30);
            }
            idx.FinishLoad();
            idx.BuildSortedOrder();
            var indexes = new[] { idx };
            SearchResult current = Full(indexes, "", FilterKind.All, SortColumn.Name, false);
            var changed = new[] { 100, 200, 300, 400, 500 };
            idx.Remove(200);
            idx.Upsert(300, "aaa_yeni.txt", 30, 0x20);
            idx.Upsert(1000200, "zzz_yeni.txt", 30, 0x20);
            var all = new[] { 100, 200, 300, 400, 500, 1000200 };
            SearchEngine.Update(current, 0, all, Query.Parse(""), FilterKind.All, SortColumn.Name, false);   // ısınma (JIT)
            var sw = Stopwatch.StartNew();
            SearchResult updated = null;
            for (int r = 0; r < 5; r++) updated = SearchEngine.Update(current, 0, all, Query.Parse(""), FilterKind.All, SortColumn.Name, false);
            long perUpdate = sw.ElapsedMilliseconds / 5;
            SameAsFull(updated, indexes, "", FilterKind.All, SortColumn.Name, false, "1M sonuç");
            Assert.True(perUpdate < 25, "1 milyon sonuçta artımlı güncelleme 25 ms altında olmalı: " + perUpdate + " ms");
        }

        [Test]
        public static void IsimSorgusundaKlasorDegisikligiArtimliKalir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            var before = Full(idx, "", FilterKind.Folders, SortColumn.Name, false);
            idx[0].Upsert(32, "Arsiv", 5, 0x10);
            idx[0].Upsert(33, "Yeni Klasör", 31, 0x10);
            var after = Update(before, 0, new[] { 32, 33 }, "", FilterKind.Folders, SortColumn.Name, false);
            SameAsFull(after, idx, "", FilterKind.Folders, SortColumn.Name, false, "klasör adı değişimi");
        }
    }
}
