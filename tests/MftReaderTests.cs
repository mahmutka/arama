using System;
using System.Diagnostics;
using System.IO;
using Arama.Index;
using Arama.Ntfs;

namespace Arama.Tests
{
    public static class MftReaderTests
    {
        static FileIndex sharedC;

        /// <summary>Diğer yönetici testleri için C: indeksi (bir kez taranır).</summary>
        internal static FileIndex SharedIndexC()
        {
            if (sharedC == null)
                using (var volume = MftReader.OpenVolume('C')) sharedC = MftReader.Read(volume, 'C', null).Index;
            return sharedC;
        }

        [AdminTest]
        public static void TaramaBilinenDosyalariDogruOkur()
        {
            string root = TestFiles.CreateTree();
            try
            {
                MftReader.FlushVolume('C');
                var sw = Stopwatch.StartNew();
                MftScanResult result;
                using (var volume = MftReader.OpenVolume('C')) result = MftReader.Read(volume, 'C', null);
                TestRunner.Info("C: " + result.Index.LiveCount + " girdi, " + sw.ElapsedMilliseconds + " ms, bozuk kayit: " + result.CorruptRecords);
                sharedC = result.Index;
                CheckEntry(result.Index, root, 0, true);
                CheckEntry(result.Index, Path.Combine(root, "öğrenci notları.txt"), 1234, false);
                CheckEntry(result.Index, Path.Combine(root, "Alt"), 0, true);
                CheckEntry(result.Index, Path.Combine(root, "Alt", "İçerik.DOCX"), 0, false);
                CheckEntry(result.Index, Path.Combine(root, "Alt", "Derin", "büyük.bin"), 70000, false);
                Assert.True(result.Index.LiveCount > 10000, "C: üzerinde makul sayıda girdi olmalı");
            }
            finally { Directory.Delete(root, true); }
        }

        static void CheckEntry(FileIndex idx, string path, long expectedSize, bool isDirectory)
        {
            int id = TestFiles.RecordId(path);
            Assert.True(idx.IsLive(id), path + " indekste yok");
            string actual = idx.GetFullPath(id);
            Assert.True(string.Equals(path, actual, StringComparison.OrdinalIgnoreCase), "yol | beklenen: " + path + ", gelen: " + actual);
            Assert.Equal(isDirectory, idx.IsDirectory(id), path + " klasör bayrağı");
            if (isDirectory) return;
            Assert.Equal(expectedSize, idx.GetSize(id), path + " boyutu");
            Assert.Equal(File.GetLastWriteTimeUtc(path).ToFileTimeUtc(), idx.GetModified(id), path + " değiştirilme zamanı");
        }
    }
}
