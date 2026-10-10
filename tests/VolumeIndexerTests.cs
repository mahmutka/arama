using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Arama.Index;

namespace Arama.Tests
{
    public static class VolumeIndexerTests
    {
        [AdminTest]
        public static void TaramaOncesiVeSonrasiDegisikliklerIndekseYansir()
        {
            string root = TestFiles.NewRoot();
            string early = Path.Combine(root, "erken.txt");
            File.WriteAllText(early, "önce");   // taramadan hemen önce: diske henüz yazılmamış olabilir
            try
            {
                using (var indexer = new VolumeIndexer('C'))
                {
                    var ready = new ManualResetEvent(false);
                    indexer.IndexChanged += () => ready.Set();
                    var sw = Stopwatch.StartNew();
                    indexer.Start();
                    while (!ready.WaitOne(500))
                    {
                        Assert.True(!indexer.Status.StartsWith("hata"), "indeksleme hatası: " + indexer.Status);
                        Assert.True(sw.ElapsedMilliseconds < 300000, "indeksleme 5 dk içinde bitmedi: " + indexer.Status);
                    }
                    TestRunner.Info("C: indeksleme " + sw.ElapsedMilliseconds + " ms, durum: " + indexer.Status);
                    Assert.Equal("hazır", indexer.Status, "canlı takip başladı");
                    FileIndex idx = indexer.Index;

                    int earlyId = TestFiles.RecordId(early);
                    Assert.True(Read(idx, () => idx.IsLive(earlyId)), "taramadan hemen önce oluşan dosya indekste");

                    string a = Path.Combine(root, "canlı.txt");
                    File.WriteAllText(a, "merhaba");
                    int id = TestFiles.RecordId(a);
                    WaitUntil(idx, () => idx.IsLive(id) && SamePath(idx.GetFullPath(id), a), "oluşturma");
                    WaitUntil(idx, () => idx.GetSize(id) == 7, "ilk boyut");
                    File.AppendAllText(a, new string('x', 100));
                    WaitUntil(idx, () => idx.GetSize(id) == 107, "yazma sonrası boyut");
                    string b = Path.Combine(root, "yeni ad.txt");
                    File.Move(a, b);
                    WaitUntil(idx, () => SamePath(idx.GetFullPath(id), b), "yeniden adlandırma");
                    Directory.CreateDirectory(Path.Combine(root, "Alt"));
                    string c = Path.Combine(root, "Alt", "taşındı.txt");
                    File.Move(b, c);
                    WaitUntil(idx, () => SamePath(idx.GetFullPath(id), c), "taşıma");
                    File.Delete(c);
                    WaitUntil(idx, () => !idx.IsLive(id), "silme");
                }
            }
            finally { Directory.Delete(root, true); }
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static bool Read(FileIndex idx, Func<bool> condition)
        {
            idx.Lock.EnterReadLock();
            try { return condition(); }
            finally { idx.Lock.ExitReadLock(); }
        }

        static void WaitUntil(FileIndex idx, Func<bool> condition, string what)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                if (Read(idx, condition))
                {
                    TestRunner.Info(what + ": " + sw.ElapsedMilliseconds + " ms");
                    return;
                }
                Thread.Sleep(20);
            }
            throw new AssertException(what + " 2 sn içinde indekse yansımadı");
        }
    }
}
