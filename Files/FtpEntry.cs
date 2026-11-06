namespace Master.Files;

// ================================================
// One file or folder returned by an FTP listing.
// Path is always absolute inside the FTP account
// and starts with "/", e.g. "/4436/062/54_L4_01.pdf".
// ================================================

public sealed class FtpEntry
{
    public string Path { get; set; } = "/";

    public string Name { get; set; } = "";

    public bool IsFolder { get; set; }

    public long Size { get; set; }

    public DateTime? Modified { get; set; }

    public string Ext { get; set; } = "";
}
