using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace VMod;

internal static class UpdateService
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/FULLMEGO/VALHEIM/main/update.txt";
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateManifest> FetchAsync(CancellationToken cancellationToken = default)
    {
        string url = ManifestUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string text = await Client.GetStringAsync(url, cancellationToken);
        return Parse(text);
    }

    public static UpdateManifest Parse(string text)
    {
        Dictionary<string, string> values = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("version", out string? versionText) || !Version.TryParse(versionText, out Version? version))
            throw new InvalidDataException("В манифесте обновления отсутствует корректная версия.");
        if (!values.TryGetValue("url", out string? downloadUrl) || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("В манифесте обновления отсутствует безопасный HTTPS-адрес.");
        if (!values.TryGetValue("sha256", out string? sha256) || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("В манифесте обновления отсутствует корректная SHA-256 сумма.");
        return new UpdateManifest { Version = version, Url = uri.AbsoluteUri, Sha256 = sha256.ToUpperInvariant(), Notes = values.GetValueOrDefault("notes", "Доступна новая версия VMod.") };
    }

    public static async Task<string> DownloadAndVerifyAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        string destination = Path.Combine(Path.GetTempPath(), "VMod_Update_" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, cancellationToken);
            await using FileStream verificationStream = File.OpenRead(destination);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(verificationStream, cancellationToken));
            if (!String.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма обновления не совпала. Файл удалён.");
            return destination;
        }
        catch
        {
            try { if (File.Exists(destination)) File.Delete(destination); } catch { }
            throw;
        }
    }

    public static void StartReplacement(string downloaded)
    {
        string current = Environment.ProcessPath ?? Application.ExecutablePath;
        Process.Start(new ProcessStartInfo(downloaded)
        {
            UseShellExecute = true,
            Arguments = $"--apply-update {Quote(current)} {Environment.ProcessId}"
        });
    }

    public static bool HandleUpdateArguments(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--apply-update" && Int32.TryParse(args[2], out int oldPid))
        {
            WaitForExit(oldPid);
            string target = Path.GetFullPath(args[1]);
            string source = Environment.ProcessPath ?? Application.ExecutablePath;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = $"--finish-update {Quote(source)} {Environment.ProcessId}"
            });
            return true;
        }
        if (args.Length >= 3 && args[0] == "--finish-update" && Int32.TryParse(args[2], out int helperPid))
        {
            WaitForExit(helperPid);
            try { if (File.Exists(args[1])) File.Delete(args[1]); } catch { }
        }
        return false;
    }

    private static void WaitForExit(int pid)
    {
        try { using Process process = Process.GetProcessById(pid); process.WaitForExit(30_000); } catch { }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VMod/2.0");
        return client;
    }
}
