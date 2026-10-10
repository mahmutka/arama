using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Arama.Tests
{
    static class TestFiles
    {
        /// <summary>Uzun (8.3 olmayan) yollu, boş bir test klasörü oluşturur.</summary>
        public static string NewRoot()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AramaTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        public static string CreateTree()
        {
            string root = NewRoot();
            File.WriteAllBytes(Path.Combine(root, "öğrenci notları.txt"), new byte[1234]);
            Directory.CreateDirectory(Path.Combine(root, "Alt", "Derin"));
            File.WriteAllBytes(Path.Combine(root, "Alt", "İçerik.DOCX"), new byte[0]);
            File.WriteAllBytes(Path.Combine(root, "Alt", "Derin", "büyük.bin"), new byte[70000]);
            return root;
        }

        /// <summary>Tam NTFS dosya referansı (kayıt no + sequence).</summary>
        public static long FileReference(string path)
        {
            using (var handle = Native.CreateFile(path, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
                Native.BY_HANDLE_FILE_INFORMATION info;
                if (!Native.GetFileInformationByHandle(handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
                return ((long)info.FileIndexHigh << 32) | info.FileIndexLow;
            }
        }

        public static int RecordId(string path)
        {
            return (int)(FileReference(path) & 0x0000FFFFFFFFFFFF);
        }
    }
}
