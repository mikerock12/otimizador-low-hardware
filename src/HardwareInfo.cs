using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace OtimizadorWin10
{
    public enum DiskKind { HDD, SSD, Desconhecido }

    public class HardwareInfo
    {
        public string CpuName = "Desconhecido";
        public int CpuCores = 1;
        public int CpuThreads = 1;
        public int CpuMhz = 0;
        public int RamMB = 0;
        public DiskKind Disco = DiskKind.Desconhecido;
        public bool DiscoDetectadoComCerteza = false;
        public string DiscoModelo = "";
        public int DiscoGB = 0;
        public int DiscoIndice = -1;   // indice fisico do disco de sistema (para SMART)
        public bool TemBateria = false;
        public string WindowsNome = "Windows";
        public string WindowsVersao = "";
        public int WindowsBuild = 0;
        public bool EhWindows10 = false;
        public bool EhWindows11 = false;

        public bool SistemaSuportado { get { return EhWindows10 || EhWindows11; } }

        public double RamGB { get { return Math.Round(RamMB / 1024.0, 1); } }

        // Perfil recomendado: 1=Leve, 2=Master, 3=Ultra
        public int TierRecomendado()
        {
            int score = 0;
            if (Disco != DiskKind.SSD) score += 2;          // HDD (ou incerto) pesa muito
            if (RamMB <= 2560) score += 2;                   // 2GB
            else if (RamMB <= 4608) score += 1;              // 3-4GB
            if (CpuCores <= 2 && CpuMhz > 0 && CpuMhz < 2600) score += 1; // dual-core fraco
            if (score >= 4) return 3;
            if (score >= 2) return 2;
            return 1;
        }

        public static HardwareInfo Detectar()
        {
            var hw = new HardwareInfo();
            try { DetectarCpu(hw); } catch { }
            try { DetectarRam(hw); } catch { }
            try { DetectarDisco(hw); } catch { }
            try { DetectarBateria(hw); } catch { }
            try { DetectarWindows(hw); } catch { }
            return hw;
        }

        static void DetectarCpu(HardwareInfo hw)
        {
            using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    hw.CpuName = Convert.ToString(mo["Name"]).Trim();
                    hw.CpuCores = SafeInt(mo["NumberOfCores"], 1);
                    hw.CpuThreads = SafeInt(mo["NumberOfLogicalProcessors"], 1);
                    hw.CpuMhz = SafeInt(mo["MaxClockSpeed"], 0);
                    break;
                }
            }
        }

        static void DetectarRam(HardwareInfo hw)
        {
            using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    ulong bytes = Convert.ToUInt64(mo["TotalPhysicalMemory"]);
                    hw.RamMB = (int)(bytes / (1024UL * 1024UL));
                    break;
                }
            }
        }

        static void DetectarDisco(HardwareInfo hw)
        {
            // 1) Descobrir o indice do disco fisico que hospeda o C:
            int indiceDiscoSistema = -1;
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='C:'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                {
                    foreach (ManagementObject part in s.Get())
                    {
                        // DeviceID ex.: "Disk #0, Partition #1"
                        string devId = Convert.ToString(part["DeviceID"]);
                        int pos = devId.IndexOf("Disk #");
                        if (pos >= 0)
                        {
                            string resto = devId.Substring(pos + 6);
                            int fim = resto.IndexOf(',');
                            if (fim > 0) resto = resto.Substring(0, fim);
                            int idx;
                            if (int.TryParse(resto.Trim(), out idx)) indiceDiscoSistema = idx;
                        }
                        break;
                    }
                }
            }
            catch { }
            hw.DiscoIndice = indiceDiscoSistema;

            // 2) Modelo e tamanho via Win32_DiskDrive
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Index, Model, Size FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        int idx = SafeInt(mo["Index"], -1);
                        if (indiceDiscoSistema >= 0 && idx != indiceDiscoSistema) continue;
                        hw.DiscoModelo = Convert.ToString(mo["Model"]).Trim();
                        try { hw.DiscoGB = (int)(Convert.ToUInt64(mo["Size"]) / (1000UL * 1000UL * 1000UL)); }
                        catch { }
                        break;
                    }
                }
            }
            catch { }

            // 3) Tipo de midia via MSFT_PhysicalDisk (namespace de Storage, Win8+)
            try
            {
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceId, MediaType, SpindleSpeed FROM MSFT_PhysicalDisk")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]);
                        if (indiceDiscoSistema >= 0 && devId != indiceDiscoSistema.ToString()) continue;
                        int mediaType = SafeInt(mo["MediaType"], 0); // 3=HDD, 4=SSD
                        if (mediaType == 4) { hw.Disco = DiskKind.SSD; hw.DiscoDetectadoComCerteza = true; }
                        else if (mediaType == 3) { hw.Disco = DiskKind.HDD; hw.DiscoDetectadoComCerteza = true; }
                        else
                        {
                            // MediaType nao especificado: decide pela rotacao.
                            // SpindleSpeed 0 = sem partes moveis (SSD); >0 = pratos (HDD).
                            ulong spindle = ulong.MaxValue;
                            try { spindle = Convert.ToUInt64(mo["SpindleSpeed"]); } catch { }
                            if (spindle == 0) { hw.Disco = DiskKind.SSD; hw.DiscoDetectadoComCerteza = true; }
                            else if (spindle > 0 && spindle < uint.MaxValue) { hw.Disco = DiskKind.HDD; hw.DiscoDetectadoComCerteza = true; }
                        }
                        break;
                    }
                }
            }
            catch { }

            // 4) Fallback: heuristica pelo nome do modelo
            if (hw.Disco == DiskKind.Desconhecido)
            {
                string m = (hw.DiscoModelo ?? "").ToUpperInvariant();
                if (m.Contains("SSD") || m.Contains("NVME")) hw.Disco = DiskKind.SSD;
                else if (m.Length > 0) hw.Disco = DiskKind.HDD; // maioria dos discos antigos sem marca SSD
            }
        }

        static void DetectarBateria(HardwareInfo hw)
        {
            using (var s = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_Battery"))
            {
                foreach (ManagementObject mo in s.Get()) { hw.TemBateria = true; break; }
            }
        }

        static void DetectarWindows(HardwareInfo hw)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (k != null)
                {
                    hw.WindowsNome = Convert.ToString(k.GetValue("ProductName", "Windows"));
                    object dv = k.GetValue("DisplayVersion");
                    if (dv == null) dv = k.GetValue("ReleaseId");
                    hw.WindowsVersao = Convert.ToString(dv ?? "");
                    int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber", "0")), out hw.WindowsBuild);
                    object major = k.GetValue("CurrentMajorVersionNumber");
                    bool nucleo10 = major != null && Convert.ToInt32(major) == 10;
                    // O registro do Windows 11 ainda informa ProductName "Windows 10 ...";
                    // a distincao confiavel e o build (>= 22000 = Windows 11).
                    hw.EhWindows11 = nucleo10 && hw.WindowsBuild >= 22000;
                    hw.EhWindows10 = nucleo10 && !hw.EhWindows11
                        && hw.WindowsNome.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hw.EhWindows11)
                        hw.WindowsNome = hw.WindowsNome.Replace("Windows 10", "Windows 11");
                }
            }
        }

        static int SafeInt(object o, int padrao)
        {
            try { return o == null ? padrao : Convert.ToInt32(o); }
            catch { return padrao; }
        }

        public string Resumo()
        {
            return string.Format(
                "CPU: {0} ({1} nucleo(s)/{2} thread(s), {3:0.0} GHz)\r\nRAM: {4:0.0} GB\r\nDisco: {5} {6} ({7} GB) - {8}\r\nSistema: {9} {10}\r\nBateria: {11}",
                CpuName, CpuCores, CpuThreads, CpuMhz / 1000.0,
                RamGB,
                Disco == DiskKind.SSD ? "SSD" : (Disco == DiskKind.HDD ? "HDD" : "?"),
                DiscoModelo, DiscoGB,
                DiscoDetectadoComCerteza ? "detectado" : "estimado",
                WindowsNome, WindowsVersao,
                TemBateria ? "sim (notebook)" : "nao detectada");
        }
    }
}
