using Microsoft.Win32;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VMod;

internal static class VModPaths
{
    public static readonly string Root = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VMOD_DATA_ROOT"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VMod")
        : Path.GetFullPath(Environment.GetEnvironmentVariable("VMOD_DATA_ROOT")!);
    public static readonly string Library = Path.Combine(Root, "Library");
    public static readonly string Archive = Path.Combine(Library, "Archive");
    public static readonly string Settings = Path.Combine(Root, "settings.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Library);
        Directory.CreateDirectory(Archive);
    }
}

internal sealed class SettingsStore
{
    public AppSettings Load()
    {
        VModPaths.Ensure();
        try
        {
            if (File.Exists(VModPaths.Settings))
                return JsonSerializer.Deserialize(File.ReadAllText(VModPaths.Settings), VModJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        VModPaths.Ensure();
        string temp = VModPaths.Settings + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, VModJsonContext.Default.AppSettings));
        File.Move(temp, VModPaths.Settings, true);
    }
}

internal sealed class PackageRepository
{
    private static readonly Assembly AppAssembly = Assembly.GetExecutingAssembly();

    public IReadOnlyList<ModPackage> LoadAll()
    {
        VModPaths.Ensure();
        Dictionary<string, ModPackage> packages = BuiltInPackages()
            .ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(VModPaths.Library, "*.zip", SearchOption.TopDirectoryOnly))
        {
            try
            {
                ModPackage custom = ReadPackage(file);
                custom.IsCustom = true;
                custom.FilePath = file;
                if (packages.TryGetValue(custom.Id, out ModPackage? existing))
                {
                    if (CompareVersions(custom.Version, existing.Version) >= 0)
                        packages[custom.Id] = custom;
                }
                else
                {
                    packages.Add(custom.Id, custom);
                }
            }
            catch
            {
                // A broken archive stays in the library for manual inspection but is not loaded.
            }
        }

        return packages.Values
            .OrderBy(package => package.IsComponent)
            .ThenBy(package => package.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ModPackage> Import(IEnumerable<string> files)
    {
        VModPaths.Ensure();
        List<ModPackage> imported = new();
        foreach (string source in files.Where(path => String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)))
        {
            FileInfo info = new(source);
            if (!info.Exists)
                continue;
            if (info.Length > 256L * 1024 * 1024)
                throw new InvalidDataException($"Архив {info.Name} превышает ограничение 256 МБ.");

            ModPackage package = ReadPackage(source);
            package.IsCustom = true;
            string safeName = SafeName(package.Name) + "-" + SafeName(package.Version) + ".zip";
            string destination = Path.Combine(VModPaths.Library, safeName);

            foreach (string oldFile in Directory.EnumerateFiles(VModPaths.Library, "*.zip", SearchOption.TopDirectoryOnly))
            {
                if (String.Equals(Path.GetFullPath(oldFile), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    ModPackage oldPackage = ReadPackage(oldFile);
                    if (!String.Equals(oldPackage.Id, package.Id, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(Path.GetFullPath(oldFile), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        continue;
                    string archived = Path.Combine(VModPaths.Archive, Path.GetFileNameWithoutExtension(oldFile) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                    File.Move(oldFile, archived, true);
                }
                catch { }
            }

            if (!String.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, destination, true);
            package.FilePath = destination;
            imported.Add(package);
        }
        return imported;
    }

    public Stream Open(ModPackage package)
    {
        if (!String.IsNullOrWhiteSpace(package.FilePath))
            return File.OpenRead(package.FilePath);
        if (String.IsNullOrWhiteSpace(package.ResourceName))
            throw new InvalidOperationException("Для пакета отсутствует источник файлов.");
        return AppAssembly.GetManifestResourceStream(package.ResourceName)
            ?? throw new InvalidOperationException("В VMod отсутствует встроенный пакет " + package.Name + ".");
    }

    public ModPackage ReadPackage(string zipPath)
    {
        using FileStream stream = File.OpenRead(zipPath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, false);
        ValidateArchive(archive);
        ZipArchiveEntry manifestEntry = archive.Entries.FirstOrDefault(entry =>
            String.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("В ZIP не найден manifest.json Thunderstore.");
        using Stream manifestStream = manifestEntry.Open();
        using JsonDocument manifest = JsonDocument.Parse(manifestStream);
        JsonElement root = manifest.RootElement;
        string name = ReadString(root, "name") ?? Path.GetFileNameWithoutExtension(zipPath);
        string version = ReadString(root, "version_number") ?? "0.0.0";
        string description = ReadString(root, "description") ?? "Пользовательский мод";
        List<string> dependencies = new();
        if (root.TryGetProperty("dependencies", out JsonElement dependencyElement) && dependencyElement.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in dependencyElement.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string value)
                    dependencies.Add(value);
        return new ModPackage
        {
            Id = NormalizeId(name),
            Name = name,
            DisplayName = name,
            Version = version,
            Description = description,
            Dependencies = dependencies,
            IsComponent = name.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "Jotunn", StringComparison.OrdinalIgnoreCase),
            IsBepInEx = name.Contains("BepInEx", StringComparison.OrdinalIgnoreCase),
            InstallOnServer = true,
            IsCustom = true,
            FilePath = zipPath
        };
    }

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > 10_000)
            throw new InvalidDataException("В архиве слишком много файлов.");
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(path))
                throw new InvalidDataException("Архив содержит небезопасный путь: " + entry.FullName);
            total += entry.Length;
            if (total > 1024L * 1024 * 1024)
                throw new InvalidDataException("Распакованный архив превышает ограничение 1 ГБ.");
        }
    }

    public static string NormalizeId(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-');
        return normalized.Length == 0 ? "mod-" + Math.Abs(value.GetHashCode()) : normalized;
    }

    public static string SafeName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Trim().Length == 0 ? "mod" : value.Trim();
    }

    private static string? ReadString(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left.Split('-')[0], out Version? leftVersion) && Version.TryParse(right.Split('-')[0], out Version? rightVersion))
            return leftVersion.CompareTo(rightVersion);
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static IReadOnlyList<ModPackage> BuiltInPackages() => new ModPackage[]
    {
        BuiltIn("bepinex", "BepInExPack_Valheim", "Загрузчик модов BepInEx", "5.4.2350", "bepinex.zip", true, true, true, "Основа для загрузки модов Valheim."),
        BuiltIn("jotunn", "Jotunn", "Библиотека модов Jotunn", "2.30.0", "jotunn.zip", true, false, true, "Библиотека, необходимая некоторым модам."),
        BuiltIn("wieldequipmentwhileswimming", "WieldEquipmentWhileSwimming", "Снаряжение во время плавания", "1.1.3", "wield.zip", false, false, true, "Позволяет использовать снаряжение в воде."),
        BuiltIn("dive_in", "Dive_In", "Ныряние", "1.2.3", "divein.zip", false, false, true, "Ныряние, ускоренное плавание и подводное взаимодействие."),
        BuiltIn("immersivebuildcamera", "ImmersiveBuildCamera", "Иммерсивная камера строительства", "1.1.1", "buildcamera.zip", false, false, false, "Удобная камера для точного строительства."),
        BuiltIn("anttrails", "AntTrails", "Протоптанные тропы", "1.0.0", "anttrails.zip", false, false, true, "Следы игроков постепенно превращаются в тропы."),
        BuiltIn("azucraftyboxes", "AzuCraftyBoxes", "Крафт из ближайших сундуков", "1.8.15", "craftyboxes.zip", false, false, true, "Использование ресурсов из ближайших контейнеров."),
        BuiltIn("farming", "Farming", "Фермерство", "2.2.2", "farming.zip", false, false, true, "Навык фермерства и улучшенный урожай."),
        BuiltIn("conversionsizeandspeed", "ConversionSizeAndSpeed", "Вместимость и скорость переработки", "1.0.17", "conversion.zip", false, false, true, "Настройка скорости и ёмкости производственных построек."),
        BuiltIn("fooddurationmultiplier", "FoodDurationMultiplier", "Множитель длительности еды", "1.1.6", "food.zip", false, false, true, "Настройка длительности и эффектов еды."),
        BuiltIn("creaturelevelandlootcontrol", "CreatureLevelAndLootControl", "Уровни существ и управление добычей", "4.6.4", "creatures.zip", false, false, true, "Управление уровнями существ и количеством добычи."),
        BuiltIn("sailing", "Sailing", "Мореплавание", "1.1.8", "sailing.zip", false, false, true, "Навык мореплавания и улучшение кораблей."),
        BuiltIn("equipmentandquickslots", "EquipmentAndQuickSlots", "Снаряжение и быстрые слоты", "3.1.1", "equipment.zip", false, false, true, "Отдельные слоты экипировки и быстрые ячейки."),
        BuiltIn("stumpsregrow", "StumpsRegrow", "Возрождение деревьев из пней", "1.0.5", "stumps.zip", false, false, true, "Пни через заданное время снова становятся деревьями."),
        BuiltIn("currencypocket", "CurrencyPocket", "Кошелёк для монет", "1.0.12", "currency.zip", false, false, false, "Отдельное хранилище монет персонажа."),
        BuiltIn("daytimecountdown", "DayTimeCountdown", "Таймер дня и ночи", "1.2.0", "daytime.zip", false, false, false, "Отображение оставшегося времени дня или ночи."),
        BuiltIn("extraskillmining", "ExtraSkillMining", "Навык горного дела", "1.0.0", "mining.zip", false, false, false, "Навык добычи руды с дополнительными бонусами.")
    };

    private static ModPackage BuiltIn(string id, string name, string displayName, string version, string resourceFile, bool component, bool bepinex, bool server, string description)
        => new()
        {
            Id = id,
            Name = name,
            DisplayName = displayName,
            Version = version,
            ResourceName = "payload." + resourceFile,
            IsComponent = component,
            IsBepInEx = bepinex,
            InstallOnServer = server,
            IsCustom = false,
            Description = description
        };
}

internal static class SteamLocator
{
    public static string Detect(InstallTarget target)
    {
        string folder = target == InstallTarget.Game ? "Valheim" : "Valheim dedicated server";
        List<string> libraries = new();
        Add(libraries, ReadSteamPath());
        Add(libraries, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(libraries, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        Add(libraries, @"Z:\home\deck\.local\share\Steam");
        Add(libraries, @"Z:\home\deck\.steam\steam");

        for (int index = 0; index < libraries.Count; index++)
        {
            string vdf = Path.Combine(libraries[index], "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    Add(libraries, match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }

        foreach (string library in libraries)
        {
            string candidate = Path.Combine(library, "steamapps", "common", folder);
            if (IsValid(candidate, target))
                return candidate;
        }
        return "";
    }

    public static bool IsValid(string path, InstallTarget target)
    {
        if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        return target == InstallTarget.Game
            ? File.Exists(Path.Combine(path, "valheim.exe")) || File.Exists(Path.Combine(path, "valheim.x86_64"))
            : File.Exists(Path.Combine(path, "valheim_server.exe")) || File.Exists(Path.Combine(path, "valheim_server.x86_64"));
    }

    private static string? ReadSteamPath()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? value = key?.GetValue("SteamPath")?.ToString() ?? key?.GetValue("SteamExe")?.ToString();
            if (String.IsNullOrWhiteSpace(value))
                return null;
            value = value.Replace('/', '\\');
            return String.Equals(Path.GetFileName(value), "steam.exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(value) : value;
        }
        catch { return null; }
    }

    private static void Add(List<string> values, string? path)
    {
        if (String.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!values.Contains(full, StringComparer.OrdinalIgnoreCase))
                values.Add(full);
        }
        catch { }
    }
}
