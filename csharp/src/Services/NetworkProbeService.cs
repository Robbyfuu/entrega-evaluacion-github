using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Deteccion (NO bloqueo, NO contenido) de contacto con endpoints de Copilot,
/// sin privilegios de admin. Dos señales complementarias:
///   1) Cache DNS de Windows (ipconfig /displaydns): si aparece un FQDN de
///      Copilot, algo lo resolvio en este PC. Nivel maquina, locale-independiente
///      (match por substring sobre la salida cruda).
///   2) Tabla TCP por proceso (GetExtendedTcpTable, iphlpapi): conexiones
///      ESTABLISHED a :443 cuyo IP remoto matchea un IP resuelto de un FQDN de
///      Copilot, con el PROCESO dueño. IP de CDN compartido => difuso, por eso
///      es señal corroborativa, no prueba.
///
/// Es EVIDENCIA para revision (evento ai_endpoint_contacted), nunca veredicto ni
/// lockdown automatico: la extension puede llamar/resolver aunque este apagada.
/// </summary>
public static class NetworkProbeService
{
    // FQDN ESPECIFICOS de Copilot. NO se incluyen github.com / api.github.com
    // (compartidos con el flujo normal de entrega -> falsos positivos).
    private static readonly string[] CopilotHosts =
    {
        "githubcopilot.com", // matchea *.githubcopilot.com por substring
        "copilot-proxy.githubusercontent.com",
        "copilot-telemetry.githubusercontent.com",
        "origin-tracker.githubusercontent.com",
        "default.exp-tas.com",
    };

    // FQDN resolvibles para mapear IP->host en la sonda TCP.
    private static readonly string[] ResolvableHosts =
    {
        "api.githubcopilot.com",
        "copilot-proxy.githubusercontent.com",
        "copilot-telemetry.githubusercontent.com",
        "default.exp-tas.com",
    };

    private static readonly string[] EditorProcessHints =
    {
        "code", "code - insiders", "codium", "vscodium", "node",
    };

    public sealed class Finding
    {
        public string Host { get; init; } = "";
        public string Source { get; init; } = ""; // "dns" | "tcp"
        public string? Process { get; init; }
        public string? RemoteIp { get; init; }

        public string Detail => $"{Source}{(Process != null ? ":" + Process : "")}{(RemoteIp != null ? ":" + RemoteIp : "")}";
    }

    /// <summary>Corre ambas sondas. Best-effort: nunca lanza.</summary>
    public static List<Finding> Probe()
    {
        var findings = new List<Finding>();
        try { findings.AddRange(ProbeDns()); }
        catch (Exception ex) { Debug.WriteLine($"[NetProbe] DNS fallo: {ex.Message}"); }
        try { findings.AddRange(ProbeTcp()); }
        catch (Exception ex) { Debug.WriteLine($"[NetProbe] TCP fallo: {ex.Message}"); }
        return findings;
    }

    // ===================== DNS cache =====================

    private static IEnumerable<Finding> ProbeDns()
    {
        var dump = RunDisplayDns();
        if (string.IsNullOrEmpty(dump)) yield break;
        foreach (var host in CopilotHosts)
        {
            if (dump.Contains(host, StringComparison.OrdinalIgnoreCase))
                yield return new Finding { Host = host, Source = "dns" };
        }
    }

    private static string RunDisplayDns()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/displaydns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetProbe] displaydns fallo: {ex.Message}");
            return "";
        }
    }

    // ===================== TCP table por proceso =====================

    private static IEnumerable<Finding> ProbeTcp()
    {
        // Resolver FQDN de Copilot -> IPs (la resolucion DNS suele andar aunque el
        // proxy SoftLock bloquee HTTP). Mapa IP remoto -> host.
        var ipToHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in ResolvableHosts)
        {
            try
            {
                foreach (var addr in Dns.GetHostAddresses(host))
                    ipToHost[addr.ToString()] = host;
            }
            catch (Exception ex) { Debug.WriteLine($"[NetProbe] resolve {host}: {ex.Message}"); }
        }
        if (ipToHost.Count == 0) yield break;

        foreach (var row in GetTcpRows())
        {
            if (row.State != MibTcpStateEstab) continue;
            if (row.RemotePort != 443) continue;
            if (!ipToHost.TryGetValue(row.RemoteIp, out var host)) continue;

            string? proc = null;
            try { proc = Process.GetProcessById((int)row.OwningPid).ProcessName; }
            catch { /* el proceso ya murio */ }

            // Reportamos siempre el contacto a un IP de Copilot; marcamos si el
            // proceso parece un editor (mayor relevancia).
            yield return new Finding
            {
                Host = host,
                Source = proc != null && IsEditor(proc) ? "tcp-editor" : "tcp",
                Process = proc,
                RemoteIp = row.RemoteIp,
            };
        }
    }

    private static bool IsEditor(string proc)
    {
        var p = proc.ToLowerInvariant();
        foreach (var hint in EditorProcessHints)
            if (p.Contains(hint)) return true;
        return false;
    }

    // ----- P/Invoke GetExtendedTcpTable (IPv4, owner PID) -----

    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint MibTcpStateEstab = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    private struct TcpRow
    {
        public uint State;
        public string RemoteIp;
        public int RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, int reserved);

    private static IEnumerable<TcpRow> GetTcpRows()
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        if (size <= 0) yield break;

        var rows = new List<TcpRow>();
        IntPtr table = Marshal.AllocHGlobal(size);
        try
        {
            uint ret = GetExtendedTcpTable(table, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (ret != 0) yield break;

            int numEntries = Marshal.ReadInt32(table);
            IntPtr rowPtr = IntPtr.Add(table, 4);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (int i = 0; i < numEntries; i++)
            {
                var r = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                var ip = new IPAddress(BitConverter.GetBytes(r.RemoteAddr)).ToString();
                int port = ((int)(r.RemotePort & 0xFF) << 8) | (int)((r.RemotePort >> 8) & 0xFF);
                rows.Add(new TcpRow { State = r.State, RemoteIp = ip, RemotePort = port, OwningPid = r.OwningPid });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }

        foreach (var r in rows) yield return r;
    }
}
