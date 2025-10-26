using System;
using System.IO;

namespace ClipManagerForWindows.Infrastructure;

public static class AppPaths
{
    public static string GetAppDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "ClipManager");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetDatabasePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            return expanded;
        }
        return Path.Combine(GetAppDataDirectory(), "clipmanager.db");
    }
}
