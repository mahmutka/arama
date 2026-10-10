using System;
using System.Text;

namespace Arama.Tests
{
    /// <summary>Testler için 1024 baytlık, 512 bayt sektörlü elle MFT kaydı üretir.</summary>
    public sealed class MftRecordBuilder
    {
        const int RecordSize = 1024;
        readonly byte[] buf = new byte[RecordSize];
        int attrPos = 0x38;
        int nextId;

        public MftRecordBuilder(bool inUse, bool isDirectory, long baseRecord)
        {
            Encoding.ASCII.GetBytes("FILE").CopyTo(buf, 0);
            Put16(0x04, 0x30);                  // update sequence dizisinin yeri
            Put16(0x06, 3);                     // 1 + 1024/512
            Put16(0x10, 1);                     // sequence number
            Put16(0x14, 0x38);                  // ilk öznitelik
            Put16(0x16, (inUse ? 1 : 0) | (isDirectory ? 2 : 0));
            Put32(0x1C, RecordSize);
            Put64(0x20, baseRecord);
        }

        public MftRecordBuilder StandardInfo(long modified, uint attributes)
        {
            var value = new byte[0x48];
            BitConverter.GetBytes(modified).CopyTo(value, 0x08);
            BitConverter.GetBytes(attributes).CopyTo(value, 0x20);
            return Resident(0x10, value, 0);
        }

        /// <summary>nameSpace: 0 POSIX, 1 Win32, 2 DOS, 3 Win32+DOS. Üst referansa sequence eklenir (parser maskelemeli).</summary>
        public MftRecordBuilder FileName(long parent, string name, byte nameSpace)
        {
            var value = new byte[0x42 + name.Length * 2];
            BitConverter.GetBytes(parent | (1L << 48)).CopyTo(value, 0);
            value[0x40] = (byte)name.Length;
            value[0x41] = nameSpace;
            Encoding.Unicode.GetBytes(name).CopyTo(value, 0x42);
            return Resident(0x30, value, 0);
        }

        /// <summary>streamNameLength > 0 ise adlı akış (ADS) olur.</summary>
        public MftRecordBuilder ResidentData(int length, int streamNameLength)
        {
            return Resident(0x80, new byte[length], streamNameLength);
        }

        public MftRecordBuilder NonResidentData(long lowestVcn, long realSize, byte[] runs)
        {
            int length = Align8(0x40 + runs.Length);
            int a = attrPos;
            Put32(a, 0x80);
            Put32(a + 4, length);
            buf[a + 8] = 1;
            Put16(a + 0x0A, 0x40);
            Put16(a + 0x0E, nextId++);
            Put64(a + 0x10, lowestVcn);
            Put64(a + 0x18, lowestVcn + 100);
            Put16(a + 0x20, 0x40);
            Put64(a + 0x28, (realSize + 4095) & ~4095L);
            Put64(a + 0x30, realSize);
            Put64(a + 0x38, realSize);
            runs.CopyTo(buf, a + 0x40);
            attrPos += length;
            return this;
        }

        MftRecordBuilder Resident(uint type, byte[] value, int nameLength)
        {
            const int nameOffset = 0x18;
            int valueOffset = Align8(nameOffset + nameLength * 2);
            int length = Align8(valueOffset + value.Length);
            int a = attrPos;
            Put32(a, type);
            Put32(a + 4, length);
            buf[a + 8] = 0;
            buf[a + 9] = (byte)nameLength;
            Put16(a + 0x0A, nameOffset);
            Put16(a + 0x0E, nextId++);
            Put32(a + 0x10, value.Length);
            Put16(a + 0x14, valueOffset);
            for (int i = 0; i < nameLength; i++) Put16(a + nameOffset + i * 2, 'S');
            value.CopyTo(buf, a + valueOffset);
            attrPos += length;
            return this;
        }

        /// <summary>Bitiş işareti, kullanılan bayt ve update sequence korumasını yazar.</summary>
        public byte[] Build()
        {
            Put32(attrPos, 0xFFFFFFFF);
            Put32(0x18, attrPos + 8);
            const int usn = 0x0007;
            Put16(0x30, usn);
            for (int i = 1; i <= 2; i++)
            {
                int pos = i * 512 - 2;
                buf[0x30 + i * 2] = buf[pos];
                buf[0x30 + i * 2 + 1] = buf[pos + 1];
                Put16(pos, usn);
            }
            return (byte[])buf.Clone();
        }

        static int Align8(int n)
        {
            return (n + 7) & ~7;
        }

        void Put16(int pos, int value)
        {
            BitConverter.GetBytes((ushort)value).CopyTo(buf, pos);
        }

        void Put32(int pos, long value)
        {
            BitConverter.GetBytes((uint)value).CopyTo(buf, pos);
        }

        void Put64(int pos, long value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, pos);
        }
    }
}
