using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Arama.Index;
using Microsoft.Win32.SafeHandles;

namespace Arama.Ntfs
{
    public sealed class MftScanResult
    {
        public FileIndex Index;
        public int CorruptRecords;
    }

    /// <summary>NTFS diskinin MFT'sini ham olarak okuyup FileIndex kurar.</summary>
    public static class MftReader
    {
        const int ChunkSize = 4 * 1024 * 1024;

        public static SafeFileHandle OpenVolume(char drive)
        {
            var handle = Native.CreateFile(VolumePath(drive), Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), drive + ": diski açılamadı");
            return handle;
        }

        /// <summary>Önbellekte bekleyen değişiklikleri (MFT dahil) diske yazdırır; ham okuma güncel olur.</summary>
        public static void FlushVolume(char drive)
        {
            using (var handle = Native.CreateFile(VolumePath(drive), Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (!handle.IsInvalid) Native.FlushFileBuffers(handle);
            }
        }

        public static MftScanResult Read(SafeFileHandle volume, char drive, Action<int> progress)
        {
            var volumeData = new byte[96];
            int returned;
            if (!Native.DeviceIoControl(volume, Native.FSCTL_GET_NTFS_VOLUME_DATA, null, 0, volumeData, volumeData.Length, out returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), drive + ": NTFS bilgisi alınamadı");
            int bytesPerSector = BitConverter.ToInt32(volumeData, 40);
            int bytesPerCluster = BitConverter.ToInt32(volumeData, 44);
            int recordSize = BitConverter.ToInt32(volumeData, 48);
            long mftValidLength = BitConverter.ToInt64(volumeData, 56);
            long mftStartLcn = BitConverter.ToInt64(volumeData, 64);

            // Kayıt 0 ($MFT): MFT'nin disk üzerindeki parçaları
            var record = new MftRecord();
            var first = new byte[RoundUp(recordSize, bytesPerSector)];
            ReadAt(volume, mftStartLcn * bytesPerCluster, first, first.Length);
            if (!MftRecordParser.ApplyFixup(first, 0, recordSize) || !MftRecordParser.Parse(first, 0, recordSize, true, record) || record.DataRuns.Count == 0)
                throw new InvalidDataException(drive + ": $MFT kaydı okunamadı");
            var extents = new List<Extent>(record.DataRuns);
            long mftClusters = 0;
            foreach (var extent in extents) mftClusters += extent.Length;
            if (mftClusters * bytesPerCluster < mftValidLength)
                throw new InvalidDataException(drive + ": MFT çok parçalı, desteklenmiyor");

            long totalRecords = mftValidLength / recordSize;
            var result = new MftScanResult();
            result.Index = new FileIndex(drive, (int)totalRecords);
            var rank = new byte[totalRecords];
            var buffer = new byte[ChunkSize];
            long recordNo = 0;
            int lastPercent = -1;
            foreach (var extent in extents)
            {
                long position = extent.Lcn * bytesPerCluster;
                long remaining = extent.Length * bytesPerCluster;
                while (remaining > 0 && recordNo < totalRecords)
                {
                    int count = (int)Math.Min(ChunkSize, remaining);
                    if (extent.Lcn < 0) Array.Clear(buffer, 0, count);
                    else ReadAt(volume, position, buffer, count);
                    for (int offset = 0; offset + recordSize <= count && recordNo < totalRecords; offset += recordSize, recordNo++)
                    {
                        if (!MftRecordParser.HasFileSignature(buffer, offset)) continue;
                        if (!MftRecordParser.ApplyFixup(buffer, offset, recordSize) || !MftRecordParser.Parse(buffer, offset, recordSize, false, record))
                        {
                            result.CorruptRecords++;
                            continue;
                        }
                        Apply(result.Index, rank, (int)recordNo, record);
                    }
                    position += count;
                    remaining -= count;
                    int percent = (int)(recordNo * 100 / Math.Max(1, totalRecords));
                    if (percent != lastPercent && progress != null)
                    {
                        lastPercent = percent;
                        progress(percent);
                    }
                }
            }
            result.Index.FinishLoad();
            return result;
        }

        /// <summary>Kaydı indekse işler; uzantı kayıtları ana kaydın girdisini günceller.</summary>
        static void Apply(FileIndex index, byte[] rank, int recordNo, MftRecord rec)
        {
            if (!rec.InUse) return;
            long target = rec.BaseRecord != 0 ? rec.BaseRecord : recordNo;
            if (target >= rank.Length) return;
            int id = (int)target;
            if (rec.BaseRecord == 0) index.LoadRecord(id, rec.IsDirectory, rec.ModifiedTime, rec.Attributes);
            if (rec.Name != null && rec.NameRank > rank[id])
            {
                rank[id] = (byte)rec.NameRank;
                index.LoadName(id, rec.Name, (int)rec.ParentRecord);
            }
            if (rec.HasSize && !rec.IsDirectory) index.LoadSize(id, rec.Size);
        }

        static void ReadAt(SafeFileHandle volume, long position, byte[] buffer, int count)
        {
            long newPosition;
            int read;
            if (!Native.SetFilePointerEx(volume, position, out newPosition, 0) || !Native.ReadFile(volume, buffer, count, out read, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Disk okunamadı (konum " + position + ")");
            if (read != count) throw new IOException("Eksik okuma: " + read + "/" + count);
        }

        static string VolumePath(char drive)
        {
            return "\\\\.\\" + drive + ":";
        }

        static int RoundUp(int value, int multiple)
        {
            return (value + multiple - 1) / multiple * multiple;
        }
    }
}
