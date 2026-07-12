using System.IO;
using System.Net.Http;

namespace Aqroflow;

public record ModelInfo(string Id, string Display, string FileName, bool Multilingual, string SizeLabel);

/// <summary>
/// Selectable whisper.cpp models. Downloaded on demand to %APPDATA%\Aqroflow\models.
/// Quantized (q5_1) to stay small enough for low-end machines.
/// </summary>
public static class ModelStore
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static readonly ModelInfo[] Catalog =
    {
        new("tiny.en",  "Tiny · English · fastest",       "ggml-tiny.en-q5_1.bin",  false, "~32 MB"),
        new("base.en",  "Base · English · balanced",      "ggml-base.en-q5_1.bin",  false, "~60 MB"),
        new("small.en", "Small · English · most accurate","ggml-small.en-q5_1.bin", false, "~190 MB"),
        new("medium.en","Medium · English · high quality","ggml-medium.en-q5_0.bin",false, "~550 MB"),
        new("tiny",     "Tiny · multilingual",            "ggml-tiny-q5_1.bin",     true,  "~32 MB"),
        new("base",     "Base · multilingual",            "ggml-base-q5_1.bin",     true,  "~60 MB"),
        new("small",    "Small · multilingual · best",    "ggml-small-q5_1.bin",    true,  "~190 MB"),
        new("medium",   "Medium · multilingual · quality","ggml-medium-q5_0.bin",   true,  "~550 MB"),
    };

    public static ModelInfo GetById(string id) =>
        Catalog.FirstOrDefault(m => m.Id == id) ?? Catalog[0];

    public static string ModelDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aqroflow", "models");

    public static string PathFor(ModelInfo m) => Path.Combine(ModelDir, m.FileName);

    public static bool IsDownloaded(ModelInfo m)
    {
        var p = PathFor(m);
        return File.Exists(p) && new FileInfo(p).Length > 10_000_000;
    }

    public static async Task EnsureAsync(ModelInfo model, IProgress<int> progress)
    {
        if (IsDownloaded(model)) return;

        Directory.CreateDirectory(ModelDir);
        using var http = new HttpClient();
        using var resp = await http.GetAsync(BaseUrl + model.FileName, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1;
        string dst = PathFor(model);
        string tmp = dst + ".tmp";
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var fs = File.Create(tmp))
        {
            var buf = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buf)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report((int)(done * 100 / total));
            }
        }
        File.Move(tmp, dst, overwrite: true);
    }
}
