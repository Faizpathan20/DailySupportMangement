namespace Master.Files;

public sealed class FileManagerPageVm
{
    public string Path { get; set; } = "/";

    public List<Crumb> Crumbs { get; set; } = new();

    public List<FileItemVm> Folders { get; set; } = new();

    public List<FileItemVm> Files { get; set; } = new();

    public int FolderCount { get; set; }

    public int FileCount { get; set; }

    public Dictionary<string, int> KindCounts { get; set; } = new();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 120;

    public int TotalPages { get; set; } = 1;

    public string Sort { get; set; } = "name";

    public string Dir { get; set; } = "asc";

    public string? Error { get; set; }

    public string PublicBaseUrl { get; set; } = "";

    public IndexStatus Index { get; set; } =
        new(false, false, 0, 0, 0, null, null);
}

public sealed class FileItemVm
{
    public string Name { get; set; } = "";

    public string Path { get; set; } = "/";

    public string Ext { get; set; } = "";

    public string Kind { get; set; } = "other";

    public string Label { get; set; } = "FILE";

    public bool IsFolder { get; set; }

    public long Size { get; set; }

    public string SizeText { get; set; } = "";

    public string ModifiedText { get; set; } = "";

    public string ViewUrl { get; set; } = "";

    public string DownloadUrl { get; set; } = "";
}
