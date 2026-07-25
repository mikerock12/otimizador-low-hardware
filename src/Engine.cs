using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace OtimizadorWin10
{
    // Uma otimizacao exibida ao usuario: agrupa uma ou mais acoes.
    public class Optimization
    {
        public string Id;
        public string Categoria;
        public string Nome;
        public string Descricao;      // o que sera feito, em linguagem clara
        public int Tier;              // 1=Leve, 2=Master, 3=Ultra (tier minimo em que entra marcada)
        public Func<HardwareInfo, bool> Aplicavel;  // null = sempre
        public string Aviso;          // alerta exibido ao usuario (efeito colateral relevante)
        public bool DesmarcadaPorPadrao; // aparece no perfil mas exige opt-in
        public List<OptAction> Acoes = new List<OptAction>();

        public bool AplicaA(HardwareInfo hw)
        {
            return Aplicavel == null || Aplicavel(hw);
        }

        public string DescricaoCompleta()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Descricao);
            sb.AppendLine();
            sb.AppendLine("O que sera feito:");
            foreach (var a in Acoes) sb.AppendLine("  - " + a.Describe());
            if (!string.IsNullOrEmpty(Aviso))
            {
                sb.AppendLine();
                sb.AppendLine("ATENCAO: " + Aviso);
            }
            return sb.ToString();
        }
    }

    public class ApplyResult
    {
        public int Sucesso;
        public int Falhas;
        public int Avisos;
        public string ArquivoUndo;
        public string ArquivoLog;
        public bool RestorePointOk;
    }

    public static class Engine
    {
        public static string PastaDados
        {
            get
            {
                string p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "OtimizadorWin10");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static ApplyResult Aplicar(List<Optimization> selecionadas, bool criarRestorePoint,
                                          Action<string> log, Action<int, int> progresso)
        {
            var res = new ApplyResult();
            var undoList = new List<Dictionary<string, object>>();
            var logLines = new List<string>();
            Action<string> logAll = delegate(string s)
            {
                logLines.Add(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, s));
                log(s);
            };

            if (criarRestorePoint)
            {
                logAll("Criando ponto de restauracao do sistema (pode levar 1-2 minutos)...");
                res.RestorePointOk = CriarPontoRestauracao();
                logAll(res.RestorePointOk
                    ? "Ponto de restauracao criado com sucesso."
                    : "AVISO: nao foi possivel criar o ponto de restauracao (Restauracao do Sistema pode estar desativada). Prosseguindo mesmo assim; um arquivo de reversao propria sera gravado.");
            }

            int total = selecionadas.Count, feito = 0;
            foreach (var opt in selecionadas)
            {
                logAll("");
                logAll(">> " + opt.Nome);
                foreach (var acao in opt.Acoes)
                {
                    try
                    {
                        var undo = acao.CaptureUndo();
                        acao.Apply(logAll);
                        if (undo != null) undoList.Add(undo);
                        logAll("  OK: " + acao.Describe());
                        res.Sucesso++;
                    }
                    catch (Exception ex)
                    {
                        if (acao.BestEffort)
                        {
                            logAll("  AVISO (nao critico, ignorado): " + acao.Describe() + " -> " + ex.Message);
                            res.Avisos++;
                        }
                        else
                        {
                            logAll("  FALHA: " + acao.Describe() + " -> " + ex.Message);
                            res.Falhas++;
                        }
                    }
                }
                feito++;
                progresso(feito, total);
            }

            // Grava arquivo de reversao (na ordem inversa de aplicacao)
            try
            {
                undoList.Reverse();
                var ser = new JavaScriptSerializer();
                string undoPath = Path.Combine(PastaDados,
                    "undo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                File.WriteAllText(undoPath, ser.Serialize(undoList), Encoding.UTF8);
                res.ArquivoUndo = undoPath;
                logAll("");
                logAll("Arquivo de reversao gravado em: " + undoPath);
            }
            catch (Exception ex)
            {
                logAll("AVISO: falha ao gravar arquivo de reversao: " + ex.Message);
            }

            // Grava log
            try
            {
                string logPath = Path.Combine(PastaDados,
                    "log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllLines(logPath, logLines, Encoding.UTF8);
                res.ArquivoLog = logPath;
            }
            catch { }

            return res;
        }

        public static string UltimoArquivoUndo()
        {
            try
            {
                return Directory.GetFiles(PastaDados, "undo_*.json")
                    .OrderByDescending(f => f).FirstOrDefault();
            }
            catch { return null; }
        }

        public static int Reverter(string undoFile, Action<string> log)
        {
            var ser = new JavaScriptSerializer();
            var lista = ser.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(undoFile, Encoding.UTF8));
            int ok = 0;
            foreach (var d in lista)
            {
                try
                {
                    string kind = Convert.ToString(d["Kind"]);
                    if (kind == "reg") RegAction.Revert(d, log);
                    else if (kind == "service") ServiceAction.Revert(d, log);
                    else if (kind == "cmd") CmdAction.Revert(d, log);
                    else if (kind == "task") TaskAction.Revert(d, log);
                    ok++;
                }
                catch (Exception ex)
                {
                    log("Falha ao reverter um item: " + ex.Message);
                }
            }
            try { File.Move(undoFile, undoFile + ".revertido"); } catch { }
            return ok;
        }

        // Cria o ponto de restauracao pela API WMI (classe SystemRestore em
        // root\default), sem lancar powershell.exe oculto. Alem de mais rapido,
        // evita o padrao "processo elevado gera shell escondido", que e o
        // comportamento numero 1 sinalizado por antivirus heuristico.
        static bool CriarPontoRestauracao()
        {
            try
            {
                // Remove o limite de 1 ponto por 24h
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    k.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }

                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                {
                    // Garante a Restauracao do Sistema ligada no C: (ignora erro:
                    // em muitas maquinas ja esta ativa e o metodo retorna falha)
                    try
                    {
                        var pEnable = cls.GetMethodParameters("Enable");
                        pEnable["Drive"] = @"C:\";
                        pEnable["WaitTillEnabled"] = true;
                        cls.InvokeMethod("Enable", pEnable, null);
                    }
                    catch { }

                    var pCreate = cls.GetMethodParameters("CreateRestorePoint");
                    pCreate["Description"] = "Otimizador Low Hardware";
                    pCreate["RestorePointType"] = 12;  // MODIFY_SETTINGS
                    pCreate["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                    var saida = cls.InvokeMethod("CreateRestorePoint", pCreate, null);
                    uint ret = Convert.ToUInt32(saida["ReturnValue"]);
                    return ret == 0;
                }
            }
            catch { return false; }
        }
    }
}
