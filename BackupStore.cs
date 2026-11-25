using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValCC;

public class BackupFile
{
    public required string FilePath { get; init; }

    public required DateTime CreatedAt { get; init; }

    public int Number { get; set; }

    public string DisplayName => CreatedAt.ToString("yyyy-MM-dd  HH:mm:ss", CultureInfo.InvariantCulture);
}

public static class BackupStore
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ValCC", "backups");

    public static string Write(JsonNode settings)
    {
        Directory.CreateDirectory(Folder);
        string baseName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        string path = Path.Combine(Folder, baseName + ".json");
        for (int copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(Folder, $"{baseName}_{copy}.json");
        }

        File.WriteAllText(path, settings.ToJsonString(JsonHelper.IndentedOptions));
        return Path.GetFileName(path);
    }

    public static List<BackupFile> List()
    {
        if (!Directory.Exists(Folder))
        {
            return new List<BackupFile>();
        }

        List<BackupFile> backups = Directory.GetFiles(Folder, "backup_*.json")
            .Select(path => new BackupFile { FilePath = path, CreatedAt = ReadTimestamp(path) })
            .OrderByDescending(backup => backup.CreatedAt)
            .ThenByDescending(backup => backup.FilePath)
            .ToList();

        for (int index = 0; index < backups.Count; index++)
        {
            backups[index].Number = index + 1;
        }

        return backups;
    }

    public static JsonNode Read(string path)
    {
        JsonNode? settings;
        try
        {
            settings = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            settings = null;
        }

        if (JsonHelper.Child(settings, "decoded") is JsonObject decodedSettings)
        {
            settings = decodedSettings;
        }

        if (settings is not JsonObject || JsonHelper.Child(settings, "stringSettings") is not JsonArray)
        {
            throw new InvalidDataException("this file is not a valorant settings backup.");
        }

        return settings;
    }

    private static DateTime ReadTimestamp(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Length >= 22 && DateTime.TryParseExact(name.Substring(7, 15), "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
        {
            return timestamp;
        }

        return File.GetLastWriteTime(path);
    }
}
