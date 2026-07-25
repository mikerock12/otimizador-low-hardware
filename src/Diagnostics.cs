using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace OtimizadorWin10
{
    public class DiagResult
    {
        // Teste de RAM
        public long RamTestadaMB;
        public int RamErros;
        public double RamTesteMBs;

        // Saude do disco
        public string SaudeDiscoTexto = "";
        public int SaudeNivel = 0; // 0=ok, 1=atencao, 2=risco
        public long HorasLigado = -1;
        public int TemperaturaC = -1;
        public int SaudeSSDPct = -1;       // 0-100 (so SSD)
        public long SetoresRealocados = -1;

        // Benchmark
        public double CpuMops;
        public double RamMBs;
        public double SeqWriteMBs;
        public double SeqReadMBs;
        public double Rand4kIops;

        // Notas
        public double NotaCpu, NotaRam, NotaDisco, NotaFinal;
        public string Veredito = "";
    }

    public static class Diagnostics
    {
        // ------------------------------------------------------------
        // Executa o diagnostico completo. Chamar fora da thread de UI.
        // ------------------------------------------------------------
        public static DiagResult Executar(HardwareInfo hw, Action<string> log, Action<int> pct)
        {
            var r = new DiagResult();
            bool ehHdd = hw.Disco != DiskKind.SSD;

            log("=== Teste rapido de memoria RAM ===");
            pct(3);
            TestarRam(r, log);
            pct(20);

            log("");
            log("=== Saude do disco (SMART) ===");
            VerificarSaudeDisco(hw, r, log);
            pct(30);

            log("");
            log("=== Benchmark de CPU e RAM ===");
            r.CpuMops = BenchCpu(log);
            pct(42);
            r.RamMBs = BenchRam(log);
            pct(50);

            log("");
            log("=== Benchmark de disco ===");
            BenchDisco(r, log);
            pct(ehHdd ? 65 : 95);

            if (ehHdd)
            {
                log("");
                log("=== Verificacao do sistema de arquivos (chkdsk) ===");
                RodarChkdsk(r, log);
                pct(95);
            }

            Pontuar(r);
            pct(100);
            return r;
        }

        // ------------------------------------------------------------
        // 1) Teste rapido de RAM: grava padroes e verifica.
        // Nao substitui um MemTest completo, mas pega erros grosseiros.
        // ------------------------------------------------------------
        static void TestarRam(DiagResult r, Action<string> log)
        {
            const int BLOCO = 32 * 1024 * 1024;
            int alvoBlocos = 16; // 512 MB
            var blocos = new List<byte[]>();
            try
            {
                for (int i = 0; i < alvoBlocos; i++)
                {
                    try { blocos.Add(new byte[BLOCO]); }
                    catch (OutOfMemoryException) { break; }
                }
                if (blocos.Count == 0) { log("Sem memoria livre suficiente para o teste."); return; }

                r.RamTestadaMB = (long)blocos.Count * BLOCO / (1024 * 1024);
                log(string.Format("Testando {0} MB de RAM (2 passadas de padrao + verificacao)...", r.RamTestadaMB));

                var sw = Stopwatch.StartNew();
                int erros = 0;
                for (int passada = 0; passada < 2; passada++)
                {
                    byte padraoBase = passada == 0 ? (byte)0xA5 : (byte)0x5A;
                    for (int b = 0; b < blocos.Count; b++)
                    {
                        byte[] blk = blocos[b];
                        byte v = (byte)(padraoBase ^ b);
                        for (int i = 0; i < blk.Length; i++) blk[i] = (byte)(v + (i & 0x3F));
                    }
                    for (int b = 0; b < blocos.Count && erros < 100; b++)
                    {
                        byte[] blk = blocos[b];
                        byte v = (byte)(padraoBase ^ b);
                        for (int i = 0; i < blk.Length; i++)
                        {
                            if (blk[i] != (byte)(v + (i & 0x3F))) { erros++; if (erros >= 100) break; }
                        }
                    }
                }
                sw.Stop();
                r.RamErros = erros;
                long bytesProcessados = (long)blocos.Count * BLOCO * 4; // 2 passadas x (escrita+leitura)
                r.RamTesteMBs = bytesProcessados / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
                log(erros == 0
                    ? string.Format("Resultado: OK - nenhum erro em {0} MB ({1:0} MB/s).", r.RamTestadaMB, r.RamTesteMBs)
                    : string.Format("ATENCAO: {0} erro(s) de memoria detectados! Recomenda-se teste completo (MemTest86) e/ou troca do modulo.", erros));
            }
            finally
            {
                blocos.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        // ------------------------------------------------------------
        // 2) Saude do disco: SMART (predicao de falha) + status WMI.
        // ------------------------------------------------------------
        static void VerificarSaudeDisco(HardwareInfo hw, DiagResult r, Action<string> log)
        {
            var linhas = new List<string>();
            int nivel = 0;

            // HealthStatus do namespace de Storage (0=Healthy, 1=Warning, 2=Unhealthy)
            try
            {
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceId, HealthStatus FROM MSFT_PhysicalDisk")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]);
                        if (hw.DiscoIndice >= 0 && devId != hw.DiscoIndice.ToString()) continue;
                        int h = 0; try { h = Convert.ToInt32(mo["HealthStatus"]); } catch { }
                        if (h == 0) linhas.Add("Status geral do disco: Saudavel");
                        else if (h == 1) { linhas.Add("Status geral do disco: ATENCAO (Warning)"); if (nivel < 1) nivel = 1; }
                        else { linhas.Add("Status geral do disco: NAO SAUDAVEL"); nivel = 2; }
                        break;
                    }
                }
            }
            catch { linhas.Add("Status geral do disco: nao disponivel nesta maquina."); }

            // SMART: predicao de falha iminente
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi");
                scope.Connect();
                bool algum = false, falha = false;
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        algum = true;
                        if (Convert.ToBoolean(mo["PredictFailure"])) falha = true;
                    }
                }
                if (!algum) linhas.Add("SMART: o disco nao expoe predicao de falha (comum em alguns modelos).");
                else if (falha) { linhas.Add("SMART: *** PREDICAO DE FALHA ATIVA - FACA BACKUP IMEDIATAMENTE E TROQUE O DISCO ***"); nivel = 2; }
                else linhas.Add("SMART: sem predicao de falha.");
            }
            catch { linhas.Add("SMART: leitura nao disponivel."); }

            // Detalhes SMART: horas ligado, temperatura, desgaste, setores realocados
            LerSmartDetalhado(hw, r, linhas);
            bool ehSsd = hw.Disco == DiskKind.SSD;
            if (r.HorasLigado >= 0)
                linhas.Add(string.Format("Horas ligado: {0:n0} h (~{1:0.0} anos de uso continuo)",
                    r.HorasLigado, r.HorasLigado / 8760.0));
            if (r.TemperaturaC > 0)
                linhas.Add(string.Format("Temperatura atual: {0} C{1}", r.TemperaturaC,
                    r.TemperaturaC >= 55 ? " - ATENCAO: acima do ideal, verifique ventilacao" : ""));
            if (ehSsd)
            {
                if (r.SaudeSSDPct >= 0)
                {
                    linhas.Add(string.Format("Saude do SSD (vida util restante): {0}%", r.SaudeSSDPct));
                    if (r.SaudeSSDPct < 30) { linhas.Add("ATENCAO: SSD proximo do fim da vida util - planeje a troca."); if (nivel < 1) nivel = 1; }
                    else if (r.SaudeSSDPct < 60) linhas.Add("Obs.: desgaste moderado, dentro do aceitavel.");
                }
                else
                    linhas.Add("Saude do SSD: o disco nao expoe o indicador de desgaste por esta interface.");
            }
            if (r.SetoresRealocados > 0)
            {
                linhas.Add(string.Format("ATENCAO: {0} setor(es) realocado(s) - o disco ja substituiu areas defeituosas. Monitore e mantenha backup.", r.SetoresRealocados));
                if (nivel < 1) nivel = 1;
            }
            else if (r.SetoresRealocados == 0)
                linhas.Add("Setores realocados: 0 (bom sinal).");

            // Espaco livre no C:
            try
            {
                var di = new DriveInfo("C");
                double livreGB = di.AvailableFreeSpace / 1e9;
                double totalGB = di.TotalSize / 1e9;
                double pctLivre = 100.0 * di.AvailableFreeSpace / di.TotalSize;
                linhas.Add(string.Format("Espaco livre em C:: {0:0.0} GB de {1:0.0} GB ({2:0}%)", livreGB, totalGB, pctLivre));
                if (pctLivre < 10)
                {
                    linhas.Add("ATENCAO: menos de 10% livre - o Windows fica lento sem folga em disco.");
                    if (nivel < 1) nivel = 1;
                }
            }
            catch { }

            foreach (string l in linhas) log(l);
            r.SaudeDiscoTexto = string.Join("\r\n", linhas);
            r.SaudeNivel = nivel;
        }

        // ------------------------------------------------------------
        // Detalhes SMART em duas fontes:
        //  1) MSFT_StorageReliabilityCounter (Win8+, mais confiavel)
        //  2) Atributos SMART brutos (fallback para discos antigos)
        // ------------------------------------------------------------
        static void LerSmartDetalhado(HardwareInfo hw, DiagResult r, List<string> linhas)
        {
            // Fonte 1: contadores de confiabilidade do Windows
            try
            {
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceId, PowerOnHours, Temperature, Wear, ReadErrorsUncorrected FROM MSFT_StorageReliabilityCounter")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]);
                        if (hw.DiscoIndice >= 0 && devId != hw.DiscoIndice.ToString()) continue;
                        long horas = -1; int temp = -1, wear = -1;
                        try { horas = Convert.ToInt64(mo["PowerOnHours"]); } catch { }
                        try { temp = Convert.ToInt32(mo["Temperature"]); } catch { }
                        try { wear = Convert.ToInt32(mo["Wear"]); } catch { }
                        if (horas > 0) r.HorasLigado = horas;
                        if (temp > 0 && temp < 100) r.TemperaturaC = temp;
                        if (wear >= 0 && wear <= 100) r.SaudeSSDPct = 100 - wear;
                        break;
                    }
                }
            }
            catch { }

            // Fonte 2: atributos SMART brutos (id 9 = horas, 194 = temperatura,
            // 5 = setores realocados, 231/233/177 = vida util do SSD)
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT VendorSpecific FROM MSStorageDriver_FailurePredictData")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        byte[] vs = (byte[])mo["VendorSpecific"];
                        if (vs == null || vs.Length < 362) continue;
                        for (int i = 0; i < 30; i++)
                        {
                            int b = 2 + i * 12;
                            if (b + 10 >= vs.Length) break;
                            byte id = vs[b];
                            if (id == 0) continue;
                            byte atual = vs[b + 3];
                            long raw = vs[b + 5] | ((long)vs[b + 6] << 8) |
                                       ((long)vs[b + 7] << 16) | ((long)vs[b + 8] << 24);
                            if (id == 9 && r.HorasLigado < 0 && raw > 0 && raw < 500000)
                                r.HorasLigado = raw;
                            else if (id == 194 && r.TemperaturaC < 0)
                            {
                                int t = vs[b + 5];
                                if (t > 0 && t < 100) r.TemperaturaC = t;
                            }
                            else if (id == 5)
                                r.SetoresRealocados = raw;
                            else if ((id == 231 || id == 233 || id == 177) && r.SaudeSSDPct < 0
                                     && atual > 0 && atual <= 100)
                                r.SaudeSSDPct = atual; // valor normalizado ~ % de vida restante
                        }
                        break; // primeiro disco com dados (maquinas-alvo tem 1 disco)
                    }
                }
            }
            catch { }

            if (r.SetoresRealocados < 0 && (r.HorasLigado >= 0 || r.TemperaturaC >= 0))
                r.SetoresRealocados = -1; // sem dado bruto; nao afirmar nada
        }

        // ------------------------------------------------------------
        // HDD: relatorio padrao do chkdsk em modo SOMENTE LEITURA
        // (sem /f - nao altera nada). Lista clusters/setores
        // defeituosos e problemas no sistema de arquivos.
        // ------------------------------------------------------------
        static void RodarChkdsk(DiagResult r, Action<string> log)
        {
            log("Executando 'chkdsk C:' em modo somente leitura (nao altera nada).");
            log("Em HDD isto pode levar varios minutos. Aguarde...");
            var saida = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("chkdsk.exe", "C:");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                try
                {
                    psi.StandardOutputEncoding = System.Text.Encoding.GetEncoding(
                        System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                }
                catch { }

                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object snd, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        string s = e.Data;
                        int cr = s.LastIndexOf('\r');
                        if (cr >= 0) s = s.Substring(cr + 1); // descarta linhas de progresso sobrescritas
                        string baixa = s.ToLowerInvariant();
                        if (baixa.Contains("por cento") || baixa.Contains("percent") || baixa.Contains("%")) return;
                        if (s.Trim().Length == 0) return;
                        lock (saida) saida.Add(s);
                        log("  " + s);
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    if (!p.WaitForExit(15 * 60 * 1000))
                    {
                        try { p.Kill(); } catch { }
                        log("chkdsk excedeu 15 minutos e foi interrompido; execute manualmente depois.");
                        return;
                    }
                }

                // Analise do resumo
                string tudo;
                lock (saida) tudo = string.Join("\n", saida).ToLowerInvariant();
                bool problemas = (tudo.Contains("encontrou problemas") || tudo.Contains("found problems"))
                                 && !tudo.Contains("nao encontrou problemas")
                                 && !tudo.Contains("não encontrou problemas")
                                 && !tudo.Contains("found no problems");
                long kbRuins = ExtrairKbDefeituosos(saida);

                if (kbRuins > 0)
                {
                    log("");
                    log(string.Format("*** ATENCAO: {0} KB em setores defeituosos. O disco tem dano fisico;", kbRuins));
                    log("*** mantenha backup em dia e planeje a troca por SSD.");
                    if (r.SaudeNivel < 1) r.SaudeNivel = 1;
                }
                if (problemas)
                {
                    log("");
                    log("*** O chkdsk encontrou problemas no sistema de arquivos.");
                    log("*** Recomendacao: executar 'chkdsk C: /f' (sera agendado para o proximo reinicio).");
                    log("*** Obs.: em disco em uso, o modo somente leitura pode apontar inconsistencias menores falsas.");
                    if (r.SaudeNivel < 1) r.SaudeNivel = 1;
                }
                if (kbRuins == 0 && !problemas)
                    log("Sistema de arquivos sem problemas e sem setores defeituosos relatados.");
            }
            catch (Exception ex)
            {
                log("Nao foi possivel executar o chkdsk: " + ex.Message);
            }
        }

        static long ExtrairKbDefeituosos(List<string> linhas)
        {
            // Procura a linha do resumo "... KB em setores defeituosos" (pt) /
            // "... KB in bad sectors" (en) e extrai o numero.
            foreach (string l in linhas)
            {
                string baixa = l.ToLowerInvariant();
                if (!(baixa.Contains("defeituoso") || baixa.Contains("bad sector") || baixa.Contains("setores inv"))) continue;
                string digitos = "";
                foreach (char c in l)
                {
                    if (char.IsDigit(c)) digitos += c;
                    else if (digitos.Length > 0 && (c == '.' || c == ',')) continue;
                    else if (digitos.Length > 0) break;
                }
                long v;
                if (long.TryParse(digitos, out v)) return v;
            }
            return 0;
        }

        // ------------------------------------------------------------
        // 3a) Benchmark de CPU: trabalho misto inteiro/ponto flutuante
        // em todas as threads por ~0,8s.
        // ------------------------------------------------------------
        static long _cpuSink; // impede o JIT de eliminar o loop

        static double BenchCpu(Action<string> log)
        {
            int nThreads = Environment.ProcessorCount;
            log(string.Format("CPU: medindo em {0} thread(s)...", nThreads));
            long totalIters = 0;
            object trava = new object();
            var pronto = new ManualResetEvent(false);
            var threads = new List<Thread>();
            var sw = new Stopwatch();

            for (int t = 0; t < nThreads; t++)
            {
                var th = new Thread(delegate()
                {
                    pronto.WaitOne();
                    long iters = 0;
                    long x = 12345;
                    double d = 1.0001;
                    var swLocal = Stopwatch.StartNew();
                    while (swLocal.ElapsedMilliseconds < 800)
                    {
                        for (int i = 0; i < 10000; i++)
                        {
                            x = x * 6364136223846793005L + 1442695040888963407L;
                            d = d * 1.0000001 + 0.000001;
                            if ((x & 0xFF) == 0) d += 0.5;
                        }
                        iters += 10000;
                    }
                    Interlocked.Add(ref _cpuSink, x + (long)d);
                    lock (trava) { totalIters += iters; }
                });
                th.IsBackground = true;
                th.Priority = ThreadPriority.AboveNormal;
                threads.Add(th);
                th.Start();
            }
            sw.Start();
            pronto.Set();
            foreach (var th in threads) th.Join();
            sw.Stop();

            // ~3 operacoes relevantes por iteracao
            double mops = totalIters * 3.0 / sw.Elapsed.TotalSeconds / 1e6;
            log(string.Format("CPU: {0:0} MOPS (todas as threads).", mops));
            return mops;
        }

        // ------------------------------------------------------------
        // 3b) Banda de memoria: copia blocos grandes.
        // ------------------------------------------------------------
        static double BenchRam(Action<string> log)
        {
            const int TAM = 64 * 1024 * 1024;
            byte[] src, dst;
            try { src = new byte[TAM]; dst = new byte[TAM]; }
            catch (OutOfMemoryException) { log("RAM: sem memoria para o teste de banda."); return 1000; }
            new Random(1).NextBytes(src);
            // aquecimento
            Buffer.BlockCopy(src, 0, dst, 0, TAM);
            var sw = Stopwatch.StartNew();
            int reps = 12;
            for (int i = 0; i < reps; i++) Buffer.BlockCopy(src, 0, dst, 0, TAM);
            sw.Stop();
            // cada copia le TAM e escreve TAM
            double mbs = (double)TAM * reps * 2 / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
            log(string.Format("RAM: banda de copia ~{0:0} MB/s.", mbs));
            return mbs;
        }

        // ------------------------------------------------------------
        // 3c) Benchmark de disco: escrita sequencial (write-through),
        // leitura sequencial e leitura aleatoria 4K sem cache
        // (FILE_FLAG_NO_BUFFERING) para nao mentir a favor do Windows.
        // ------------------------------------------------------------
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFilePointerEx(IntPtr hFile, long liDistanceToMove, IntPtr lpNewFilePointer, uint dwMoveMethod);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        const uint GENERIC_READ = 0x80000000;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        const uint MEM_COMMIT_RESERVE = 0x3000;
        const uint PAGE_READWRITE = 0x04;
        const uint MEM_RELEASE = 0x8000;

        static void BenchDisco(DiagResult r, Action<string> log)
        {
            const int TAM_MB = 128;
            const int CHUNK = 1 << 20; // 1 MB
            string caminho = Path.Combine(Path.GetTempPath(), "otimizador_bench.tmp");
            try
            {
                // --- escrita sequencial (write-through: vai ao disco de verdade)
                log(string.Format("Disco: escrevendo arquivo de teste de {0} MB...", TAM_MB));
                byte[] chunk = new byte[CHUNK];
                new Random(2).NextBytes(chunk);
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.None,
                    CHUNK, FileOptions.WriteThrough))
                {
                    for (int i = 0; i < TAM_MB; i++) fs.Write(chunk, 0, CHUNK);
                    fs.Flush(true);
                }
                sw.Stop();
                r.SeqWriteMBs = TAM_MB / sw.Elapsed.TotalSeconds;
                log(string.Format("Disco: escrita sequencial ~{0:0} MB/s.", r.SeqWriteMBs));

                // --- leitura sem cache do Windows
                IntPtr h = CreateFileW(caminho, GENERIC_READ, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
                if (h == new IntPtr(-1)) { log("Disco: leitura sem cache indisponivel; pulando."); return; }
                IntPtr buf = VirtualAlloc(IntPtr.Zero, (UIntPtr)CHUNK, MEM_COMMIT_RESERVE, PAGE_READWRITE);
                try
                {
                    // sequencial
                    sw.Restart();
                    uint lidos;
                    long totalLido = 0;
                    SetFilePointerEx(h, 0, IntPtr.Zero, 0);
                    for (int i = 0; i < TAM_MB; i++)
                    {
                        if (!ReadFile(h, buf, CHUNK, out lidos, IntPtr.Zero) || lidos == 0) break;
                        totalLido += lidos;
                    }
                    sw.Stop();
                    if (totalLido > 0)
                        r.SeqReadMBs = totalLido / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
                    log(string.Format("Disco: leitura sequencial ~{0:0} MB/s.", r.SeqReadMBs));

                    // aleatoria 4K (o que separa HDD de SSD no uso real)
                    log("Disco: leitura aleatoria 4K (e aqui que HDD e SSD se separam)...");
                    var rnd = new Random(3);
                    long tamArq = (long)TAM_MB * CHUNK;
                    int nLeituras = 192;
                    sw.Restart();
                    int okCount = 0;
                    for (int i = 0; i < nLeituras; i++)
                    {
                        long off = ((long)(rnd.NextDouble() * (tamArq - 4096))) & ~4095L;
                        SetFilePointerEx(h, off, IntPtr.Zero, 0);
                        if (ReadFile(h, buf, 4096, out lidos, IntPtr.Zero) && lidos > 0) okCount++;
                    }
                    sw.Stop();
                    if (okCount > 0)
                        r.Rand4kIops = okCount / sw.Elapsed.TotalSeconds;
                    log(string.Format("Disco: ~{0:0} operacoes 4K/s.", r.Rand4kIops));
                }
                finally
                {
                    if (buf != IntPtr.Zero) VirtualFree(buf, UIntPtr.Zero, MEM_RELEASE);
                    CloseHandle(h);
                }
            }
            catch (Exception ex)
            {
                log("Disco: benchmark incompleto (" + ex.Message + ")");
            }
            finally
            {
                try { File.Delete(caminho); } catch { }
            }
        }

        // ------------------------------------------------------------
        // 4) Notas de 1 a 10 (escala logaritmica: cada dobra de
        // desempenho vale pontos fixos). Pesos: disco 40%, CPU 35%,
        // RAM 25% - em maquina fraca o disco domina a experiencia.
        // ------------------------------------------------------------
        static double Log2(double x) { return Math.Log(x) / Math.Log(2.0); }
        static double Clamp(double v) { return Math.Max(1.0, Math.Min(10.0, v)); }

        static void Pontuar(DiagResult r)
        {
            // Referencias calibradas: um desktop i5-6500 + SSD SATA fica ~7;
            // Celeron 5205U + HDD ~3.5; maquina moderna com NVMe ~9-10.
            r.NotaCpu = Clamp(5.0 + 1.5 * Log2(Math.Max(1, r.CpuMops) / 1800.0));
            r.NotaRam = Clamp(5.0 + 1.5 * Log2(Math.Max(1, r.RamMBs) / 6000.0));

            double seqMedia = Math.Max(1, (r.SeqWriteMBs + r.SeqReadMBs) / 2.0);
            double notaSeq = 3.0 + 1.7 * Log2(seqMedia / 90.0);
            double notaRand = 2.0 + 1.2 * Log2(Math.Max(1, r.Rand4kIops) / 100.0);
            r.NotaDisco = Clamp(0.55 * Clamp(notaSeq) + 0.45 * Clamp(notaRand));

            r.NotaFinal = Clamp(0.35 * r.NotaCpu + 0.25 * r.NotaRam + 0.40 * r.NotaDisco);
            if (r.RamErros > 0) r.NotaFinal = Math.Min(r.NotaFinal, 2.0);

            if (r.RamErros > 0)
                r.Veredito = "ERROS DE MEMORIA DETECTADOS - resolva isso antes de qualquer outra coisa (teste os modulos, limpe contatos, substitua se preciso).";
            else if (r.SaudeNivel == 2)
                r.Veredito = "DISCO EM RISCO - faca backup ja e substitua o disco. Nota de desempenho fica em segundo plano.";
            else if (r.NotaFinal < 3)
                r.Veredito = "Critica: adequada apenas a tarefas muito basicas. Upgrade de SSD e/ou RAM e fortemente recomendado.";
            else if (r.NotaFinal < 5)
                r.Veredito = "Limitada: da conta de navegacao leve e documentos, com paciencia. SSD (se ainda for HDD) transformaria esta maquina.";
            else if (r.NotaFinal < 6.5)
                r.Veredito = "Razoavel: uso basico do dia a dia flui bem apos a otimizacao.";
            else if (r.NotaFinal < 8)
                r.Veredito = "Boa: responde bem para produtividade e multitarefa moderada.";
            else
                r.Veredito = "Excelente: maquina rapida para qualquer uso comum.";
        }

        public static string Relatorio(DiagResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format("NOTA FINAL: {0:0.0} / 10", r.NotaFinal));
            sb.AppendLine(string.Format("  CPU: {0:0.0}  |  RAM: {1:0.0}  |  Disco: {2:0.0}", r.NotaCpu, r.NotaRam, r.NotaDisco));
            sb.AppendLine(r.Veredito);
            return sb.ToString();
        }
    }
}
