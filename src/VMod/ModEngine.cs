using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VMod;

internal sealed class ModEngine
{
    private readonly PackageRepository _repository;
    public ModEngine(PackageRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<ModView> BuildViews(IEnumerable<ModPackage> packages, string root, InstallTarget target)
    {
        return packages
            .Where(package => !package.IsComponent && (target == InstallTarget.Game || package.InstallOnServer))
            .Select(package => new ModView { Package = package, State = GetState(package, root), Target = target })
            .OrderBy(view => StateOrder(view.State))
            .ThenBy(view => view.Package.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public DashboardStats GetStats(IEnumerable<ModView> views)
    {
        DashboardStats stats = new();
        foreach (ModView view in views)
        {
            if (view.State == ModState.Installed) stats.Installed++;
            else if (view.State == ModState.Disabled) stats.Disabled++;
            else if (view.State == ModState.New) stats.New++;
        }
        return stats;
    }

    public ModState GetState(ModPackage package, string root)
    {
        if (!Directory.Exists(root))
            return package.IsCustom ? ModState.New : ModState.NotInstalled;
        string disabledRoot = DisabledRoot(root, package);
        if (Directory.Exists(disabledRoot) && Directory.EnumerateFiles(disabledRoot, "*", SearchOption.AllDirectories).Any())
            return ModState.Disabled;

        PackageReceipt? receipt = LoadReceipt(root, package);
        IEnumerable<string> relativePaths = receipt?.RelativePaths ?? GetMappedFiles(package);
        if (relativePaths.Any(relative => File.Exists(SafeDestination(root, relative))))
            return ModState.Installed;
        return package.IsCustom ? ModState.New : ModState.NotInstalled;
    }

    public OperationResult InstallCoreSet(IEnumerable<ModPackage> packages, string root, InstallTarget target, IProgress<string>? progress = null)
    {
        ValidateRoot(root, target);
        EnsureStopped(target);
        List<ModPackage> selected = packages
            .Where(package => package.IsComponent || (target == InstallTarget.Game || package.InstallOnServer))
            .ToList();
        return Install(selected, root, progress);
    }

    public OperationResult InstallNew(IEnumerable<ModPackage> packages, string root, InstallTarget target, IProgress<string>? progress = null)
    {
        ValidateRoot(root, target);
        EnsureStopped(target);
        List<ModPackage> selected = packages
            .Where(package => package.IsComponent || (package.IsCustom && GetState(package, root) == ModState.New))
            .Where(package => target == InstallTarget.Game || package.InstallOnServer)
            .ToList();
        return Install(selected, root, progress);
    }

    public OperationResult InstallOne(ModPackage package, IEnumerable<ModPackage> allPackages, string root, InstallTarget target, IProgress<string>? progress = null)
    {
        ValidateRoot(root, target);
        EnsureStopped(target);
        List<ModPackage> selected = allPackages.Where(item => item.IsComponent).ToList();
        selected.Add(package);
        return Install(selected.DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList(), root, progress);
    }

    public OperationResult SetEnabled(ModPackage package, string root, InstallTarget target, bool enabled)
    {
        ValidateRoot(root, target);
        EnsureStopped(target);
        return enabled ? Enable(package, root) : Disable(package, root);
    }

    public OperationResult Remove(ModPackage package, string root, InstallTarget target)
    {
        ValidateRoot(root, target);
        EnsureStopped(target);
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupRoot = Path.Combine(root, "BepInEx", "VMod", "Removed", timestamp, PackageRepository.SafeName(package.Id));
        PackageReceipt? receipt = LoadReceipt(root, package);
        List<string> paths = (receipt?.RelativePaths ?? GetMappedFiles(package)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int removed = 0;

        foreach (string relative in paths)
        {
            string active = SafeDestination(root, relative);
            if (File.Exists(active))
            {
                BackupAndDelete(active, SafeDestination(backupRoot, relative));
                removed++;
            }
            string disabled = SafeDestination(DisabledRoot(root, package), relative);
            if (File.Exists(disabled))
            {
                BackupAndDelete(disabled, SafeDestination(Path.Combine(backupRoot, "disabled"), relative));
                removed++;
            }
        }

        string receiptPath = ReceiptPath(root, package);
        if (File.Exists(receiptPath))
            File.Delete(receiptPath);
        DeleteEmptyParents(DisabledRoot(root, package), root);
        return new OperationResult { PackageCount = 1, FileCount = removed, BackupPath = removed > 0 ? backupRoot : null };
    }

    public void LaunchGame(string gameRoot)
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/892970") { UseShellExecute = true });
            return;
        }
        catch { }
        string executable = FirstExisting(Path.Combine(gameRoot, "valheim.exe"), Path.Combine(gameRoot, "valheim.x86_64"))
            ?? throw new FileNotFoundException("Не найден исполняемый файл Valheim.");
        Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = gameRoot, UseShellExecute = true });
    }

    public void LaunchServer(string serverRoot)
    {
        ValidateRoot(serverRoot, InstallTarget.Server);
        string? launcher = FirstExisting(
            Path.Combine(serverRoot, "start_headless_server.bat"),
            Path.Combine(serverRoot, "start_server_bepinex.sh"),
            Path.Combine(serverRoot, "valheim_server.exe"),
            Path.Combine(serverRoot, "valheim_server.x86_64"));
        if (launcher == null)
            throw new FileNotFoundException("Не найден файл запуска выделенного сервера.");
        Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = serverRoot, UseShellExecute = true });
    }

    public static void ValidateRoot(string root, InstallTarget target)
    {
        if (!SteamLocator.IsValid(root, target))
        {
            string expected = target == InstallTarget.Game ? "valheim.exe или valheim.x86_64" : "valheim_server.exe или valheim_server.x86_64";
            throw new DirectoryNotFoundException("В выбранной папке не найден " + expected + ".");
        }
        string probe = Path.Combine(root, ".vmod-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe)) { }
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private OperationResult Install(IReadOnlyList<ModPackage> packages, string root, IProgress<string>? progress)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupRoot = Path.Combine(root, "BepInEx", "VMod", "Backups", timestamp);
        Dictionary<string, string> backups = new(StringComparer.OrdinalIgnoreCase);
        List<string> created = new();
        int files = 0;

        try
        {
            foreach (ModPackage package in packages.OrderByDescending(item => item.IsBepInEx).ThenByDescending(item => item.IsComponent))
            {
                progress?.Report("Установка: " + package.DisplayName);
                List<string> receiptPaths = new();
                using Stream source = _repository.Open(package);
                using ZipArchive archive = new(source, ZipArchiveMode.Read, false);
                PackageRepository.ValidateArchive(archive);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name))
                        continue;
                    string? relative = MapEntry(package, entry.FullName);
                    if (relative == null)
                        continue;
                    string destination = SafeDestination(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (File.Exists(destination) && !backups.ContainsKey(destination))
                    {
                        string backup = SafeDestination(backupRoot, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(destination, backup, true);
                        backups[destination] = backup;
                    }
                    else if (!File.Exists(destination))
                    {
                        created.Add(destination);
                    }
                    using Stream input = entry.Open();
                    using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                    receiptPaths.Add(relative);
                    files++;
                }
                SaveReceipt(root, package, new PackageReceipt
                {
                    PackageId = package.Id,
                    Version = package.Version,
                    InstalledAtUtc = DateTime.UtcNow,
                    Disabled = false,
                    RelativePaths = receiptPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                });
            }
            return new OperationResult { PackageCount = packages.Count, FileCount = files, BackupPath = backups.Count > 0 ? backupRoot : null };
        }
        catch
        {
            foreach ((string destination, string backup) in backups)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(backup, destination, true); } catch { }
            }
            foreach (string path in created.AsEnumerable().Reverse())
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            throw;
        }
    }

    private OperationResult Disable(ModPackage package, string root)
    {
        PackageReceipt receipt = LoadReceipt(root, package) ?? new PackageReceipt
        {
            PackageId = package.Id,
            Version = package.Version,
            InstalledAtUtc = DateTime.UtcNow,
            RelativePaths = GetMappedFiles(package)
        };
        int moved = 0;
        foreach (string relative in receipt.RelativePaths)
        {
            string source = SafeDestination(root, relative);
            if (!File.Exists(source))
                continue;
            string destination = SafeDestination(DisabledRoot(root, package), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, true);
            moved++;
            DeleteEmptyParents(Path.GetDirectoryName(source), root);
        }
        receipt.Disabled = true;
        SaveReceipt(root, package, receipt);
        return new OperationResult { PackageCount = 1, FileCount = moved };
    }

    private OperationResult Enable(ModPackage package, string root)
    {
        string disabledRoot = DisabledRoot(root, package);
        if (!Directory.Exists(disabledRoot))
            return new OperationResult { PackageCount = 1 };
        string backupRoot = Path.Combine(root, "BepInEx", "VMod", "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"), "enable", package.Id);
        int moved = 0;
        foreach (string source in Directory.EnumerateFiles(disabledRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            string relative = Path.GetRelativePath(disabledRoot, source);
            string destination = SafeDestination(root, relative);
            if (File.Exists(destination))
            {
                string backup = SafeDestination(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, true);
            moved++;
        }
        DeleteEmptyParents(disabledRoot, root);
        PackageReceipt receipt = LoadReceipt(root, package) ?? new PackageReceipt
        {
            PackageId = package.Id,
            Version = package.Version,
            InstalledAtUtc = DateTime.UtcNow,
            RelativePaths = GetMappedFiles(package)
        };
        receipt.Disabled = false;
        SaveReceipt(root, package, receipt);
        return new OperationResult { PackageCount = 1, FileCount = moved, BackupPath = Directory.Exists(backupRoot) ? backupRoot : null };
    }

    private List<string> GetMappedFiles(ModPackage package)
    {
        List<string> paths = new();
        using Stream source = _repository.Open(package);
        using ZipArchive archive = new(source, ZipArchiveMode.Read, false);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (String.IsNullOrEmpty(entry.Name))
                continue;
            string? mapped = MapEntry(package, entry.FullName);
            if (mapped != null)
                paths.Add(mapped);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? MapEntry(ModPackage package, string archivePath)
    {
        string path = archivePath.Replace('\\', '/').TrimStart('/');
        if (package.IsBepInEx)
        {
            const string prefix = "BepInExPack_Valheim/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                path = path[prefix.Length..];
            else if (path.Contains('/'))
                return null;
            if (IsPackagingFile(path))
                return null;
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
        if (IsPackagingFile(path))
            return null;
        if (path.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
            return path.Replace('/', Path.DirectorySeparatorChar);
        if (path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase))
            return ("BepInEx/" + path).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine("BepInEx", "plugins", PackageRepository.SafeName(package.Name), path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsPackagingFile(string path)
    {
        string name = Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
        return String.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
               String.Equals(name, "icon.png", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase);
    }

    private static PackageReceipt? LoadReceipt(string root, ModPackage package)
    {
        try
        {
            string path = ReceiptPath(root, package);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), VModJsonContext.Default.PackageReceipt) : null;
        }
        catch { return null; }
    }

    private static void SaveReceipt(string root, ModPackage package, PackageReceipt receipt)
    {
        string path = ReceiptPath(root, package);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(receipt, VModJsonContext.Default.PackageReceipt));
        File.Move(temp, path, true);
    }

    private static string ReceiptPath(string root, ModPackage package)
        => Path.Combine(root, "BepInEx", "VMod", "Receipts", PackageRepository.SafeName(package.Id) + ".json");

    private static string DisabledRoot(string root, ModPackage package)
        => Path.Combine(root, "BepInEx", "VMod", "Disabled", PackageRepository.SafeName(package.Id));

    private static string SafeDestination(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string destination = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Небезопасный путь: " + relative);
        return destination;
    }

    private static void BackupAndDelete(string source, string backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(source, backup, true);
        File.Delete(source);
    }

    private static void DeleteEmptyParents(string? directory, string stopRoot)
    {
        if (String.IsNullOrWhiteSpace(directory))
            return;
        string root = Path.GetFullPath(stopRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!String.IsNullOrWhiteSpace(current) && !String.Equals(current, root, StringComparison.OrdinalIgnoreCase) && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    break;
                Directory.Delete(current);
            }
            catch { break; }
            current = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureStopped(InstallTarget target)
    {
        string processName = target == InstallTarget.Game ? "valheim" : "valheim_server";
        if (Process.GetProcessesByName(processName).Length > 0)
            throw new InvalidOperationException(target == InstallTarget.Game ? "Закройте Valheim перед изменением модов." : "Остановите выделенный сервер перед изменением модов.");
    }

    private static int StateOrder(ModState state) => state switch
    {
        ModState.New => 0,
        ModState.Disabled => 1,
        ModState.Installed => 2,
        _ => 3
    };

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
}
