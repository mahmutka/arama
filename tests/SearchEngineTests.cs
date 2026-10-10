using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Arama.Index;
using Arama.Search;

namespace Arama.Tests
{
    public static class SearchEngineTests
    {
        static SearchResult Run(FileIndex[] indexes, string query, FilterKind filter, SortColumn sort, bool descending)
        {
            return SearchEngine.Run(indexes, Query.Parse(query), filter, sort, descending, CancellationToken.None);
        }

        static List<string> Names(SearchResult result)
        {
            var names = new List<string>();
            foreach (var item in result.Items) names.Add(result.Indexes[item.Volume].GetName(item.Id));
            return names;
        }

        [Test]
        public static void DisklerAdaGoreBirlesir()
        {
            var r = Run(new[] { SearchFixture.BuildC(true), SearchFixture.BuildD() }, "not", FilterKind.All, SortColumn.Name, false);
            Assert.SequenceEqual(new[] { "Ali Notları.txt", "notlar.txt" }, Names(r), "iki diskten ada göre");
            Assert.Equal(1, r.Items[0].Volume, "ilk sonuç D:");
            Assert.Equal(12, r.TotalLive, "toplam nesne");
        }

        [Test]
        public static void AdaGoreTersSiralama()
        {
            var r = Run(new[] { SearchFixture.BuildC(true) }, "rapor", FilterKind.All, SortColumn.Name, true);
            Assert.SequenceEqual(new[] { "tmp_rapor.tmp", "rapor 2024.docx", "rapor 2023.pdf" }, Names(r), "ters");
        }

        [Test]
        public static void BoyutaGoreSiralama()
        {
            var c = new[] { SearchFixture.BuildC(true) };
            Assert.SequenceEqual(new[] { "tmp_rapor.tmp", "rapor 2024.docx", "rapor 2023.pdf" },
                Names(Run(c, "rapor", FilterKind.All, SortColumn.Size, false)), "artan");
            Assert.SequenceEqual(new[] { "rapor 2023.pdf", "rapor 2024.docx", "tmp_rapor.tmp" },
                Names(Run(c, "rapor", FilterKind.All, SortColumn.Size, true)), "azalan");
        }

        [Test]
        public static void TariheGoreSiralama()
        {
            var r = Run(new[] { SearchFixture.BuildC(true) }, "rapor", FilterKind.All, SortColumn.Modified, false);
            Assert.SequenceEqual(new[] { "tmp_rapor.tmp", "rapor 2023.pdf", "rapor 2024.docx" }, Names(r), "eskiden yeniye");
        }

        [Test]
        public static void YolaGoreSiralama()
        {
            var r = Run(new[] { SearchFixture.BuildC(true) }, "rapor|readme", FilterKind.All, SortColumn.Path, false);
            Assert.SequenceEqual(new[] { "rapor 2023.pdf", "rapor 2024.docx", "tmp_rapor.tmp", "readme.md" }, Names(r), @"C:\Projeler, sonra C:\Users");
        }

        [Test]
        public static void SiraliDiziYokkenSonucYineAdaGoreSirali()
        {
            var r = Run(new[] { SearchFixture.BuildC(false) }, "", FilterKind.All, SortColumn.Name, false);
            Assert.SequenceEqual(new[] { "ali", "IMG_0012.jpg", "notlar.txt", "Öğrenci Listesi.xlsx", "Projeler",
                "rapor 2023.pdf", "rapor 2024.docx", "readme.md", "tmp_rapor.tmp", "Users" }, Names(r), "sıralı");
        }

        [Test]
        public static void KuyruktakiGirdilerDogruYerdeVeTekrarsiz()
        {
            var c = SearchFixture.BuildC(true);
            c.Upsert(47, "aaa.txt", 32, 0x20);
            c.Upsert(44, "zzz.docx", 32, 0x20);
            var r = Run(new[] { c }, "", FilterKind.Files, SortColumn.Name, false);
            Assert.SequenceEqual(new[] { "aaa.txt", "IMG_0012.jpg", "notlar.txt", "Öğrenci Listesi.xlsx",
                "rapor 2023.pdf", "readme.md", "tmp_rapor.tmp", "zzz.docx" }, Names(r), "kuyruk birleşti");
        }

        [Test]
        public static void SilinenGirdiSonuctaYok()
        {
            var c = SearchFixture.BuildC(true);
            c.Remove(45);
            var r = Run(new[] { c }, "rapor", FilterKind.All, SortColumn.Name, false);
            Assert.SequenceEqual(new[] { "rapor 2024.docx", "tmp_rapor.tmp" }, Names(r), "silinen yok");
        }

        [Test]
        public static void IptalEdilenAramaHataFirlatir()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool canceled = false;
            try { SearchEngine.Run(new[] { SearchFixture.BuildC(true) }, Query.Parse(""), FilterKind.All, SortColumn.Name, false, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.True(canceled, "iptal edilmeli");
        }

        [Test]
        public static void GecersizRegexHatasiSonucaTasinir()
        {
            var r = Run(new[] { SearchFixture.BuildC(true) }, "regex:([", FilterKind.All, SortColumn.Name, false);
            Assert.True(r.Error != null, "hata mesajı");
            Assert.Equal(0, r.Items.Length, "sonuç yok");
        }

        [Test]
        public static void SecimKimlikleKorunur()
        {
            var c = SearchFixture.BuildC(true);
            var before = Run(new[] { c }, "rapor", FilterKind.All, SortColumn.Name, false);
            ResultRef selected = before.Items[1];                    // rapor 2024.docx
            c.Upsert(47, "rapor 2000.txt", 32, 0x20);                // öne yeni satır girer
            var after = Run(new[] { c }, "rapor", FilterKind.All, SortColumn.Name, false);
            List<int> positions = after.IndexesOf(new[] { selected });
            Assert.SequenceEqual(new[] { 2 }, positions, "seçili satır yeni konumunda bulunur");
            Assert.Equal("rapor 2024.docx", c.GetName(after.Items[positions[0]].Id), "aynı dosya");
        }

        [Test]
        public static void SecimKonumuBuyukSonuctaHizliBulunur()
        {
            var result = new SearchResult();
            result.Items = new ResultRef[2000000];
            for (int i = 0; i < result.Items.Length; i++) result.Items[i] = new ResultRef(i & 1, i);
            var selected = new[] { new ResultRef(1, 1999999) };
            result.IndexesOf(selected);   // ısınma
            var sw = Stopwatch.StartNew();
            List<int> positions = null;
            for (int r = 0; r < 10; r++) positions = result.IndexesOf(selected);
            double perCall = sw.Elapsed.TotalMilliseconds / 10;
            Assert.SequenceEqual(new[] { 1999999 }, positions, "konum");
            Assert.True(perCall < 8, "2 milyon sonuçta seçim konumu 8 ms altında bulunmalı: " + perCall.ToString("F1") + " ms");
        }

        [Test]
        public static void TamTaramaBellegiSiraylaOkur()
        {
            // İsimler kayıt sırasında eklenir, ada göre sıra ise bunun karışık bir permütasyonudur (gerçek disklerdeki gibi)
            var idx = new FileIndex('c', 1000100);
            idx.LoadRecord(30, true, 1, 0x10);
            idx.LoadName(30, "klasor", 5);
            for (int i = 0; i < 1000000; i++)
            {
                idx.LoadRecord(100 + i, false, i, 0x20);
                idx.LoadName(100 + i, "dosya" + (i * 7919 % 1000000).ToString("D7") + "_rapor.txt", 30);
            }
            idx.FinishLoad();
            idx.BuildSortedOrder();
            var indexes = new[] { idx };
            Query query = Query.Parse("bulunmaz");
            SearchEngine.Run(indexes, query, FilterKind.All, SortColumn.Name, false, CancellationToken.None);   // ısınma
            // Windows CPU süresini ~15,6 ms adımlarla sayar: kısa aramaları çok tekrarlayarak ölçülebilir kıl
            const int runs = 40;
            var process = Process.GetCurrentProcess();
            TimeSpan cpu0 = process.TotalProcessorTime;
            for (int r = 0; r < runs; r++) SearchEngine.Run(indexes, query, FilterKind.All, SortColumn.Name, false, CancellationToken.None);
            process.Refresh();
            double cpuPerSearch = (process.TotalProcessorTime - cpu0).TotalMilliseconds / runs;
            TestRunner.Info("1M girdi, eşleşmeyen sorgu: arama başına CPU " + cpuPerSearch.ToString("F0") + " ms");
            // İsimler sıralı düzende art arda okununca ~30-40 ms, rastgele erişimde ~130 ms; eşik ikisinin arasında (ölçüm gürültüsüne pay)
            Assert.True(cpuPerSearch < 70, "1 milyon isim taraması 70 ms CPU altında olmalı: " + cpuPerSearch.ToString("F0") + " ms");
            var r2 = SearchEngine.Run(indexes, Query.Parse("0000001_rapor"), FilterKind.All, SortColumn.Name, false, CancellationToken.None);
            Assert.Equal(1, r2.Items.Length, "doğru sonuç");
        }

        [AdminTest]
        public static void GercekDiskteAramaSuresi()
        {
            FileIndex c = MftReaderTests.SharedIndexC();
            if (!c.HasSortedOrder) c.BuildSortedOrder();
            var indexes = new[] { c };
            Run(indexes, "a", FilterKind.All, SortColumn.Name, false);   // ısınma (JIT)
            foreach (string q in new[] { "rapor", "a", "*.dll", "", @"regex:^IMG_\d+", @"windows\system32\ .dll" })
            {
                var sw = Stopwatch.StartNew();
                var r = Run(indexes, q, FilterKind.All, SortColumn.Name, false);
                TestRunner.Info("[" + q + "] " + r.Items.Length + " sonuc, " + sw.ElapsedMilliseconds + " ms (" + c.LiveCount + " girdi)");
            }
            var timer = Stopwatch.StartNew();
            Run(indexes, "rapor", FilterKind.All, SortColumn.Name, false);
            Assert.True(timer.ElapsedMilliseconds < 100, "alt dizi araması 100 ms altında olmalı: " + timer.ElapsedMilliseconds + " ms");
        }
    }
}
