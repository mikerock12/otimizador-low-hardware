using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OtimizadorWin10
{
    internal interface ITaskHandle : IDisposable
    {
        bool Enabled { get; set; }
    }

    // API oficial do Agendador: preserva o estado anterior sem interpretar textos
    // localizados de schtasks.exe nem tratar acesso negado como "tarefa ausente".
    internal sealed class WindowsTaskHandle : ITaskHandle
    {
        object service, folder, task;
        static object Call(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
        }
        public WindowsTaskHandle(string path)
        {
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                Call(service, "Connect", Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                folder = Call(service, "GetFolder", @"\");
                task = Call(folder, "GetTask", path);
            }
            catch { Dispose(); throw; }
        }
        public bool Enabled
        {
            get { return (bool)task.GetType().InvokeMember("Enabled", BindingFlags.GetProperty, null, task, null); }
            set { task.GetType().InvokeMember("Enabled", BindingFlags.SetProperty, null, task, new object[] { value }); }
        }
        public void Dispose()
        {
            foreach (var value in new object[] { task, folder, service })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            task = folder = service = null;
        }
    }
}
