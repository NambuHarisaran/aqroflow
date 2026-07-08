using System.IO;
using System.Net.Http;

namespace Aqroflow;

/// <summary>
/// Downloads the whisper model to %APPDATA%\Aqroflow\models on first run.
/// tiny.en q5_1 ≈ 32MB — small enough for low-end machines.
/// </summary>
public static class ModelStore
{
    private const string ModelFileName = "ggml-tiny.en-q5_1.bin";
    private const string ModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelFileName;

    public static string ModelDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "models");

    public static string ModelPath => Path.Combine(ModelDir, ModelFileName);

    public static async Task EnsureAsync(IProgress<int> progress)
    {
        // a valid tiny q5_1 model is ~32MB; anything tiny is a broken partial download
        if (File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 10_000_000)
            return;

        Directory.CreateDirectory(ModelDir);
        using var http = new HttpClient();
        using var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1;
        string tmp = ModelPath + ".tmp";
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report((int)(done * 100 / total));
            }
        }
        File.Move(tmp, ModelPath, overwrite: true);
    }
}
