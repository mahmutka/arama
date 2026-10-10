using System;
using System.IO;
using System.Text;

namespace Arama.App
{
    /// <summary>Beklenmeyen hataları %APPDATA%\Arama\hata.log dosyasına ekler; kendisi asla hata fırlatmaz.</summary>
    public static class ErrorLog
    {
        static readonly object Sync = new object();

        /// <summary>Testler için farklı dosya.</summary>
        public static string FileOverride;

        public static void Write(Exception ex)
        {
            Write(ex.ToString());
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    string path = FileOverride ?? AppPaths.ErrorLogFile;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
