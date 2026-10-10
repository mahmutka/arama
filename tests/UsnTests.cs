using System;
using System.Collections.Generic;
using System.Text;
using Arama.Index;
using Arama.Usn;

namespace Arama.Tests
{
    public static class UsnTests
    {
        static byte[] Record(long fileRef, long parentRef, uint reason, uint attributes, string name, int majorVersion)
        {
            int nameBytes = name.Length * 2;
            int length = (60 + nameBytes + 7) & ~7;
            var r = new byte[length];
            BitConverter.GetBytes(length).CopyTo(r, 0);
            BitConverter.GetBytes((ushort)majorVersion).CopyTo(r, 4);
            BitConverter.GetBytes(fileRef).CopyTo(r, 8);
            BitConverter.GetBytes(parentRef).CopyTo(r, 16);
            BitConverter.GetBytes(12345L).CopyTo(r, 24);
            BitConverter.GetBytes(reason).CopyTo(r, 40);
            BitConverter.GetBytes(attributes).CopyTo(r, 52);
            BitConverter.GetBytes((ushort)nameBytes).CopyTo(r, 56);
            BitConverter.GetBytes((ushort)60).CopyTo(r, 58);
            Encoding.Unicode.GetBytes(name).CopyTo(r, 60);
            return r;
        }

        static byte[] Output(long nextUsn, params byte[][] records)
        {
            var all = new List<byte>(BitConverter.GetBytes(nextUsn));
            foreach (var r in records) all.AddRange(r);
            return all.ToArray();
        }

        static UsnEvent Ev(long fileRef, int parent, uint reason, uint attributes, string name)
        {
            var e = new UsnEvent();
            e.FileReference = fileRef;
            e.Id = (int)(fileRef & 0x0000FFFFFFFFFFFF);
            e.ParentId = parent;
            e.Reason = reason;
            e.Attributes = attributes;
            e.Name = name;
            return e;
        }

        [Test]
        public static void KayitlarAyristirilir()
        {
            long seq = 7L << 48;
            var buf = Output(999,
                Record(seq | 100, 5, UsnRecordParser.FileCreate, 0x20, "yeni.txt", 2),
                Record(101, seq | 30, UsnRecordParser.RenameNewName | UsnRecordParser.Close, 0x10, "Klasör", 2));
            var events = new List<UsnEvent>();
            Assert.Equal(999L, UsnRecordParser.Parse(buf, buf.Length, events), "sonraki USN");
            Assert.Equal(2, events.Count, "iki olay");
            Assert.Equal(100, events[0].Id, "kayıt no sequence'tan ayrılır");
            Assert.Equal(seq | 100, events[0].FileReference, "tam referans korunur");
            Assert.Equal(5, events[0].ParentId, "üst");
            Assert.Equal("yeni.txt", events[0].Name, "isim");
            Assert.Equal(0x20u, events[0].Attributes, "öznitelik");
            Assert.Equal(30, events[1].ParentId, "üst sequence'tan ayrılır");
            Assert.Equal("Klasör", events[1].Name, "Türkçe isim");
            Assert.Equal(UsnRecordParser.RenameNewName | UsnRecordParser.Close, events[1].Reason, "neden");
        }

        [Test]
        public static void DesteklenmeyenSurumAtlanir()
        {
            var buf = Output(5,
                Record(100, 5, UsnRecordParser.FileCreate, 0, "v3.txt", 3),
                Record(101, 5, UsnRecordParser.FileCreate, 0, "v2.txt", 2));
            var events = new List<UsnEvent>();
            UsnRecordParser.Parse(buf, buf.Length, events);
            Assert.Equal(1, events.Count, "yalnızca V2");
            Assert.Equal("v2.txt", events[0].Name, "V2 kaydı");
        }

        [Test]
        public static void BosCiktiYalnizcaSonrakiUsnDoner()
        {
            var buf = Output(42);
            var events = new List<UsnEvent>();
            Assert.Equal(42L, UsnRecordParser.Parse(buf, buf.Length, events), "sonraki USN");
            Assert.Equal(0, events.Count, "olay yok");
        }

        [Test]
        public static void NedenlerSiniflandirilir()
        {
            Assert.Equal(UsnAction.Upsert, UsnRecordParser.Classify(UsnRecordParser.FileCreate), "oluşturma");
            Assert.Equal(UsnAction.Remove, UsnRecordParser.Classify(UsnRecordParser.FileCreate | UsnRecordParser.FileDelete | UsnRecordParser.Close), "geçici dosya");
            Assert.Equal(UsnAction.Ignore, UsnRecordParser.Classify(UsnRecordParser.RenameOldName), "eski isim");
            Assert.Equal(UsnAction.Upsert, UsnRecordParser.Classify(UsnRecordParser.RenameNewName), "yeni isim");
            Assert.Equal(UsnAction.Upsert, UsnRecordParser.Classify(UsnRecordParser.DataExtend | UsnRecordParser.Close), "yazma");
        }

        [Test]
        public static void OlaylarIndekseUygulanir()
        {
            FileIndex idx = FileIndexTests.Sample();
            long fullRef50 = (3L << 48) | 50;
            var events = new List<UsnEvent>
            {
                Ev(fullRef50, 31, UsnRecordParser.FileCreate, 0x20, "yeni.txt"),
                Ev(40, 31, UsnRecordParser.RenameOldName, 0x20, "notlar.txt"),
                Ev(40, 30, UsnRecordParser.RenameNewName, 0x20, "x.txt"),
                Ev(40, 30, UsnRecordParser.DataExtend | UsnRecordParser.Close, 0x20, "x.txt"),
                Ev(41, 30, UsnRecordParser.FileDelete | UsnRecordParser.Close, 0x20, "readme.md"),
                Ev(10, 5, UsnRecordParser.FileCreate, 0, "$Meta"),
                Ev(60, 999, UsnRecordParser.FileCreate, 0x20, "yetim.txt"),
            };
            Dictionary<int, long> refresh = UsnApplier.Apply(idx, events);
            Assert.Equal(@"C:\Users\ali\yeni.txt", idx.GetFullPath(50), "oluşturulan");
            Assert.Equal(@"C:\Users\x.txt", idx.GetFullPath(40), "taşınan");
            Assert.True(!idx.IsLive(41), "silinen");
            Assert.True(!idx.IsLive(10), "meta dosya yok sayılır");
            Assert.True(!idx.IsLive(60), "üstü bilinmeyen eklenmez");
            Assert.Equal(2, refresh.Count, "yenilenecek: yeni dosya ve kapanan dosya");
            Assert.Equal(fullRef50, refresh[50], "tam referans");
            Assert.True(refresh.ContainsKey(40), "kapanan dosya yenilenir");
        }
    }
}
