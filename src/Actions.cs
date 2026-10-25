using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OtimizadorWin10
{
    // ---------------------------------------------------------------
    // Acoes atomicas que compoem uma otimizacao. Cada acao sabe:
    //  - descrever o que faz (mostrado ao usuario antes de aplicar)
    //  - capturar o estado atual (para o arquivo de reversao)
    //  - aplicar a mudanca
    // ---------------------------------------------------------------
    public sealed class ActionSkippedException : Exception { public ActionSkippedException(string message) : base(message) { } }
    public sealed class ActionWarningException : Exception { public ActionWarningException(string message) : base(message) { } }

    public abstract class OptAction
    {
        // Acao de "melhor esforco": se o Windows bloquear (ex.: valores que a
        // Microsoft protegeu em builds novas do Win11), registra aviso em vez
        // de falha - desde que outra acao do mesmo item ja cubra o efeito.
        public bool BestEffort;

        public abstract string Describe();
        public abstract Dictionary<string, object> CaptureUndo(); // null = sem reversao
        public abstract void Apply(Action<string> log);

        protected static void RunHidden(string exe, string args, int timeoutMs)
        {
            RunCapture(exe, args, timeoutMs, false);
        }

        protected static string RunCapture(string exe, string args, int timeoutMs, bool utf8)
        {
            if (exe.IndexOfAny(new char[] { '\\', '/' }) < 0)
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                    system = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative");
                exe = System.IO.Path.Combine(system, exe == "powershell.exe" ? @"WindowsPowerShell\v1.0\powershell.exe" : exe);
            }
            var psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            if (utf8) { psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8; }
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEndAsync();
                var errors = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException(exe + " nao respondeu no tempo esperado.");
                }
                System.Threading.Tasks.Task.WaitAll(output, errors);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(exe + " retornou codigo " + p.ExitCode + ": " + errors.Result);
                return output.Result;
            }
        }
    }

    // ----------------------- Registro ------------------------------
    public class RegAction : OptAction
    {
        public string Hive;          // "HKLM" ou "HKCU"
        public string Path;
        public string Name;
        public object Value;
        public RegistryValueKind Kind;
        public string Detalhe;       // opcional, para exibicao

        public RegAction(string hive, string path, string name, object value, RegistryValueKind kind, string detalhe = null)
        {
            Hive = hive; Path = path; Name = name; Value = value; Kind = kind; Detalhe = detalhe;
        }

        static RegistryHive HiveOf(string h)
        {
            return h == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        }

        public override string Describe()
        {
            if (Detalhe != null) return Detalhe;
            return string.Format("Registro: {0}\\{1} -> {2} = {3}", Hive, Path, Name, ValueToText(Value));
        }

        static string ValueToText(object v)
        {
            var arr = v as string[];
            if (arr != null) return string.Join(" | ", arr);
            return Convert.ToString(v);
        }

        public override Dictionary<string, object> CaptureUndo()
        {
            var d = new Dictionary<string, object>();
            d["Kind"] = "reg"; d["Hive"] = Hive; d["Path"] = Path; d["Name"] = Name;
            using (var bk = RegistryKey.OpenBaseKey(HiveOf(Hive), RegistryView.Registry64))
            using (var k = bk.OpenSubKey(Path))
            {
                object cur = (k == null) ? null : k.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (cur == null) { d["Existed"] = false; }
                else
                {
                    d["Existed"] = true;
                    RegistryValueKind vk = k.GetValueKind(Name);
                    d["ValKind"] = vk.ToString();
                    if (vk == RegistryValueKind.MultiString)
                        d["Value"] = string.Join("\n", (string[])cur);
                    else if (vk == RegistryValueKind.Binary)
                        d["Value"] = Convert.ToBase64String((byte[])cur);
                    else
                        d["Value"] = Convert.ToString(cur);
                }
            }
            return d;
        }

        public override void Apply(Action<string> log)
        {
            using (var bk = RegistryKey.OpenBaseKey(HiveOf(Hive), RegistryView.Registry64))
            using (var k = bk.CreateSubKey(Path))
            {
                k.SetValue(Name, Value, Kind);
            }
        }

        public static void Revert(Dictionary<string, object> d, Action<string> log)
        {
            string hive = Convert.ToString(d["Hive"]);
            string path = Convert.ToString(d["Path"]);
            string name = Convert.ToString(d["Name"]);
            bool existed = Convert.ToBoolean(d["Existed"]);
            using (var bk = RegistryKey.OpenBaseKey(HiveOf(hive), RegistryView.Registry64))
            {
                if (!existed)
                {
                    using (var k = bk.OpenSubKey(path, true))
                    {
                        if (k != null) { try { k.DeleteValue(name, false); } catch { } }
                    }
                    log(string.Format("Removido {0}\\{1}\\{2} (nao existia antes)", hive, path, name));
                    return;
                }
                var vk = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), Convert.ToString(d["ValKind"]));
                string raw = Convert.ToString(d["Value"]);
                object val;
                if (vk == RegistryValueKind.DWord) val = int.Parse(raw);
                else if (vk == RegistryValueKind.QWord) val = long.Parse(raw);
                else if (vk == RegistryValueKind.MultiString) val = raw.Split('\n');
                else if (vk == RegistryValueKind.Binary) val = Convert.FromBase64String(raw);
                else val = raw;
                using (var k = bk.CreateSubKey(path)) { k.SetValue(name, val, vk); }
                log(string.Format("Restaurado {0}\\{1}\\{2}", hive, path, name));
            }
        }
    }

    // ----------------------- Servicos ------------------------------
    public class ServiceAction : OptAction
    {
        public string Service;
        public int StartValue;   // 2=Automatico, 3=Manual, 4=Desativado
        public bool StopNow;
        public string NomeAmigavel;

        public ServiceAction(string service, int startValue, string nomeAmigavel, bool stopNow = true)
        {
            Service = service; StartValue = startValue; NomeAmigavel = nomeAmigavel; StopNow = stopNow;
        }

        string RegPath { get { return @"SYSTEM\CurrentControlSet\Services\" + Service; } }

        public bool Exists()
        {
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(RegPath)) { return k != null; }
        }

        public override string Describe()
        {
            string modo = StartValue == 4 ? "Desativado" : (StartValue == 3 ? "Manual" : "Automatico");
            return string.Format("Servico \"{0}\" ({1}) -> inicializacao: {2}", NomeAmigavel, Service, modo);
        }

        public override Dictionary<string, object> CaptureUndo()
        {
            if (!Exists()) return null;
            var d = new Dictionary<string, object>();
            d["Kind"] = "service"; d["Service"] = Service;
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(RegPath))
            {
                d["Start"] = Convert.ToInt32(k.GetValue("Start", 3));
            }
            return d;
        }

        public override void Apply(Action<string> log)
        {
            if (!Exists()) throw new ActionSkippedException("Servico " + Service + " nao existe nesta maquina.");
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(RegPath, true))
            {
                k.SetValue("Start", StartValue, RegistryValueKind.DWord);
            }
            if (StopNow && StartValue >= 3)
            {
                // API nativa em vez de "sc.exe stop": menos um processo filho
                // gerado por aplicacao elevada (padrao observado por heuristica).
                try
                {
                    using (var sc = new System.ServiceProcess.ServiceController(Service))
                    {
                        if (sc.CanStop && sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                        {
                            sc.Stop();
                            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
                                TimeSpan.FromSeconds(8));
                        }
                    }
                }
                catch (Exception ex) { throw new ActionWarningException("Inicializacao alterada, mas nao foi possivel parar " + Service + ": " + ex.Message + ". Pode exigir reinicio."); }
            }
        }

        public static void Revert(Dictionary<string, object> d, Action<string> log)
        {
            string svc = Convert.ToString(d["Service"]);
            int start = Convert.ToInt32(d["Start"]);
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svc, true))
            {
                if (k == null) throw new InvalidOperationException("Servico ausente: " + svc);
                k.SetValue("Start", start, RegistryValueKind.DWord);
            }
            log("Servico " + svc + " restaurado para inicializacao " + start);
        }
    }

    // ----------------------- Comandos ------------------------------
    public class CmdAction : OptAction
    {
        public string Exe;
        public string Args;
        public string UndoExe;
        public string UndoArgs;
        public string Detalhe;
        public int TimeoutMs = 60000;

        public CmdAction(string exe, string args, string detalhe, string undoExe = null, string undoArgs = null)
        {
            Exe = exe; Args = args; Detalhe = detalhe; UndoExe = undoExe; UndoArgs = undoArgs;
        }

        public override string Describe() { return Detalhe; }

        public override Dictionary<string, object> CaptureUndo()
        {
            if (UndoExe == null) return null;
            var d = new Dictionary<string, object>();
            d["Kind"] = "cmd"; d["Exe"] = UndoExe; d["Args"] = UndoArgs; d["Detalhe"] = "Desfazer: " + Detalhe;
            return d;
        }

        public override void Apply(Action<string> log)
        {
            RunHidden(Exe, Args, TimeoutMs);
        }

        public static void Revert(Dictionary<string, object> d, Action<string> log)
        {
            string exe = Convert.ToString(d["Exe"]);
            string args = Convert.ToString(d["Args"]);
            RunHidden(exe, args, 60000);
            log(Convert.ToString(d["Detalhe"]));
        }
    }

    // ----------------------- Apagar valor de registro --------------
    // Substitui o uso de "reg.exe delete" por API nativa: um processo
    // elevado chamando reg.exe e um padrao classico de malware, alem de
    // ser mais lento e sem tratamento de erro decente.
    public class RegDeleteAction : OptAction
    {
        public string Hive;
        public string Path;
        public string Name;
        public string Detalhe;

        public RegDeleteAction(string hive, string path, string name, string detalhe)
        {
            Hive = hive; Path = path; Name = name; Detalhe = detalhe;
        }

        static RegistryHive HiveOf(string h)
        {
            return h == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        }

        public override string Describe() { return Detalhe; }

        // Mesmo formato do RegAction: a reversao recria o valor original.
        public override Dictionary<string, object> CaptureUndo()
        {
            var d = new Dictionary<string, object>();
            d["Kind"] = "reg"; d["Hive"] = Hive; d["Path"] = Path; d["Name"] = Name;
            using (var bk = RegistryKey.OpenBaseKey(HiveOf(Hive), RegistryView.Registry64))
            using (var k = bk.OpenSubKey(Path))
            {
                object cur = (k == null) ? null : k.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (cur == null) { d["Existed"] = false; }
                else
                {
                    d["Existed"] = true;
                    d["ValKind"] = k.GetValueKind(Name).ToString();
                    d["Value"] = Convert.ToString(cur);
                }
            }
            return d;
        }

        public override void Apply(Action<string> log)
        {
            using (var bk = RegistryKey.OpenBaseKey(HiveOf(Hive), RegistryView.Registry64))
            using (var k = bk.OpenSubKey(Path, true))
            {
                if (k == null) { log("  (chave nao existe - nada a remover)"); return; }
                k.DeleteValue(Name, false);
            }
        }
    }

    // ----------------------- Parar/iniciar servico -----------------
    // Usa a API System.ServiceProcess em vez de "cmd.exe /c net stop x & ...".
    public class ServiceControlAction : OptAction
    {
        public string[] Servicos;
        public bool Iniciar;      // false = parar
        public string Detalhe;

        public ServiceControlAction(string[] servicos, bool iniciar, string detalhe)
        {
            Servicos = servicos; Iniciar = iniciar; Detalhe = detalhe;
        }

        public override string Describe() { return Detalhe; }
        public override Dictionary<string, object> CaptureUndo() { return null; }

        public override void Apply(Action<string> log)
        {
            bool warning = false;
            foreach (string nome in Servicos)
            {
                try
                {
                    using (var sc = new System.ServiceProcess.ServiceController(nome))
                    {
                        var espera = TimeSpan.FromSeconds(30);
                        if (Iniciar)
                        {
                            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                            {
                                sc.Start();
                                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, espera);
                            }
                        }
                        else
                        {
                            if (sc.CanStop && sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                            {
                                sc.Stop();
                                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, espera);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    warning = true;
                    log("  (servico " + nome + ": " + ex.Message + ")");
                }
            }
            if (warning) throw new ActionWarningException("Um ou mais servicos nao concluiram a operacao. Confira as mensagens acima.");
        }
    }

    // ----------------------- Desinstalar OneDrive ------------------
    // Substitui a cadeia "cmd.exe /c taskkill /f ... & if exist ... /uninstall",
    // que e sintaticamente identica a um dropper.
    public class UninstallOneDriveAction : OptAction
    {
        public override string Describe()
        {
            return "Encerrar e desinstalar o cliente OneDrive (usa o desinstalador oficial da Microsoft)";
        }

        public override Dictionary<string, object> CaptureUndo() { return null; }

        public override void Apply(Action<string> log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string setup = System.IO.Path.Combine(win, @"SysWOW64\OneDriveSetup.exe");
            if (!File.Exists(setup)) setup = System.IO.Path.Combine(win, @"System32\OneDriveSetup.exe");
            if (!File.Exists(setup))
                throw new ActionWarningException("Desinstalador oficial do OneDrive nao encontrado. O cliente foi preservado; use Aplicativos nas Configuracoes do Windows para confirmar a instalacao/remocao.");
            foreach (var p in Process.GetProcessesByName("OneDrive"))
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch { }
                finally { p.Dispose(); }
            }

            RunHidden(setup, "/uninstall", 120000);
        }
    }

    // ----------------------- Tarefas agendadas ---------------------
    public class TaskAction : OptAction
    {
        public string TaskPath;
        readonly Func<string, ITaskHandle> open;
        public TaskAction(string taskPath) : this(taskPath, delegate(string p) { return new WindowsTaskHandle(p); }) { }
        internal TaskAction(string taskPath, Func<string, ITaskHandle> factory) { TaskPath = taskPath; open = factory; }
        static bool Missing(Exception ex)
        {
            if (ex is TargetInvocationException && ex.InnerException != null) return Missing(ex.InnerException);
            var com = ex as COMException;
            return com != null && (com.ErrorCode == unchecked((int)0x80070002) || com.ErrorCode == unchecked((int)0x80070003));
        }
        public override string Describe() { return "Desativar tarefa agendada: " + TaskPath; }
        public override Dictionary<string, object> CaptureUndo()
        {
            try { using (var task = open(TaskPath)) return new Dictionary<string, object> { { "Kind", "task" }, { "TaskPath", TaskPath }, { "Enabled", task.Enabled } }; }
            catch (Exception ex) { if (Missing(ex)) return null; throw; }
        }
        public override void Apply(Action<string> log)
        {
            try { using (var task = open(TaskPath)) task.Enabled = false; }
            catch (Exception ex) { if (Missing(ex)) throw new ActionSkippedException("Tarefa nao existe nesta versao do Windows: " + TaskPath); throw; }
        }
        public static void Revert(Dictionary<string, object> d, Action<string> log)
        { RevertWith(d, log, delegate(string p) { return new WindowsTaskHandle(p); }); }
        internal static void RevertWith(Dictionary<string, object> d, Action<string> log, Func<string, ITaskHandle> factory)
        {
            string path = Convert.ToString(d["TaskPath"]);
            bool enabled = !d.ContainsKey("Enabled") || Convert.ToBoolean(d["Enabled"]);
            using (var task = factory(path)) task.Enabled = enabled;
            log("Estado anterior da tarefa restaurado: " + path + " (ativa: " + enabled + ")");
        }
    }

    // ----------------------- Apps nativos (Appx) -------------------
    public class AppxAction : OptAction
    {
        public string PackageName;   // ex.: Microsoft.BingNews
        public string NomeAmigavel;

        public AppxAction(string packageName, string nomeAmigavel)
        {
            PackageName = packageName; NomeAmigavel = nomeAmigavel;
        }

        public override string Describe()
        {
            return string.Format("Desinstalar app \"{0}\" ({1}) nos perfis instalados; sem reversao automatica", NomeAmigavel, PackageName);
        }

        public override Dictionary<string, object> CaptureUndo() { return null; } // reinstalavel pela Store

        // CommandLineToArgvW/CRT: mantem aspas, barras e quebras de linha do
        // script literal auditavel, sem bypass de politica nem comando ofuscado.
        internal static string QuoteArgument(string value)
        {
            var text = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                text.Append(c); slashes = 0;
            }
            text.Append('\\', slashes * 2); text.Append('"');
            return text.ToString();
        }
        internal static string RunPowerShell(string command)
        {
            return RunCapture("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command " + QuoteArgument(command), 180000, true);
        }

        internal static void InterpretResult(string json, Action<string> log)
        {
            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (result == null || !result.ContainsKey("status")) throw new InvalidDataException("Resposta de desinstalacao invalida.");
            object messages;
            if (result.TryGetValue("messages", out messages))
            {
                var values = messages as System.Collections.IEnumerable;
                if (values != null && !(messages is string)) foreach (var value in values) log("  " + Convert.ToString(value));
            }
            string status = Convert.ToString(result["status"]);
            if (status == "removed") return;
            if (status == "absent") throw new ActionSkippedException("App sem instalacao ativa. Nao era necessario remover.");
            if (status == "blocked") throw new ActionWarningException("Remocao bloqueada pelo Windows; componente protegido preservado.");
            throw new InvalidOperationException("A remocao nao foi confirmada. Consulte as orientacoes e o codigo do Windows no log.");
        }
        public override void Apply(Action<string> log)
        {
            if (!Regex.IsMatch(PackageName ?? "", @"^[A-Za-z0-9][A-Za-z0-9._-]{1,159}$")) throw new ArgumentException("Nome de pacote invalido.");
            string script;
            using (var stream = typeof(AppxAction).Assembly.GetManifestResourceStream("AppxRemoval.ps1"))
            {
                if (stream == null) throw new InvalidOperationException("Rotina de desinstalacao ausente do executavel.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) script = reader.ReadToEnd();
            }
            string command = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); & { " + script + "\n } -PackageName '" + PackageName + "'";
            InterpretResult(RunPowerShell(command), log);
        }
    }

    // ----------------------- Limpeza de arquivos -------------------
    public class CleanAction : OptAction
    {
        public string[] Pastas;
        public string Detalhe;
        public string Mask;

        public CleanAction(string[] pastas, string detalhe, string mask = "*") { Pastas = pastas; Detalhe = detalhe; Mask = mask; }

        public override string Describe() { return Detalhe; }
        public override Dictionary<string, object> CaptureUndo() { return null; }

        public override void Apply(Action<string> log)
        {
            long liberado = 0;
            foreach (string pasta in Pastas)
            {
                string p = Environment.ExpandEnvironmentVariables(pasta);
                if (!Directory.Exists(p)) continue;
                string full = System.IO.Path.GetFullPath(p).TrimEnd('\\');
                if (full == System.IO.Path.GetPathRoot(full).TrimEnd('\\')) throw new InvalidOperationException("Limpeza da raiz de uma unidade nao e permitida.");
                liberado += LimparPasta(full, Mask);
            }
            log(string.Format("  Liberado: {0:0.0} MB", liberado / (1024.0 * 1024.0)));
        }

        static long LimparPasta(string pasta, string mask)
        {
            long total = 0;
            if ((File.GetAttributes(pasta) & FileAttributes.ReparsePoint) != 0) return 0;
            try
            {
                foreach (string f in Directory.GetFiles(pasta, mask))
                {
                    try
                    {
                        if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) continue;
                        long len = new FileInfo(f).Length;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        total += len;
                    }
                    catch { }
                }
                foreach (string d in mask == "*" ? Directory.GetDirectories(pasta) : new string[0])
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    total += LimparPasta(d, mask);
                    try { Directory.Delete(d, false); } catch { }
                }
            }
            catch { }
            return total;
        }
    }
}
