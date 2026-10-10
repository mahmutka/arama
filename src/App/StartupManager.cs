using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Arama.App
{
    /// <summary>
    /// "Windows ile başlat": oturum açılınca en yüksek yetkiyle /tray argümanıyla çalışan bir zamanlanmış görev.
    /// XML ile oluşturulur; schtasks'ın varsayılan 72 saat süre sınırı ve pil koşulları kapatılır.
    /// </summary>
    public static class StartupManager
    {
        public const string TaskName = "Arama";

        public static bool IsEnabled(string taskName)
        {
            return RunSchtasks("/Query /TN \"" + taskName + "\"") == 0;
        }

        public static bool Enable(string taskName, string exePath)
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "arama_gorev_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(xmlPath, BuildTaskXml(exePath, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
                return RunSchtasks("/Create /F /TN \"" + taskName + "\" /XML \"" + xmlPath + "\"") == 0;
            }
            finally
            {
                try { File.Delete(xmlPath); }
                catch (IOException) { }
            }
        }

        public static bool Disable(string taskName)
        {
            return RunSchtasks("/Delete /F /TN \"" + taskName + "\"") == 0;
        }

        public static string BuildTaskXml(string exePath, string userId)
        {
            string user = SecurityElement.Escape(userId);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Arama dosya arama programı</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exePath) + "</Command><Arguments>/tray</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        static int RunSchtasks(string arguments)
        {
            var info = new ProcessStartInfo("schtasks.exe", arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (var process = Process.Start(info))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    return -1;
                }
                return process.ExitCode;
            }
        }
    }
}
