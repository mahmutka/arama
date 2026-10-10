using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Arama
{
    internal static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_READ_ATTRIBUTES = 0x80;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
        public const uint FSCTL_CREATE_USN_JOURNAL = 0x000900E7;
        public const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;

        public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
        public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
        public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

        public const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_ID_DESCRIPTOR
        {
            public int Size;        // 24
            public int Type;        // 0 = FileIdType
            public long FileId;
            public long Padding;    // birleşimin (union) kalan 8 baytı
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public uint CreationTimeLow, CreationTimeHigh;
            public uint LastAccessTimeLow, LastAccessTimeHigh;
            public uint LastWriteTimeLow, LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh, FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh, FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle file, long distance, out long newPosition, uint moveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FlushFileBuffers(SafeFileHandle file);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, ref FILE_ID_DESCRIPTOR id, uint access, uint share, IntPtr security, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
