using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;

namespace Arama.App
{
    /// <summary>
    /// Tarama ve aramaların bıraktığı büyük geçici dizileri toplar ve büyük nesne alanını sıkıştırarak
    /// belleği işletim sistemine geri verir. Boşta kalan programda .NET bunu kendiliğinden yapmaz.
    /// </summary>
    public static class MemoryTrim
    {
        // -1: kırpmadan sonra yerleşik bellek henüz ölçülmedi
        static long baseline = -1;

        public static void Now()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            Interlocked.Exchange(ref baseline, -1);
        }

        /// <summary>
        /// Süreç belleği yerleşik düzeyden eşikten fazla büyüdüyse kırpar. Yönetilen bellek yerine süreç belleğine
        /// bakılır: sıkıştırmasız toplamadan sonra parçalı kalan bellek ancak böyle görülür. Kırpmadan hemen sonra
        /// .NET çalışma payını geri aldığı için yerleşik düzey kırpmadan sonraki ilk kontrolde ölçülür; aksi halde
        /// bu doğal büyüme her kontrolde yeni (boşuna) bir kırpma tetikler.
        /// </summary>
        public static bool IfGrown(long thresholdBytes)
        {
            long current = PrivateBytes();
            long settled = Interlocked.Read(ref baseline);
            if (settled < 0)
            {
                Interlocked.Exchange(ref baseline, current);
                return false;
            }
            if (current - settled <= thresholdBytes) return false;
            Now();
            return true;
        }

        static long PrivateBytes()
        {
            using (var process = Process.GetCurrentProcess()) return process.PrivateMemorySize64;
        }
    }
}
