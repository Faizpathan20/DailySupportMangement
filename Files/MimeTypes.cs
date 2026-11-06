namespace Master.Files;

// ================================================
// MIME map + the "kind" used by the UI icons and
// filter chips. Inline is deliberately conservative:
// HTML, SVG and scripts are never rendered inline
// from the app origin.
// ================================================

public static class MimeTypes
{
    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = "video/mp4",
            [".m4v"] = "video/mp4",
            [".webm"] = "video/webm",
            [".ogv"] = "video/ogg",
            [".ogg"] = "video/ogg",
            [".mov"] = "video/quicktime",
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".m4a"] = "audio/mp4",
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".txt"] = "text/plain; charset=utf-8",
            [".log"] = "text/plain; charset=utf-8",
            [".md"] = "text/plain; charset=utf-8",
            [".csv"] = "text/csv; charset=utf-8",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".xls"] = "application/vnd.ms-excel",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".zip"] = "application/zip",
            [".rar"] = "application/vnd.rar",
            [".7z"] = "application/x-7z-compressed",
            [".tar"] = "application/x-tar",
            [".gz"] = "application/gzip",
            [".exe"] = "application/vnd.microsoft.portable-executable",
            [".msi"] = "application/x-msi"
        };

    public static string For(string ext)
    {
        return Map.TryGetValue(ext ?? "", out var mime)
            ? mime
            : "application/octet-stream";
    }

    // Only these types are safe to display inline from
    // the application's own origin. SVG is excluded on
    // purpose because it can contain script.
    public static bool CanInline(string ext)
    {
        return (ext ?? "").ToLowerInvariant() is
            ".mp4" or ".m4v" or ".webm" or ".ogv" or ".ogg" or
            ".mp3" or ".wav" or ".m4a" or
            ".pdf" or
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    public static string Kind(string ext)
    {
        return (ext ?? "").ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" or ".webm" or ".ogv" or ".ogg" or
            ".mov" or ".avi" or ".mkv" => "video",

            ".mp3" or ".wav" or ".m4a" or ".aac" => "audio",

            ".pdf" => "pdf",

            ".xlsx" or ".xls" or ".csv" => "excel",

            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or
            ".bmp" or ".svg" or ".ico" => "image",

            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "archive",

            ".doc" or ".docx" or ".ppt" or ".pptx" or
            ".txt" or ".log" or ".md" or ".json" or ".xml" => "doc",

            _ => "other"
        };
    }

    // Text shown inside the coloured file tile.
    public static string Label(string ext)
    {
        var clean = (ext ?? "").TrimStart('.').ToUpperInvariant();

        return clean.Length switch
        {
            0 => "FILE",
            <= 4 => clean,
            _ => clean[..4]
        };
    }

    public static bool IsText(string ext)
    {
        return (ext ?? "").ToLowerInvariant() is
            ".txt" or ".log" or ".md" or ".json" or ".xml" or
            ".csv" or ".cs" or ".js" or ".css" or ".html" or
            ".htm" or ".config" or ".ini" or ".sql" or ".bat" or
            ".ps1" or ".yml" or ".yaml";
    }

    public static string Size(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.#} {units[unit]}";
    }
}
