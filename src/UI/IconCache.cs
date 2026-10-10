using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Arama.UI
{
    /// <summary>Uzantı başına bir sistem simgesi (dosyaya dokunmadan). Yalnızca arayüz iş parçacığında kullanılır.</summary>
    sealed class IconCache
    {
        public readonly ImageList Images = new ImageList();
        readonly Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public IconCache()
        {
            Images.ColorDepth = ColorDepth.Depth32Bit;
            Images.ImageSize = new Size(16, 16);
        }

        public int Get(string name, bool isDirectory)
        {
            string key;
            if (isDirectory) key = "<klasör>";
            else
            {
                int dot = name.LastIndexOf('.');
                key = dot > 0 && dot < name.Length - 1 ? name.Substring(dot) : "<dosya>";
            }
            int index;
            if (map.TryGetValue(key, out index)) return index;

            index = -1;
            var info = new Native.SHFILEINFO();
            string probe = isDirectory ? "klasor" : "dosya" + (key.StartsWith(".") ? key : "");
            uint attributes = isDirectory ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL;
            IntPtr ok = Native.SHGetFileInfo(probe, attributes, ref info, (uint)Marshal.SizeOf(info),
                Native.SHGFI_ICON | Native.SHGFI_SMALLICON | Native.SHGFI_USEFILEATTRIBUTES);
            if (ok != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    using (var icon = Icon.FromHandle(info.hIcon)) Images.Images.Add(icon.ToBitmap());
                    index = Images.Images.Count - 1;
                }
                finally { Native.DestroyIcon(info.hIcon); }
            }
            map[key] = index;
            return index;
        }
    }
}
