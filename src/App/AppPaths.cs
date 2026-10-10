using System;
using System.IO;

namespace Arama.App
{
    public static class AppPaths
    {
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arama"); }
        }

        public static string SettingsFile { get { return Path.Combine(Folder, "ayarlar.ini"); } }
        public static string ErrorLogFile { get { return Path.Combine(Folder, "hata.log"); } }
    }
}
