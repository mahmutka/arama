using System;
using Arama.Ntfs;

namespace Arama.Tests
{
    public static class DataRunsTests
    {
        [Test]
        public static void IkiParcaliRunVeNegatifOfsetCozulur()
        {
            // 0x31: uzunluk 1 bayt (0x20), ofset 3 bayt (0x001000 = 4096)
            // 0x21: uzunluk 1 bayt (0x10), ofset 2 bayt (0xFFF0 = -16) → 4080
            var bytes = new byte[] { 0x31, 0x20, 0x00, 0x10, 0x00, 0x21, 0x10, 0xF0, 0xFF, 0x00 };
            var runs = DataRuns.Decode(bytes, 0, bytes.Length);
            Assert.SequenceEqual(new[] { new Extent(4096, 32), new Extent(4080, 16) }, runs, "runlar");
        }

        [Test]
        public static void SeyrekRunLcnEksiBirOlurVeTabaniDegistirmez()
        {
            var bytes = new byte[] { 0x11, 0x08, 0x40, 0x01, 0x04, 0x11, 0x02, 0x10, 0x00 };
            var runs = DataRuns.Decode(bytes, 0, bytes.Length);
            Assert.SequenceEqual(new[] { new Extent(64, 8), new Extent(-1, 4), new Extent(80, 2) }, runs, "seyrek run");
        }

        [Test]
        public static void SinirdanTasanRunHataVerir()
        {
            var bytes = new byte[] { 0x31, 0x20, 0x00 };
            Assert.Throws<FormatException>(() => DataRuns.Decode(bytes, 0, bytes.Length), "taşan run");
        }

        [Test]
        public static void SifirBaslikBitisDemektir()
        {
            var bytes = new byte[] { 0x00, 0x31, 0x20 };
            Assert.Equal(0, DataRuns.Decode(bytes, 0, bytes.Length).Count, "boş liste");
        }
    }
}
