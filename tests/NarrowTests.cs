using System;
using System.Collections.Generic;
using System.Threading;
using Arama.Index;
using Arama.Search;

namespace Arama.Tests
{
    /// <summary>Yazarken daraltma: yeni sorgu öncekinin uzantısıysa yalnızca önceki sonuçlar taranır.</summary>
    public static class NarrowTests
    {
        static List<string> Names(SearchResult result)
        {
            var names = new List<string>();
            foreach (var item in result.Items) names.Add(result.Indexes[item.Volume].GetName(item.Id));
            return names;
        }

        static SearchResult Full(FileIndex[] indexes, string query, FilterKind filter, SortColumn sort, bool descending)
        {
            return SearchEngine.Run(indexes, Query.Parse(query), filter, sort, descending, CancellationToken.None);
        }

        [Test]
        public static void YalnizcaDuzMetinUzantisiDaraltilabilir()
        {
            Assert.True(Query.CanNarrow("ra", "rap"), "harf eklendi");
            Assert.True(Query.CanNarrow("ra", "ra p"), "yeni terim eklendi");
            Assert.True(Query.CanNarrow("Rapor", "Rapor 2"), "büyük harf");
            Assert.True(!Query.CanNarrow("rap", "ra"), "silme");
            Assert.True(!Query.CanNarrow("ra", "rb"), "değişti");
            Assert.True(!Query.CanNarrow("", "r"), "boş önceki sorgu");
            Assert.True(!Query.CanNarrow(null, "r"), "önceki yok");
            foreach (string special in new[] { "ra|x", "!ra", "ra\"", "*.p", "ra?", @"ra\", "regex:ra" })
                Assert.True(!Query.CanNarrow("ra", special), "özel karakter: " + special);
            Assert.True(!Query.CanNarrow("!r", "!ra"), "DEĞİL içeren önceki sorgu");
        }

        [Test]
        public static void DaraltmaTamAramaylaAyniSonucuVerir()
        {
            var idx = new[] { SearchFixture.BuildC(true), SearchFixture.BuildD() };
            foreach (SortColumn sort in new[] { SortColumn.Name, SortColumn.Size, SortColumn.Path })
            {
                SearchResult previous = Full(idx, "r", FilterKind.All, sort, false);
                SearchResult narrowed = SearchEngine.Narrow(previous, Query.Parse("rap"), FilterKind.All, CancellationToken.None);
                SearchResult full = Full(idx, "rap", FilterKind.All, sort, false);
                Assert.SequenceEqual(Names(full), Names(narrowed), "sıralama " + sort);
                Assert.Equal(full.TotalLive, narrowed.TotalLive, "toplam nesne");
            }
            SearchResult prevDesc = Full(idx, "a", FilterKind.Files, SortColumn.Name, true);
            SearchResult narrowedDesc = SearchEngine.Narrow(prevDesc, Query.Parse("a t"), FilterKind.Files, CancellationToken.None);
            Assert.SequenceEqual(Names(Full(idx, "a t", FilterKind.Files, SortColumn.Name, true)), Names(narrowedDesc), "ters sıra + filtre");
        }

        [Test]
        public static void DaraltmaSilinenGirdiyiAtlar()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            SearchResult previous = Full(idx, "rapor", FilterKind.All, SortColumn.Name, false);
            idx[0].Remove(45);
            SearchResult narrowed = SearchEngine.Narrow(previous, Query.Parse("rapor 20"), FilterKind.All, CancellationToken.None);
            Assert.SequenceEqual(new[] { "rapor 2024.docx" }, Names(narrowed), "silinen rapor 2023 yok");
        }

        [Test]
        public static void IptalEdilenDaraltmaHataFirlatir()
        {
            var idx = new[] { SearchFixture.BuildC(true) };
            SearchResult previous = Full(idx, "r", FilterKind.All, SortColumn.Name, false);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool canceled = false;
            try { SearchEngine.Narrow(previous, Query.Parse("ra"), FilterKind.All, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.True(canceled, "iptal edilmeli");
        }
    }
}
