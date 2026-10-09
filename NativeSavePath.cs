namespace CyberTrap;
public static class NativeSavePath
{
    public static string Rebuild(string persistentPath, string requested)
    {
        var root = Path.GetFullPath(Path.Combine(persistentPath, "Saves"));
        var normalized = Path.GetFullPath(requested);
        if (!normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("select_native_save_folder");
        // The gameplay bridge keys its ledgers by the native folder string.
        // Unity's persistentDataPath uses forward slashes; Path.Combine appends
        // the native Windows Saves/slot portion. Preserve that exact format.
        return Path.Combine(persistentPath, "Saves", Path.GetRelativePath(root, normalized));
    }
}
