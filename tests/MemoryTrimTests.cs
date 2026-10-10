using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arama.App;

namespace Arama.Tests
{
    public static class MemoryTrimTests
    {
        [Test]
        public static void KirpmaBuyukGeciciDizilerinBelleginiGeriVerir()
        {
            var process = Process.GetCurrentProcess();
            MemoryTrim.Now();
            process.Refresh();
            long before = process.PrivateMemorySize64;

            var garbage = new List<byte[]>();
            for (int i = 0; i < 40; i++)
            {
                var big = new byte[5 << 20];
                big[0] = 1;
                garbage.Add(big);
                garbage.Add(new byte[100000]);   // büyük nesne alanını parçalar
            }
            garbage = null;

            MemoryTrim.Now();
            process.Refresh();
            long grownMb = (process.PrivateMemorySize64 - before) >> 20;
            Assert.True(grownMb < 50, "200 MB geçici dizi sonrası bellek geri verilmeli; kalan artış: " + grownMb + " MB");
        }

        [Test]
        public static void KirpmaSonrasiDogalBuyumeYenidenKirptirmaz()
        {
            MemoryTrim.Now();
            // Kırpmadan sonra .NET çalışma payını geri alır; canlı (çöp olmayan) 80 MB'lık büyüme bunu taklit eder
            var live = new List<byte[]>();
            for (int i = 0; i < 16; i++)
            {
                var block = new byte[5 << 20];
                for (int j = 0; j < block.Length; j += 4096) block[j] = 1;
                live.Add(block);
            }
            Assert.True(!MemoryTrim.IfGrown(64L << 20), "kırpma sonrası ilk kontrol yalnızca yerleşik belleği ölçer");
            Assert.True(!MemoryTrim.IfGrown(64L << 20), "büyüme yokken tekrar tekrar kırpmaz");
            GC.KeepAlive(live);
        }

        [Test]
        public static void ParcalanmisBellekteDeKirpar()
        {
            MemoryTrim.Now();
            MemoryTrim.IfGrown(64L << 20);       // yerleşik belleği ölç
            var keep = new List<byte[]>();
            var garbage = new List<byte[]>();
            for (int i = 0; i < 30; i++)
            {
                garbage.Add(new byte[4 << 20]);
                keep.Add(new byte[90000]);       // canlı küçük büyük-nesneler alanı parçalar
            }
            garbage = null;
            GC.Collect();                        // sıkıştırmasız: yönetilen bellek düşer, süreç belleği parçalı kalabilir
            bool trimmed = MemoryTrim.IfGrown(64L << 20);
            GC.KeepAlive(keep);
            Assert.True(trimmed, "süreç belleği büyüdüğü halde kırpılmadı");
        }

        [Test]
        public static void YalnizcaBellekEsigiAsincaKirpar()
        {
            MemoryTrim.Now();
            Assert.True(!MemoryTrim.IfGrown(64L << 20), "büyüme yokken kırpmaz (yerleşik bellek ölçülür)");
            var garbage = new List<byte[]>();
            for (int i = 0; i < 20; i++) garbage.Add(new byte[5 << 20]);
            garbage = null;
            Assert.True(MemoryTrim.IfGrown(64L << 20), "100 MB büyüyünce kırpar");
            Assert.True(!MemoryTrim.IfGrown(64L << 20), "kırpmadan hemen sonra yeniden kırpmaz");
        }
    }
}
