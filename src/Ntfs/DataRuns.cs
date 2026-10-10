using System;
using System.Collections.Generic;

namespace Arama.Ntfs
{
    /// <summary>Disk üzerindeki ardışık küme aralığı. Lcn = -1 seyrek (sparse) aralık demektir.</summary>
    public struct Extent
    {
        public readonly long Lcn;
        public readonly long Length;

        public Extent(long lcn, long length)
        {
            Lcn = lcn;
            Length = length;
        }

        public override string ToString()
        {
            return Lcn + "+" + Length;
        }
    }

    /// <summary>Non-resident özniteliklerin data run listesini çözer.</summary>
    public static class DataRuns
    {
        public static List<Extent> Decode(byte[] buf, int offset, int end)
        {
            var list = new List<Extent>();
            long lcn = 0;
            int p = offset;
            while (p < end)
            {
                byte header = buf[p];
                if (header == 0) break;
                int lengthSize = header & 0x0F;
                int offsetSize = header >> 4;
                p++;
                if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8 || p + lengthSize + offsetSize > end)
                    throw new FormatException("Bozuk data run");
                long length = ReadUnsigned(buf, p, lengthSize);
                p += lengthSize;
                if (offsetSize == 0)
                {
                    list.Add(new Extent(-1, length));
                    continue;
                }
                lcn += ReadSigned(buf, p, offsetSize);
                p += offsetSize;
                list.Add(new Extent(lcn, length));
            }
            return list;
        }

        static long ReadUnsigned(byte[] buf, int p, int size)
        {
            long value = 0;
            for (int i = size - 1; i >= 0; i--) value = (value << 8) | buf[p + i];
            return value;
        }

        static long ReadSigned(byte[] buf, int p, int size)
        {
            long value = ReadUnsigned(buf, p, size);
            if (size < 8 && (buf[p + size - 1] & 0x80) != 0) value |= -1L << (size * 8);
            return value;
        }
    }
}
