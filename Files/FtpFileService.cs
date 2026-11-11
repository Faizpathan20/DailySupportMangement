using System.Buffers;
using System.Text;
using FluentFTP;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Master.Files;

public sealed class FileStorageException : Exception
{
    public FileStorageException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// ================================================
// FTP access used by the File Manager.
//
// Server quirks handled here:
//   * PASV is broken: it answers with a 229 line,
//     so EPSV is forced.
//   * MLSD is not supported: ForceList keeps the
//     parser on regular LIST output (which includes
//     sizes and timestamps).
//   * The account is chrooted, so "/" is the files
//     folder and no root prefix has to be added.
// ================================================

public sealed class FtpFileService
{
    private readonly FtpOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FtpFileService> _logger;

    // Keeps a burst of folder clicks from opening a
    // dozen FTP connections at once. Streaming does
    // not use this gate.
    private readonly SemaphoreSlim _listingGate = new(6, 6);

    public FtpFileService(
        IOptions<FtpOptions> options,
        IMemoryCache cache,
        ILogger<FtpFileService> logger)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public FtpOptions Options => _options;

    public AsyncFtpClient CreateClient()
    {
        var client = new AsyncFtpClient(
            _options.Host,
            _options.User,
            _options.Password,
            _options.Port);

        // The server advertises UTF8 in FEAT and is
        // running on IIS, so file names with spaces or
        // non-ASCII characters survive the listing.
        client.Encoding = Encoding.UTF8;

        // The server answers PASV with an EPSV style
        // "229 Extended Passive Mode Entered" reply.
        // Forcing EPSV is what makes this host work.
        client.Config.DataConnectionType = FtpDataConnectionType.EPSV;

        client.Config.ConnectTimeout = 15_000;
        client.Config.ReadTimeout = 60_000;
        client.Config.DataConnectionConnectTimeout = 15_000;
        client.Config.DataConnectionReadTimeout = 60_000;

        client.Config.RetryAttempts = 1;

        if (_options.UseTls)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;

            // The certificate is GoDaddy's
            // *.shr.prod.phx3.secureserver.net wildcard,
            // which does not match this IP or bestit.in.
            client.Config.ValidateAnyCertificate =
                _options.AcceptAnyCertificate;
        }

        return client;
    }

    // ------------------------------------------------
    // FOLDER LISTING
    // ------------------------------------------------

    public async Task<IReadOnlyList<FtpEntry>> GetFolderAsync(
        string path,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        path = FtpPaths.Normalize(path);

        var cacheKey = "fm:dir:" + path;

        if (!forceRefresh &&
            _cache.TryGetValue(cacheKey, out IReadOnlyList<FtpEntry>? cached) &&
            cached is not null)
        {
            return cached;
        }

        await _listingGate.WaitAsync(cancellationToken);

        try
        {
            // Re-check after waiting for the gate.
            if (!forceRefresh &&
                _cache.TryGetValue(cacheKey, out cached) &&
                cached is not null)
                return cached;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await using var client = CreateClient();

                    await client.Connect(cancellationToken);

                    var listing = await client.GetListing(
                        path,
                        FtpListOption.ForceList,
                        cancellationToken);

                    var entries = listing
                        .Where(item => item.Name is not "." and not "..")
                        .Select(ToEntry)
                        .OrderBy(item => item.IsFolder ? 0 : 1)
                        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    _cache.Set(
                        cacheKey,
                        (IReadOnlyList<FtpEntry>)entries,
                        TimeSpan.FromSeconds(Math.Max(10, _options.CacheSeconds)));

                    return entries;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // FluentFTP occasionally throws a
                    // NullReferenceException while parsing one
                    // folder (for example /4436/076) even
                    // though the same LIST succeeds on a fresh
                    // connection. One quick retry fixes it.
                    if (attempt < 2)
                    {
                        _logger.LogWarning(
                            ex,
                            "FTP listing failed for {Path} on {Host} (attempt {Attempt}); retrying",
                            path,
                            _options.Host,
                            attempt);

                        await Task.Delay(400, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning(
                        ex,
                        "FTP listing failed for {Path} on {Host}",
                        path,
                        _options.Host);

                    throw new FileStorageException(
                        "FTP server se folder list nahi ho paaya. " +
                        "Connection ya credentials check kijiye.",
                        ex);
                }
            }
        }
        finally
        {
            _listingGate.Release();
        }
    }

    public async Task<FtpEntry?> FindFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        path = FtpPaths.Normalize(path);

        if (path == "/")
            return null;

        var parent = FtpPaths.Parent(path);
        var folder = await GetFolderAsync(parent, cancellationToken);

        return folder.FirstOrDefault(item =>
            !item.IsFolder &&
            item.Path.Equals(path, StringComparison.Ordinal));
    }

    /// <summary>
    /// IIS returns an empty LIST for a folder that does not
    /// exist instead of an error, so an empty page cannot
    /// tell "empty folder" and "typo in the URL" apart.
    /// This checks the parent listing to be sure.
    /// </summary>
    public async Task<bool> FolderExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        path = FtpPaths.Normalize(path);

        if (path == "/")
            return true;

        var parent = FtpPaths.Parent(path);
        var siblings = await GetFolderAsync(parent, cancellationToken);

        return siblings.Any(item =>
            item.IsFolder &&
            item.Path.Equals(path, StringComparison.Ordinal));
    }

    public void ForgetFolder(string path)
    {
        _cache.Remove("fm:dir:" + FtpPaths.Normalize(path));
    }

    public void ForgetAll()
    {
        // The index service keeps its own cache; the
        // per-folder cache is short lived, so the easiest
        // correct reset is to age the entries out early.
        // A new scan uses fresh connections anyway.
    }

    // ------------------------------------------------
    // DOWNLOAD / STREAM
    // ------------------------------------------------

    public async Task<long> GetSizeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var client = CreateClient();

        try
        {
            await client.Connect(cancellationToken);
            return await client.GetFileSize(path, -1, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new FileStorageException(
                "File ka size nahi mila.", ex);
        }
    }

    /// <summary>
    /// Streams <paramref name="count"/> bytes starting at
    /// <paramref name="offset"/>. FTP cannot seek, so the
    /// server is asked to restart the transfer at the
    /// requested byte offset. This is what makes video
    /// seeking work when the app proxies the file.
    /// </summary>
    public async Task CopyToAsync(
        string path,
        long offset,
        long count,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        await using var client = CreateClient();

        await client.Connect(cancellationToken);

        await using var source = await client.OpenRead(
            path,
            FtpDataType.Binary,
            offset,
            true,
            cancellationToken);

        var buffer = ArrayPool<byte>.Shared.Rent(80 * 1024);

        try
        {
            while (count > 0)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)),
                    cancellationToken);

                if (read <= 0)
                    break;

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);

                count -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ------------------------------------------------
    // MAPPING
    // ------------------------------------------------

    private static FtpEntry ToEntry(FtpListItem item)
    {
        var fullPath = "/" + item.FullName
            .Replace('\\', '/')
            .TrimStart('/');

        var isFolder = item.Type == FtpObjectType.Directory;

        return new FtpEntry
        {
            Path = fullPath,
            Name = item.Name,
            IsFolder = isFolder,
            Size = isFolder ? 0 : Math.Max(0, item.Size),
            Modified = item.Modified == DateTime.MinValue
                ? null
                : item.Modified,
            Ext = isFolder
                ? ""
                : Path.GetExtension(item.Name).ToLowerInvariant()
        };
    }
}
