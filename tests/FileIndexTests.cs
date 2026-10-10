using Arama.Index;

namespace Arama.Tests
{
    public static class FileIndexTests
    {
        // C:\ (5)
        //   Users (30)
        //     ali (31)
        //       notlar.txt (40, 1234 bayt)
        //     readme.md (41, 10 bayt)
        internal static FileIndex Sample()
        {
            var idx = new FileIndex('c', 16);
            idx.LoadRecord(30, true, 1, 0x10); idx.LoadName(30, "Users", 5);
            idx.LoadRecord(31, true, 2, 0x10); idx.LoadName(31, "ali", 30);
            idx.LoadRecord(40, false, 100, 0x20); idx.LoadName(40, "notlar.txt", 31); idx.LoadSize(40, 1234);
            idx.LoadRecord(41, false, 200, 0x22); idx.LoadName(41, "readme.md", 30); idx.LoadSize(41, 10);
            idx.FinishLoad();
            return idx;
        }

        [Test]
        public static void YuklenenGirdilerVeYollar()
        {
            var idx = Sample();
            Assert.Equal(4, idx.LiveCount, "canlı girdi sayısı");
            Assert.Equal(@"C:\Users\ali\notlar.txt", idx.GetFullPath(40), "tam yol");
            Assert.Equal(@"C:\Users\ali", idx.GetDirectoryPath(40), "klasör yolu");
            Assert.Equal(@"C:\", idx.GetDirectoryPath(30), "kökteki girdinin klasörü");
            Assert.Equal(1234L, idx.GetSize(40), "boyut");
            Assert.Equal(100L, idx.GetModified(40), "tarih");
            Assert.True(idx.IsDirectory(31), "ali klasör");
            Assert.True(!idx.IsDirectory(40), "notlar dosya");
            Assert.True(!idx.IsLive(5), "kök klasör listelenmez");
        }

        [Test]
        public static void KapasiteGerektigindeBuyur()
        {
            var idx = new FileIndex('d', 1);
            Assert.True(idx.Upsert(1000, "x", 5, 0), "eklendi");
            Assert.True(idx.Capacity > 1000, "kapasite büyüdü");
            Assert.Equal(@"D:\x", idx.GetFullPath(1000), "yol");
        }

        [Test]
        public static void MetaDosyalarExtendVeKokeBaglanmayanlarElenir()
        {
            var idx = new FileIndex('c', 16);
            idx.LoadRecord(3, false, 0, 0); idx.LoadName(3, "$Volume", 5);
            idx.LoadRecord(11, true, 0, 0); idx.LoadName(11, "$Extend", 5);
            idx.LoadRecord(50, false, 0, 0); idx.LoadName(50, "$UsnJrnl", 11);
            idx.LoadRecord(60, true, 0, 0); idx.LoadName(60, "$RmMetadata", 11);
            idx.LoadRecord(61, false, 0, 0); idx.LoadName(61, "$TxfLog", 60);
            idx.LoadRecord(70, false, 0, 0); idx.LoadName(70, "normal.txt", 5);
            idx.LoadRecord(80, false, 0, 0); idx.LoadName(80, "yetim.txt", 999);
            idx.LoadRecord(90, true, 0, 0); idx.LoadName(90, "dongu1", 91);
            idx.LoadRecord(91, true, 0, 0); idx.LoadName(91, "dongu2", 90);
            idx.LoadRecord(95, false, 0, 0);
            idx.FinishLoad();
            Assert.Equal(1, idx.LiveCount, "yalnızca normal.txt kalır");
            Assert.True(idx.IsLive(70), "normal.txt canlı");
            foreach (int id in new[] { 3, 11, 50, 60, 61, 80, 90, 91, 95 })
                Assert.True(!idx.IsLive(id), id + " elenmeli");
        }

        [Test]
        public static void EklemeTasimaSilme()
        {
            var idx = Sample();
            Assert.True(idx.Upsert(42, "yeni.txt", 31, 0x20), "yeni girdi eklendi");
            Assert.Equal(5, idx.LiveCount, "sayı arttı");
            Assert.Equal(@"C:\Users\ali\yeni.txt", idx.GetFullPath(42), "yeni yol");
            Assert.True(!idx.Upsert(42, "yeni.txt", 31, 0x20), "aynı olay tekrar gelince yeni sayılmaz");
            idx.Upsert(42, "taşındı.txt", 30, 0x20);
            Assert.Equal(@"C:\Users\taşındı.txt", idx.GetFullPath(42), "taşıma + yeniden adlandırma");
            idx.Remove(42);
            Assert.True(!idx.IsLive(42), "silindi");
            Assert.Equal(4, idx.LiveCount, "sayı azaldı");
            idx.Remove(42);
            Assert.Equal(4, idx.LiveCount, "ikinci silme sayıyı değiştirmez");
        }

        [Test]
        public static void KlasorDegisinceYolOnbellegiYenilenir()
        {
            var idx = Sample();
            Assert.Equal(@"C:\Users\ali\notlar.txt", idx.GetFullPath(40), "önbelleğe alındı");
            idx.Upsert(31, "veli", 30, 0x10);
            Assert.Equal(@"C:\Users\veli\notlar.txt", idx.GetFullPath(40), "klasör yeniden adlandırıldı");
            idx.Upsert(31, "veli", 5, 0x10);
            Assert.Equal(@"C:\veli\notlar.txt", idx.GetFullPath(40), "klasör taşındı");
        }

        [Test]
        public static void BoyutVeTarihGuncellenir()
        {
            var idx = Sample();
            idx.SetInfo(40, 999, 555, 0x20);
            Assert.Equal(999L, idx.GetSize(40), "boyut");
            Assert.Equal(555L, idx.GetModified(40), "tarih");
            idx.SetInfo(31, 4096, 777, 0x10);
            Assert.Equal(0L, idx.GetSize(31), "klasör boyutu 0 kalır");
            idx.SetInfo(77, 1, 1, 0);
            Assert.True(!idx.IsLive(77), "olmayan girdi eklenmez");
        }

        [Test]
        public static void SiraliDiziVeKuyruk()
        {
            var idx = Sample();
            Assert.True(idx.BuildSortedOrder(), "sıralandı");
            Assert.SequenceEqual(new[] { 31, 40, 41, 30 }, idx.SortedByName, "ali, notlar.txt, readme.md, Users");
            Assert.Equal(0, idx.TailCount, "kuyruk boş");
            idx.Upsert(42, "Ağaç.txt", 31, 0x20);
            idx.Upsert(40, "zz.txt", 31, 0x20);
            idx.Upsert(40, "zzz.txt", 31, 0x20);
            Assert.Equal(2, idx.TailCount, "yeni ve yeniden adlandırılan kuyrukta, tekrarsız");
            Assert.True((idx.flags[40] & FileIndex.FlagInTail) != 0, "40 kuyruk işaretli");
            idx.SetInfo(41, 5, 5, 0x20);
            Assert.Equal(2, idx.TailCount, "boyut değişimi kuyruğa girmez");
            Assert.True(idx.BuildSortedOrder(), "yeniden sıralandı");
            Assert.SequenceEqual(new[] { 42, 31, 41, 30, 40 }, idx.SortedByName, "agac, ali, readme, users, zzz");
            Assert.Equal(0, idx.TailCount, "kuyruk boşaldı");
            Assert.True((idx.flags[40] & FileIndex.FlagInTail) == 0, "işaret temizlendi");
        }

        [Test]
        public static void TaramaSonrasiIsimTamponuKirpilir()
        {
            var idx = new FileIndex('c', 64);   // başlangıç tamponu 65.536 karakter
            for (int i = 0; i < 10; i++)
            {
                idx.LoadRecord(30 + i, false, 0, 0x20);
                idx.LoadName(30 + i, "dosya" + i + ".txt", 5);
            }
            idx.FinishLoad();
            Assert.True(idx.names.Length < 1024, "tampon kullanılan boyuta kırpılmalı: " + idx.names.Length);
            Assert.Equal("dosya3.txt", idx.GetName(33), "isimler korunur");
            idx.Upsert(50, new string('y', 2000), 5, 0x20);
            Assert.Equal(new string('y', 2000), idx.GetName(50), "kırpmadan sonra ekleme tamponu büyütür");
            Assert.Equal("dosya9.txt", idx.GetName(39), "büyüme sonrası eski isimler korunur");
        }

        [Test]
        public static void KlasorYoluOnbellegiSinirliKalir()
        {
            var idx = new FileIndex('c', 16);
            int dirs = FileIndex.MaxCachedDirectories + 100;
            int firstFile = 30 + dirs;
            for (int i = 0; i < dirs; i++)
            {
                idx.Upsert(30 + i, "k" + i, 5, 0x10);
                idx.Upsert(firstFile + i, "d.txt", 30 + i, 0x20);
            }
            for (int i = 0; i < dirs; i++) idx.GetFullPath(firstFile + i);
            Assert.True(idx.DirCacheCount <= FileIndex.MaxCachedDirectories, "önbellek sınırı aşıldı: " + idx.DirCacheCount);
            Assert.Equal(@"C:\k" + (dirs - 1) + @"\d.txt", idx.GetFullPath(firstFile + dirs - 1), "yol doğru kalır");
            Assert.Equal(@"C:\k0\d.txt", idx.GetFullPath(firstFile), "temizlenmiş önbellekten sonra da doğru");
        }

        [Test]
        public static void IsimKarsilastirmaTurkceKatlamaKullanir()
        {
            var idx = new FileIndex('c', 16);
            idx.Upsert(30, "Çanta", 5, 0);
            idx.Upsert(31, "cuzdan", 5, 0);
            idx.Upsert(32, "A.txt", 5, 0);
            idx.Upsert(33, "a.txt", 5, 0);
            Assert.True(FileIndex.CompareNames(idx, 30, idx, 31) < 0, "Çanta < cuzdan");
            Assert.True(FileIndex.CompareNames(idx, 32, idx, 33) < 0, "katlanınca eşit: A.txt < a.txt");
            Assert.Equal(0, FileIndex.CompareNames(idx, 32, idx, 32), "kendisiyle eşit");
        }
    }
}
