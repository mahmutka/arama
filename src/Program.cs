using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Arama.App;
using Arama.UI;

namespace Arama
{
    static class Program
    {
        /// <summary>İkinci kopya açılınca ilk kopyaya "pencereni göster" mesajı.</summary>
        public static readonly int ShowMessage = Native.RegisterWindowMessage("ARAMA_GOSTER");

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\Arama_TekKopya", out createdNew))
            {
                if (!createdNew)
                {
                    Native.PostMessage(Native.HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ErrorLog.Write(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ErrorLog.Write(e.ExceptionObject.ToString());
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool startHidden = args.Any(a => string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));
                Application.Run(new MainForm(startHidden));
                GC.KeepAlive(mutex);
            }
        }
    }
}
