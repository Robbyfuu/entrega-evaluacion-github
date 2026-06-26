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
            // Forzar SIEMPRE la extension .pdf: nunca confiar en storagePath para
            // decidir con que se abre el archivo (UseShellExecute resuelve por
            // extension). Asi un objeto .exe/.bat/.cmd no puede shell-ejecutarse.
            var safePdf = Path.GetFileNameWithoutExtension(safe) + ".pdf";
            var local = Path.Combine(Dir, safePdf);

            var url = $"{Config.SupabaseUrl}/storage/v1/object/exam-pdfs/{Uri.EscapeDataString(storagePath)}";
            var bytes = await Http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(local, bytes);

            // Validar MAGIC BYTES del contenido descargado: un PDF real empieza con
            // "%PDF-" (25 50 44 46 2D). Si no coincide, NO abrir: borrar el archivo
            // y devolver null. Esto bloquea el RCE distribuido (sec-client-08) donde
            // un objeto no-PDF se shell-ejecutaria en TODOS los clientes de la sala.
            if (!HasPdfMagicBytes(bytes))
            {
                Debug.WriteLine(
                    $"[ExamPdf] contenido NO es PDF (magic bytes invalidos) para '{storagePath}'; " +
                    "se borra y se aborta apertura por seguridad (sec-client-08)");
                try { File.Delete(local); }
                catch (Exception delEx) { Debug.WriteLine($"[ExamPdf] borrado no-PDF fallo: {delEx.Message}"); }
                return null;
            }

            Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
            return local;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExamPdf] descarga/abrir fallo: {ex.Message}");
            return null;
        }
    }

    // Magic bytes de un PDF: "%PDF-" => 25 50 44 46 2D.
    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46, 0x2D };

    /// <summary>
    /// True si el contenido empieza con la firma "%PDF-". Defensa contra
    /// shell-execute de objetos no-PDF (RCE distribuido, sec-client-08).
    /// </summary>
    private static bool HasPdfMagicBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length < PdfMagic.Length) return false;
        for (var i = 0; i < PdfMagic.Length; i++)
            if (bytes[i] != PdfMagic[i]) return false;
        return true;
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
