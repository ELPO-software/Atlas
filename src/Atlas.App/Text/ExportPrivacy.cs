namespace Atlas.App.Text;

/// <summary>Finds what an exported file would reveal about the person who exports it.</summary>
internal static class ExportPrivacy
{
    /// <returns>The user name if a location in the workspace contains it, otherwise null.</returns>
    public static string? RevealedUserName(IEnumerable<string> targets, string userName, string profileFolder)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var userFolder = $@"\Users\{userName}\";
        var profile = Path.TrimEndingDirectorySeparator(profileFolder);
        foreach (var target in targets)
        {
            // "C:\Users\jack" must not match "C:\Users\jackson": the folder ends at a separator or with the path.
            var insideProfile = profile.Length > 0
                && (target.Equals(profile, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (insideProfile
                || target.Contains(userFolder, StringComparison.OrdinalIgnoreCase)
                || target.EndsWith(userFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return userName;
            }
        }

        return null;
    }

    /// <summary>A file name made from the workspace name, without the characters Windows refuses.</summary>
    public static string FileNameFor(string workspaceName)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var name = new string(workspaceName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return (name.Length == 0 ? "atlas" : name) + ".json";
    }
}
