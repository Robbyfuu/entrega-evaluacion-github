using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Descarga el PDF de enunciado de una evaluacion desde Supabase Storage (bucket
/// privado 'exam-pdfs', lectura anon por RLS), lo abre con el visor por defecto y
/// lo BORRA al terminar la evaluacion para que no quede registro local.
///
/// "Borrar aunque este abierto": en Windows no se puede forzar el borrado de un
/// archivo con lock de otro proceso (algunos visores lo bloquean). Estrategia:
/// intentar borrar; si esta lockeado, anotarlo en una lista de borrado-pendiente
/// que se procesa al proximo arranque (cuando el visor ya no lo tiene abierto).
/// </summary>
public static class ExamPdfService
{
    private static readonly string Dir =
        Path.Combine(Path.GetTempPath(), "EntregaEvaluacion", "exam-pdf");

    private static readonly string PendingFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EntregaEvaluacion", "pending-pdf-delete.txt");

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        // Sin proxy: el SoftLock pone un proxy invalido en HKCU; la descarga del
        // enunciado debe ir directo igual que el resto de llamadas a Supabase.
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("apikey", Config.SupabaseAnonKey);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Config.SupabaseAnonKey}");
        return http;
    }

    /// <summary>
    /// Descarga el PDF (storagePath = ej "eval-49.pdf") a un temp y lo abre con el
    /// visor por defecto. Devuelve la ruta local o null si fallo.
    /// </summary>
    public static async Task<string?> DownloadAndOpenAsync(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return null;
        try
        {
            Directory.CreateDirectory(Dir);
            var safe = Path.GetFileName(storagePath); // evita traversal
            var local = Path.Combine(Dir, safe);

            var url = $"{Config.SupabaseUrl}/storage/v1/object/exam-pdfs/{Uri.EscapeDataString(storagePath)}";
            var bytes = await Http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(local, bytes);

            Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
            return local;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExamPdf] descarga/abrir fallo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Borra todos los PDFs de enunciado descargados. Lo que este lockeado se
    /// anota para borrar al proximo arranque. Llamar al ENTREGAR / cerrar sesion.
    /// </summary>
    public static void DeleteAllDownloaded()
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var file in Directory.GetFiles(Dir))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ExamPdf] borrado diferido {file}: {ex.Message}");
                    AddPending(file);
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ExamPdf] DeleteAllDownloaded fallo: {ex.Message}"); }
    }

    /// <summary>Procesa la lista de borrado-pendiente. Llamar en el arranque.</summary>
    public static void CleanupPendingOnStartup()
    {
        try
        {
            // Borrar cualquier resto del directorio temporal del arranque previo.
            if (Directory.Exists(Dir))
                foreach (var f in Directory.GetFiles(Dir))
                    try { File.Delete(f); } catch { /* sigue lockeado, raro al arrancar */ }

            if (!File.Exists(PendingFile)) return;
            foreach (var line in File.ReadAllLines(PendingFile))
            {
                var p = line.Trim();
                if (p.Length == 0) continue;
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            File.Delete(PendingFile);
        }
        catch (Exception ex) { Debug.WriteLine($"[ExamPdf] CleanupPendingOnStartup fallo: {ex.Message}"); }
    }

    private static void AddPending(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PendingFile)!);
            File.AppendAllText(PendingFile, path + Environment.NewLine);
        }
        catch (Exception ex) { Debug.WriteLine($"[ExamPdf] AddPending fallo: {ex.Message}"); }
    }
}
