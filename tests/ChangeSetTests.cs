using System.Linq;
using Arama.Index;

namespace Arama.Tests
{
    public static class ChangeSetTests
    {
        [Test]
        public static void BirikenDegisikliklerTekrarsizAlinirVeSifirlanir()
        {
            var changes = new ChangeSet(100);
            changes.Add(new[] { 40, 41 });
            changes.Add(new[] { 41, 42 });
            int[] ids;
            Assert.True(changes.Take(out ids), "taşma yok");
            Assert.SequenceEqual(new[] { 40, 41, 42 }, ids.OrderBy(x => x).ToArray(), "tekrarsız birikti");
            Assert.True(changes.Take(out ids), "ikinci alış");
            Assert.Equal(0, ids.Length, "alındıktan sonra boş");
        }

        [Test]
        public static void SinirAsilincaTamAramaIstenir()
        {
            var changes = new ChangeSet(3);
            changes.Add(new[] { 1, 2, 3, 4 });
            int[] ids;
            Assert.True(!changes.Take(out ids), "sınır aşıldı: tam arama gerekir");
            Assert.Equal(0, ids.Length, "biriken liste tutulmaz (bellek)");
            changes.Add(new[] { 7 });
            Assert.True(changes.Take(out ids), "taşma bayrağı alınca sıfırlanır");
            Assert.SequenceEqual(new[] { 7 }, ids, "yeni değişiklik");
        }

        [Test]
        public static void IndeksYenileninceTamAramaIstenir()
        {
            var changes = new ChangeSet(100);
            changes.Add(new[] { 5 });
            changes.RequireFullSearch();
            int[] ids;
            Assert.True(!changes.Take(out ids), "indeks değişti: tam arama");
        }
    }
}
