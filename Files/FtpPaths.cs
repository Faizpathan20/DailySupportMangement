namespace Master.Files;

public sealed record Crumb(string Name, string Path);

// ================================================
// Path helpers shared by the controller, the FTP
// service and the index. Every user supplied path
// goes through Normalize(), which removes ".."
// segments and rejects control characters.
// ================================================

public static class FtpPaths
{
    public static string Normalize(string? path)
    {
        var raw = (path ?? "/").Replace('\\', '/');

        var parts = new List<string>();

        foreach (var segment in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == ".." ||
                segment.IndexOfAny(['\0', '\r', '\n']) >= 0)
            {
                throw new ArgumentException("Invalid path.");
            }

            parts.Add(segment);
        }

        return "/" + string.Join('/', parts);
    }

    public static string Parent(string path)
    {
        path = Normalize(path);

        if (path == "/")
            return "/";

        var index = path.LastIndexOf('/');

        return index <= 0 ? "/" : path[..index];
    }

    public static string Combine(string parent, string name)
    {
        parent = Normalize(parent);

        return parent == "/"
            ? "/" + name
            : parent.TrimEnd('/') + "/" + name;
    }

    // Builds the public IIS URL. Each segment is
    // escaped because file names contain spaces,
    // "#", "&", "+" and non-ASCII characters.
    public static string PublicUrl(string baseUrl, string relativePath)
    {
        var suffix = string.Concat(
            relativePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => "/" + Uri.EscapeDataString(segment)));

        return baseUrl.TrimEnd('/') + suffix;
    }

    public static List<Crumb> Crumbs(string path)
    {
        path = Normalize(path);

        var crumbs = new List<Crumb> { new("Files", "/") };

        var accumulated = "";

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated += "/" + segment;
            crumbs.Add(new Crumb(segment, accumulated));
        }

        return crumbs;
    }
}
