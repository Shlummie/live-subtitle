namespace LiveSubtitle.App.Services;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveSubtitle");
        Models = Path.Combine(Root, "Models");
        Logs = Path.Combine(Root, "Logs");
        Exports = Path.Combine(Root, "Exports");
        Settings = Path.Combine(Root, "settings.json");
        History = Path.Combine(Root, "history.sqlite3");
    }

    public string Root { get; }
    public string Models { get; }
    public string Logs { get; }
    public string Exports { get; }
    public string Settings { get; }
    public string History { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Exports);
    }
}
