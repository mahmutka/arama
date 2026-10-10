using System;
using System.Diagnostics;
using System.IO;

namespace Arama.UI
{
    /// <summary>
    /// Program yönetici olarak çalışır; açma isteği explorer.exe'ye verilir, o da çalışan
    /// (yükseltilmemiş) Explorer'a iletir. Böylece açılan program yönetici yetkisi devralmaz.
    /// </summary>
    static class ShellLauncher
    {
        public static void Open(string path)
        {
            Start("\"" + path + "\"");
        }

        public static void ShowInFolder(string path)
        {
            Start("/select,\"" + path + "\"");
        }

        static void Start(string arguments)
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var info = new ProcessStartInfo(explorer, arguments);
            info.UseShellExecute = false;
            Process.Start(info);
        }
    }
}
