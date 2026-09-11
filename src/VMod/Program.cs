namespace VMod;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-check")
        {
            IReadOnlyList<ModPackage> packages = new PackageRepository().LoadAll();
            return packages.Count == 17 && packages.Count(package => !package.IsComponent) == 15 ? 0 : 2;
        }
        if (UpdateService.HandleUpdateArguments(args))
            return 0;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
