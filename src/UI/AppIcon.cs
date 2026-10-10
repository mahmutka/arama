using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Arama.UI
{
    /// <summary>Pencere ve tepsi için çalışma anında çizilen büyüteç simgesi.</summary>
    static class AppIcon
    {
        public static Icon Create()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var lens = new Pen(Color.FromArgb(0, 120, 215), 4))
                using (var handle = new Pen(Color.FromArgb(0, 120, 215), 5))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.DrawEllipse(lens, 4, 4, 17, 17);
                    handle.StartCap = LineCap.Round;
                    handle.EndCap = LineCap.Round;
                    g.DrawLine(handle, 20, 20, 28, 28);
                }
                IntPtr hIcon = bitmap.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(hIcon)) return (Icon)temp.Clone();
                }
                finally { Native.DestroyIcon(hIcon); }
            }
        }
    }
}
