using System;
using System.Collections.Generic;
using System.Text;

namespace Arama.Ntfs
{
    /// <summary>Tek bir MFT kaydından çıkarılan bilgiler. Reset ile tekrar kullanılır.</summary>
    public sealed class MftRecord
    {
        public bool InUse;
        public bool IsDirectory;
        public long BaseRecord;          // 0: bu kayıt ana kayıttır
        public bool HasStandardInfo;
        public long ModifiedTime;        // FILETIME (UTC)
        public uint Attributes;          // Windows dosya öznitelikleri
        public string Name;              // null: bu kayıtta $FILE_NAME yok
        public int NameRank;             // 3 Win32, 2 POSIX, 1 DOS
        public long ParentRecord;
        public bool HasSize;
        public long Size;
        public readonly List<Extent> DataRuns = new List<Extent>();

        public void Reset()
        {
            InUse = false;
            IsDirectory = false;
            BaseRecord = 0;
            HasStandardInfo = false;
            ModifiedTime = 0;
            Attributes = 0;
            Name = null;
            NameRank = 0;
            ParentRecord = 0;
            HasSize = false;
            Size = 0;
            DataRuns.Clear();
        }
    }

    /// <summary>Ham MFT kaydını ayrıştırır. Disk erişimi yapmaz; yalnızca bayt dizisi alır.</summary>
    public static class MftRecordParser
    {
        public const int SectorStride = 512;
        const uint TypeStandardInfo = 0x10, TypeFileName = 0x30, TypeData = 0x80, TypeEnd = 0xFFFFFFFF;
        const long RecordMask = 0x0000FFFFFFFFFFFF;

        public static bool HasFileSignature(byte[] buf, int offset)
        {
            return buf[offset] == 'F' && buf[offset + 1] == 'I' && buf[offset + 2] == 'L' && buf[offset + 3] == 'E';
        }

        /// <summary>İmzayı kontrol eder ve update sequence düzeltmesini yerinde uygular.</summary>
        public static bool ApplyFixup(byte[] buf, int offset, int recordSize)
        {
            if (!HasFileSignature(buf, offset)) return false;
            int usaOffset = BitConverter.ToUInt16(buf, offset + 4);
            int usaCount = BitConverter.ToUInt16(buf, offset + 6);
            if (usaCount < 2 || (usaCount - 1) * SectorStride != recordSize || usaOffset + usaCount * 2 > recordSize) return false;
            byte u0 = buf[offset + usaOffset], u1 = buf[offset + usaOffset + 1];
            for (int i = 1; i < usaCount; i++)
            {
                int pos = offset + i * SectorStride - 2;
                if (buf[pos] != u0 || buf[pos + 1] != u1) return false;
                buf[pos] = buf[offset + usaOffset + i * 2];
                buf[pos + 1] = buf[offset + usaOffset + i * 2 + 1];
            }
            return true;
        }

        /// <summary>Düzeltmesi yapılmış kaydı ayrıştırır. wantRuns: isimsiz $DATA'nın runlarını topla ($MFT için).</summary>
        public static bool Parse(byte[] buf, int offset, int recordSize, bool wantRuns, MftRecord rec)
        {
            rec.Reset();
            int flags = BitConverter.ToUInt16(buf, offset + 0x16);
            rec.InUse = (flags & 1) != 0;
            rec.IsDirectory = (flags & 2) != 0;
            rec.BaseRecord = BitConverter.ToInt64(buf, offset + 0x20) & RecordMask;
            if (!rec.InUse) return true;

            int p = BitConverter.ToUInt16(buf, offset + 0x14);
            int used = (int)Math.Min(BitConverter.ToUInt32(buf, offset + 0x18), (uint)recordSize);
            while (p + 8 <= used)
            {
                int a = offset + p;
                uint type = BitConverter.ToUInt32(buf, a);
                if (type == TypeEnd) break;
                long length = BitConverter.ToUInt32(buf, a + 4);
                if (length < 0x18 || p + length > used) return false;
                int len = (int)length;
                bool nonResident = buf[a + 8] != 0;
                int nameLength = buf[a + 9];
                if (!nonResident)
                {
                    long valueLength = BitConverter.ToUInt32(buf, a + 0x10);
                    int valueOffset = BitConverter.ToUInt16(buf, a + 0x14);
                    if (valueOffset + valueLength > len) return false;
                    int v = a + valueOffset;
                    if (type == TypeStandardInfo && valueLength >= 0x24)
                    {
                        rec.HasStandardInfo = true;
                        rec.ModifiedTime = BitConverter.ToInt64(buf, v + 0x08);
                        rec.Attributes = BitConverter.ToUInt32(buf, v + 0x20);
                    }
                    else if (type == TypeFileName && valueLength >= 0x42)
                    {
                        ReadFileName(buf, v, (int)valueLength, rec);
                    }
                    else if (type == TypeData && nameLength == 0)
                    {
                        rec.HasSize = true;
                        rec.Size = valueLength;
                    }
                }
                else if (type == TypeData && nameLength == 0)
                {
                    if (len < 0x40) return false;
                    long lowestVcn = BitConverter.ToInt64(buf, a + 0x10);
                    if (lowestVcn == 0)
                    {
                        rec.HasSize = true;
                        rec.Size = BitConverter.ToInt64(buf, a + 0x30);
                    }
                    if (wantRuns)
                    {
                        int runsOffset = BitConverter.ToUInt16(buf, a + 0x20);
                        if (runsOffset >= len) return false;
                        rec.DataRuns.AddRange(DataRuns.Decode(buf, a + runsOffset, a + len));
                    }
                }
                p += len;
            }
            return true;
        }

        static void ReadFileName(byte[] buf, int v, int valueLength, MftRecord rec)
        {
            int nameLength = buf[v + 0x40];
            int nameSpace = buf[v + 0x41];
            if (0x42 + nameLength * 2 > valueLength) return;
            int rank = nameSpace == 2 ? 1 : nameSpace == 0 ? 2 : 3;
            if (rec.Name != null && rank <= rec.NameRank) return;
            rec.Name = Encoding.Unicode.GetString(buf, v + 0x42, nameLength * 2);
            rec.NameRank = rank;
            rec.ParentRecord = BitConverter.ToInt64(buf, v) & RecordMask;
        }
    }
}
