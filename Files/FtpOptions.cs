namespace Master.Files;

// ================================================
// FTP settings for the File Manager.
//
// The account used here is chrooted on the server
// to the "files" folder, so RootPath is "/" and
// FTP "/" == https://bestit.in/files.
//
// NOTE: ftp.bestit.in does not resolve in DNS at
// the time this was written, so the server IP is
// used. Keep the host configurable for the future.
// ================================================

public sealed class FtpOptions
{
    public string Host { get; set; } = "50.63.8.60";

    public int Port { get; set; } = 21;

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    // Plain FTP by default. The server also offers
    // AUTH TLS, but its certificate is a GoDaddy
    // wildcard (*.shr.prod.phx3.secureserver.net),
    // so normal validation fails against this host.
    public bool UseTls { get; set; }

    public bool AcceptAnyCertificate { get; set; }

    // Path on the FTP server that maps to the web
    // folder. The account is chrooted, so this is "/".
    public string RootPath { get; set; } = "/";

    // Public IIS folder used for direct video / PDF
    // playback. Leave empty to proxy everything.
    public string PublicBaseUrl { get; set; } = "https://bestit.in/files";

    // How long a folder listing stays cached.
    public int CacheSeconds { get; set; } = 120;

    // Files shown per page inside a folder.
    public int PageSize { get; set; } = 120;

    // Parallel FTP connections used by the
    // background search-index scan.
    public int ScanParallelism { get; set; } = 4;

    // Hard stop for a full recursive scan.
    public int ScanTimeoutMinutes { get; set; } = 20;

    // Index scan safety caps.
    public int MaxFolders { get; set; } = 20000;

    public int MaxFiles { get; set; } = 250000;
}
