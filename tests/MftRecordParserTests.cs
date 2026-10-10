using System;
using Arama.Ntfs;

namespace Arama.Tests
{
    public static class MftRecordParserTests
    {
        static MftRecord ParseBytes(byte[] bytes, bool wantRuns)
        {
            Assert.True(MftRecordParser.HasFileSignature(bytes, 0), "imza");
            Assert.True(MftRecordParser.ApplyFixup(bytes, 0, 1024), "fixup başarısız");
            var rec = new MftRecord();
            Assert.True(MftRecordParser.Parse(bytes, 0, 1024, wantRuns, rec), "parse başarısız");
            return rec;
        }

        [Test]
        public static void DosyaKaydiOkunurVeWin32IsimTercihEdilir()
        {
            var bytes = new MftRecordBuilder(true, false, 0)
                .StandardInfo(132000000000000000L, 0x22)
                .FileName(5, "RAPOR~1.TXT", 2)
                .FileName(5, "rapor 2024.txt", 1)
                .ResidentData(123, 0)
                .Build();
            var rec = ParseBytes(bytes, false);
            Assert.True(rec.InUse, "kullanımda");
            Assert.True(!rec.IsDirectory, "dosya");
            Assert.Equal("rapor 2024.txt", rec.Name, "Win32 isim DOS isme tercih edilir");
            Assert.Equal(3, rec.NameRank, "Win32 sırası");
            Assert.Equal(5L, rec.ParentRecord, "üst klasör (sequence maskelenir)");
            Assert.Equal(132000000000000000L, rec.ModifiedTime, "tarih");
            Assert.Equal(0x22u, rec.Attributes, "öznitelik");
            Assert.True(rec.HasSize, "boyut var");
            Assert.Equal(123L, rec.Size, "resident boyut");
            Assert.Equal(0L, rec.BaseRecord, "ana kayıt");
        }

        [Test]
        public static void KlasorKaydiBoyutsuzdur()
        {
            var bytes = new MftRecordBuilder(true, true, 0)
                .StandardInfo(1, 0x10000000)
                .FileName(5, "Belgeler", 3)
                .Build();
            var rec = ParseBytes(bytes, false);
            Assert.True(rec.IsDirectory, "klasör");
            Assert.Equal("Belgeler", rec.Name, "isim");
            Assert.True(!rec.HasSize, "klasörde $DATA yok");
        }

        [Test]
        public static void KullanilmayanKayittaIsimOkunmaz()
        {
            var bytes = new MftRecordBuilder(false, false, 0).FileName(5, "silinmis.txt", 1).Build();
            var rec = ParseBytes(bytes, false);
            Assert.True(!rec.InUse, "kullanımda değil");
            Assert.True(rec.Name == null, "isim okunmaz");
        }

        [Test]
        public static void NonResidentBoyutVeRunlarOkunur()
        {
            var bytes = new MftRecordBuilder(true, false, 0)
                .FileName(5, "$MFT", 3)
                .NonResidentData(0, 5000000, new byte[] { 0x31, 0x20, 0x00, 0x10, 0x00, 0x00 })
                .Build();
            var rec = ParseBytes(bytes, true);
            Assert.Equal(5000000L, rec.Size, "gerçek boyut");
            Assert.SequenceEqual(new[] { new Extent(4096, 32) }, rec.DataRuns, "runlar");
        }

        [Test]
        public static void SifirdanBaslamayanVcnBoyutVermez()
        {
            var bytes = new MftRecordBuilder(true, false, 0)
                .NonResidentData(100, 999, new byte[] { 0x11, 0x01, 0x01, 0x00 })
                .Build();
            var rec = ParseBytes(bytes, false);
            Assert.True(!rec.HasSize, "devam parçası boyut taşımaz");
        }

        [Test]
        public static void AdliAkisBoyutaSayilmaz()
        {
            var bytes = new MftRecordBuilder(true, false, 0)
                .FileName(5, "a.txt", 1)
                .ResidentData(50, 4)
                .ResidentData(7, 0)
                .Build();
            var rec = ParseBytes(bytes, false);
            Assert.Equal(7L, rec.Size, "yalnızca isimsiz akış");
        }

        [Test]
        public static void UzantiKaydiAnaKaydiGosterir()
        {
            var bytes = new MftRecordBuilder(true, false, 1234 | (3L << 48)).FileName(77, "uzanti.txt", 1).Build();
            var rec = ParseBytes(bytes, false);
            Assert.Equal(1234L, rec.BaseRecord, "ana kayıt no (sequence maskelenir)");
            Assert.True(!rec.HasStandardInfo, "uzantıda $STANDARD_INFORMATION yok");
            Assert.Equal("uzanti.txt", rec.Name, "isim");
        }

        [Test]
        public static void FixupSektorSinirindakiBaytlariGeriYukler()
        {
            string longName = new string('a', 120) + "ğ" + new string('b', 100);
            var bytes = new MftRecordBuilder(true, false, 0).StandardInfo(1, 0).FileName(5, longName, 1).Build();
            var rec = ParseBytes(bytes, false);
            Assert.Equal(longName, rec.Name, "510. bayttan geçen isim");
        }

        [Test]
        public static void BozukFixupReddedilir()
        {
            var bytes = new MftRecordBuilder(true, false, 0).FileName(5, "a.txt", 1).Build();
            bytes[510] ^= 0xFF;
            Assert.True(!MftRecordParser.ApplyFixup(bytes, 0, 1024), "fixup uyuşmazlığı");
        }

        [Test]
        public static void ImzasizKayitReddedilir()
        {
            var bytes = new MftRecordBuilder(true, false, 0).Build();
            bytes[0] = (byte)'B';
            Assert.True(!MftRecordParser.HasFileSignature(bytes, 0), "imza yok");
            Assert.True(!MftRecordParser.ApplyFixup(bytes, 0, 1024), "fixup reddeder");
        }

        [Test]
        public static void TasanOznitelikUzunluguReddedilir()
        {
            var bytes = new MftRecordBuilder(true, false, 0).FileName(5, "a.txt", 1).Build();
            Assert.True(MftRecordParser.ApplyFixup(bytes, 0, 1024), "fixup");
            BitConverter.GetBytes(5000u).CopyTo(bytes, 0x38 + 4);
            Assert.True(!MftRecordParser.Parse(bytes, 0, 1024, false, new MftRecord()), "taşan uzunluk");
        }
    }
}
