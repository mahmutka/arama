using System;
using System.Collections.Generic;
using System.Text;
using Arama.Index;

namespace Arama.Usn
{
    public struct UsnEvent
    {
        public long FileReference;   // tam referans (kayıt no + sequence)
        public int Id;               // MFT kayıt no
        public int ParentId;
        public uint Reason;
        public uint Attributes;
        public string Name;
    }

    public enum UsnAction { Ignore, Remove, Upsert }

    public static class UsnRecordParser
    {
        public const uint DataOverwrite = 0x1, DataExtend = 0x2, DataTruncation = 0x4,
            FileCreate = 0x100, FileDelete = 0x200, RenameOldName = 0x1000, RenameNewName = 0x2000,
            BasicInfoChange = 0x8000, Close = 0x80000000;
        const long RecordMask = 0x0000FFFFFFFFFFFF;

        /// <summary>FSCTL_READ_USN_JOURNAL çıktısı: ilk 8 bayt sonraki USN, ardından USN_RECORD_V2 kayıtları.</summary>
        public static long Parse(byte[] buf, int length, List<UsnEvent> output)
        {
            if (length < 8) return 0;
            long next = BitConverter.ToInt64(buf, 0);
            int p = 8;
            while (p + 60 <= length)
            {
                int recordLength = BitConverter.ToInt32(buf, p);
                if (recordLength < 60 || p + recordLength > length) break;
                int major = BitConverter.ToUInt16(buf, p + 4);
                if (major == 2)
                {
                    int nameLength = BitConverter.ToUInt16(buf, p + 56);
                    int nameOffset = BitConverter.ToUInt16(buf, p + 58);
                    if (nameOffset + nameLength <= recordLength)
                    {
                        var ev = new UsnEvent();
                        ev.FileReference = BitConverter.ToInt64(buf, p + 8);
                        ev.Id = (int)(ev.FileReference & RecordMask);
                        ev.ParentId = (int)(BitConverter.ToInt64(buf, p + 16) & RecordMask);
                        ev.Reason = BitConverter.ToUInt32(buf, p + 40);
                        ev.Attributes = BitConverter.ToUInt32(buf, p + 52);
                        ev.Name = Encoding.Unicode.GetString(buf, p + nameOffset, nameLength);
                        output.Add(ev);
                    }
                }
                p += recordLength;
            }
            return next;
        }

        public static UsnAction Classify(uint reason)
        {
            if ((reason & FileDelete) != 0) return UsnAction.Remove;
            if ((reason & RenameOldName) != 0 && (reason & RenameNewName) == 0) return UsnAction.Ignore;
            return UsnAction.Upsert;
        }
    }

    public static class UsnApplier
    {
        /// <summary>
        /// Olayları indekse uygular (yazma kilidi çağıranda).
        /// Dönen sözlük: boyut/tarihi diskten okunacak girdi → tam dosya referansı.
        /// </summary>
        public static Dictionary<int, long> Apply(FileIndex index, List<UsnEvent> events)
        {
            var refresh = new Dictionary<int, long>();
            foreach (var ev in events)
            {
                if (ev.Id < FileIndex.FirstUserRecord) continue;
                UsnAction action = UsnRecordParser.Classify(ev.Reason);
                if (action == UsnAction.Ignore) continue;
                bool parentKnown = ev.ParentId == FileIndex.RootId || index.IsLive(ev.ParentId);
                if (action == UsnAction.Remove || !parentKnown)
                {
                    index.Remove(ev.Id);
                    refresh.Remove(ev.Id);
                    continue;
                }
                bool added = index.Upsert(ev.Id, ev.Name, ev.ParentId, ev.Attributes);
                if (added || (ev.Reason & UsnRecordParser.Close) != 0) refresh[ev.Id] = ev.FileReference;
            }
            return refresh;
        }
    }
}
