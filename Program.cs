using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Daftari;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    const int SW_RESTORE = 9;

    [STAThread]
    static void Main()
    {
        // نسخة واحدة فقط: تشغيل ثانٍ يُحضر النافذة المفتوحة بدل فتح نسخة جديدة
        // (نسختان تحرران نفس الملفات قد تمحو إحداهما تعديلات الأخرى)
        using var mutex = new Mutex(true, "DaftariSingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName("Daftari"))
            {
                if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    break;
                }
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
