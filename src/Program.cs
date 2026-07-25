using System;
using System.Windows.Forms;

namespace OtimizadorWin10
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SplashForm());
            Application.Run(new MainForm());
        }
    }
}
