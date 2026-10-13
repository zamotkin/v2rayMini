namespace ServiceLib.Handler;

public static class ConfigMigrationHandler
{
    private static readonly string _tag = "ConfigMigrationHandler";

    /// <summary>
    /// Copies the first non-empty legacy config dir into the target dir on first run.
    /// Never overwrites or merges: an existing target holding any file is left untouched.
    /// </summary>
    public static bool Migrate(string targetConfigDir, IReadOnlyList<string> candidateConfigDirs)
    {
        try
        {
            if (Directory.Exists(targetConfigDir) && Directory.EnumerateFiles(targetConfigDir, "*", SearchOption.AllDirectories).Any())
            {
                return false;
            }

            foreach (var candidateConfigDir in candidateConfigDirs)
            {
                if (Directory.Exists(candidateConfigDir) == false || Directory.EnumerateFiles(candidateConfigDir, "*", SearchOption.AllDirectories).Any() == false)
                {
                    continue;
                }

                FileUtils.CopyDirectory(candidateConfigDir, targetConfigDir, true, false);
                Logging.SaveLog($"{_tag} migrated config from {candidateConfigDir} to {targetConfigDir}");
                return true;
            }

            Logging.SaveLog($"{_tag} no legacy config found for {targetConfigDir}");
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ConfigMigration", ex);
            return false;
        }
    }

    public static IReadOnlyList<string> GetDefaultCandidates()
    {
        return
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "v2rayN", "guiConfigs"),
            "/Applications/v2rayN.app/Contents/MacOS/guiConfigs",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "v2rayN.app", "Contents", "MacOS", "guiConfigs"),
        ];
    }
}
