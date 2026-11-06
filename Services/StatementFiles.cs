namespace MoneySpend.Services;

/// <summary>
/// Keeps an imported statement / screenshot in the app's PRIVATE folder (other apps cannot read it)
/// for one day, then deletes it. The transactions themselves are already saved in the database
/// by then, so the file is no longer needed.
/// </summary>
public static class StatementFiles
{
    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "imports");

    public static async Task<string> SaveAsync(FileResult file)
    {
        Directory.CreateDirectory(Folder);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

        var path = Path.Combine(Folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}");

        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(path);
        await source.CopyToAsync(target);

        return path;
    }

    /// <summary>Delete right away (used when nothing useful was found in the file).</summary>
    public static void DeleteNow(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort - the daily clean-up will try again
        }
    }

    /// <summary>Call on every app start: removes imported files older than <paramref name="age"/>.</summary>
    public static void DeleteOlderThan(TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;

            var limit = DateTime.UtcNow - age;

            foreach (var file in Directory.GetFiles(Folder))
            {
                try
                {
                    // LastWrite = the moment we copied it (creation time is not reliable on Android)
                    if (File.GetLastWriteTimeUtc(file) <= limit)
                        File.Delete(file);
                }
                catch
                {
                    // skip this one, try again next launch
                }
            }
        }
        catch
        {
            // never let clean-up crash the app
        }
    }
}
