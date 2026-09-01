using System;
using System.IO;
using System.Windows.Forms;
using System.Threading;

namespace KontroXXL_WinApp
{
    internal static class Program
    {
        private static Mutex mutex = new Mutex(true, "{KONTROXXL-77BB-42C1-BD61-A0B89C2D1F20}");

        [STAThread]
        static void Main()
        {
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                // Zaten calisiyor
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                string m = e.ExceptionObject?.ToString() ?? "Bilinmeyen hata";
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), m); } catch { }
                MessageBox.Show("Kritik Sistem Hatasi:\n\n" + m.Split('\n')[0], "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) => {
                string m = e.Exception?.ToString() ?? "Bilinmeyen hata";
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), m); } catch { }
            };

            try {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
            } catch (Exception ex) {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString()); } catch { }
                MessageBox.Show("Görsel motor hatasi:\n\n" + ex.Message, "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
