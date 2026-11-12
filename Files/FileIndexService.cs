using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Master.Files;

public sealed record IndexStatus(
    bool Running,
    bool Complete,
    int Folders,
    int Files,
    int Seconds,
    DateTime? BuiltUtc,
    string? Error);

public sealed record FileSearchPage(
    IReadOnlyList<FtpEntry> Items,
    int Total);

// ================================================
// Background catalogue used by "Search all folders".
//
// The FTP tree is far too large to walk inside a page
// request (tens of thousands of files, ~2 seconds per
// folder). This service walks it in the background
// with a few parallel connections, keeps the result in
// memory and writes a JSON snapshot to App_Data so an
// app-pool recycle does not force another full scan.
//
// Nothing starts automatically: the first deep search
// calls EnsureStarted().
// ================================================

public sealed class FileIndexService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, FtpEntry> _entries =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> _seenFolders =
        new(StringComparer.Ordinal);

    // Paths seen in the last live listing of each folder. Used
    // to drop files that were deleted since the index was built.
    private readonly ConcurrentDictionary<string, string[]> _folderPaths =
        new(StringComparer.Ordinal);

    private readonly FtpFileService _ftp;
    private readonly FtpOptions _options;
    private readonly ILogger<FileIndexService> _logger;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _state = new();

    private CancellationTokenSource? _cts;
    private Task? _task;
    private Task? _refreshTask;
    private bool _complete;
    private string? _lastError;
    private int _folders;
    private int _files;
    private DateTime? _builtUtc;
    private DateTime _startedUtc;

    public FileIndexService(
        FtpFileService ftp,
        IOptions<FtpOptions> options,
        IWebHostEnvironment environment,
        ILogger<FileIndexService> logger)
    {
        _ftp = ftp;
        _options = options.Value;
        _logger = logger;

        _indexPath = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "ftp-index.json");

        LoadFromDisk();
    }

    // ------------------------------------------------
    // PUBLIC API
    // ------------------------------------------------

    public IndexStatus GetStatus()
    {
        lock (_state)
        {
            return BuildStatusUnsafe();
        }
    }

    public IndexStatus EnsureStarted()
    {
        var status = GetStatus();

        if (status.Complete || status.Running)
            return status;

        return Start();
    }

    // Starts a background refresh when the index has become
    // older than AutoRefreshMinutes. The current snapshot keeps
    // serving searches while the refresh runs.
    public IndexStatus EnsureFresh()
    {
        var minutes = _options.AutoRefreshMinutes;

        if (minutes <= 0)
            return GetStatus();

        lock (_state)
        {
            if (_task is { IsCompleted: false } ||
                _refreshTask is { IsCompleted: false } ||
                !_complete)
            {
                return BuildStatusUnsafe();
            }

            var age = _builtUtc is null
                ? TimeSpan.MaxValue
                : DateTime.UtcNow - _builtUtc.Value;

            if (age < TimeSpan.FromMinutes(minutes))
                return BuildStatusUnsafe();

            _refreshTask = Task.Run(
                () => SoftRefreshAsync(CancellationToken.None));
        }

        return GetStatus();
    }

    public IndexStatus Start()
    {
        lock (_state)
        {
            if (_task is { IsCompleted: false } ||
                _refreshTask is { IsCompleted: false })
                return BuildStatusUnsafe();

            _cts?.Dispose();

            _cts = new CancellationTokenSource(
                TimeSpan.FromMinutes(Math.Max(1, _options.ScanTimeoutMinutes)));

            _entries.Clear();
            _seenFolders.Clear();
            _folderPaths.Clear();
            _seenFolders["/"] = 0;

            _folders = 0;
            _files = 0;
            _complete = false;
            _lastError = null;
            _startedUtc = DateTime.UtcNow;
            _builtUtc = null;

            _task = Task.Run(() => RunAsync(_cts.Token));
        }

        return GetStatus();
    }

    public FileSearchPage Search(
        string? query,
        string? kind,
        int offset = 0,
        int limit = 500,
        string? preferPath = null)
    {
        IEnumerable<FtpEntry> entries = _entries.Values;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();

            entries = entries.Where(entry =>
                entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                entry.Path.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(kind) &&
            !kind.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(entry =>
                entry.IsFolder
                    ? kind.Equals("folder", StringComparison.OrdinalIgnoreCase)
                    : MimeTypes.Kind(entry.Ext)
                        .Equals(kind, StringComparison.OrdinalIgnoreCase));
        }

        var preferred = (preferPath ?? "/").TrimEnd('/');

        if (preferred.Length == 0)
        {
            preferred = "/";
        }

        var ordered = entries
            .OrderByDescending(entry => IsInside(entry.Path, preferred))
            .ThenBy(entry => entry.IsFolder ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = ordered
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 2000))
            .ToList();

        return new FileSearchPage(items, ordered.Count);
    }

    // ------------------------------------------------
    // LIVE FOLDER SYNC
    // ------------------------------------------------
    // Folder browsing reads the FTP server directly, so a
    // folder can already show files that the background
    // snapshot does not know about yet. Feeding that live
    // listing back into the index keeps search results in
    // sync without waiting for a full rescan.
    public void SyncFolder(string folder, IReadOnlyList<FtpEntry> items)
    {
        folder = FtpPaths.Normalize(folder);

        var live = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            live.Add(item.Path);
        }

        // Remove entries that disappeared from this folder.
        if (_folderPaths.TryRemove(folder, out var previous))
        {
            foreach (var path in previous)
            {
                if (live.Contains(path))
                    continue;

                if (_entries.TryRemove(path, out var removed) &&
                    !removed.IsFolder)
                {
                    Interlocked.Decrement(ref _files);
                }

                _seenFolders.TryRemove(path, out _);
            }
        }

        // Add or update the current listing.
        foreach (var item in items)
        {
            if (_entries.TryAdd(item.Path, item))
            {
                if (!item.IsFolder)
                    Interlocked.Increment(ref _files);
            }
            else
            {
                _entries[item.Path] = item;
            }

            if (item.IsFolder)
                _seenFolders.TryAdd(item.Path, 0);
        }

        _folderPaths[folder] = live.ToArray();
    }

    // ------------------------------------------------
    // BACKGROUND REFRESH
    // ------------------------------------------------
    // Re-lists every folder and feeds the live results into
    // the index without clearing it, so searches keep working
    // and newly added/removed files appear automatically.
    private async Task SoftRefreshAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(Math.Max(1, _options.ScanTimeoutMinutes)));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        var token = linked.Token;
        var queue = new ConcurrentQueue<string>();
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        queue.Enqueue("/");
        seen["/"] = 0;

        var activeWorkers = 0;

        async Task WorkerAsync()
        {
            while (!token.IsCancellationRequested)
            {
                if (queue.TryDequeue(out var folder))
                {
                    Interlocked.Increment(ref activeWorkers);

                    try
                    {
                        var items = await _ftp.GetFolderAsync(
                            folder,
                            token,
                            forceRefresh: true);

                        SyncFolder(folder, items);

                        foreach (var item in items)
                        {
                            if (item.IsFolder &&
                                seen.TryAdd(item.Path, 0))
                            {
                                queue.Enqueue(item.Path);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Index refresh failed for {Folder}",
                            folder);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeWorkers);
                    }

                    continue;
                }

                if (Volatile.Read(ref activeWorkers) == 0)
                    break;

                await Task.Delay(150, token);
            }
        }

        var workerCount = Math.Clamp(_options.ScanParallelism, 1, 8);

        var workers = Enumerable
            .Range(0, workerCount)
            .Select(_ => Task.Run(WorkerAsync, token))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Index refresh timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Index refresh failed.");
        }

        lock (_state)
        {
            _builtUtc = DateTime.UtcNow;
        }

        await SaveSnapshotAsync(CancellationToken.None, wait: true);
    }

    // ------------------------------------------------
    // SCAN
    // ------------------------------------------------

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue("/");

        var activeWorkers = 0;

        async Task WorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (queue.TryDequeue(out var folder))
                {
                    Interlocked.Increment(ref activeWorkers);

                    try
                    {
                        await ScanFolderAsync(folder, queue, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lock (_state)
                        {
                            _lastError = ex.Message;
                        }

                        _logger.LogWarning(
                            ex,
                            "Index scan failed for {Folder}",
                            folder);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeWorkers);
                    }

                    continue;
                }

                // A worker may be about to add children from
                // the folder it is processing. Only stop once
                // the queue is empty AND nobody is working.
                if (Volatile.Read(ref activeWorkers) == 0)
                    break;

                await Task.Delay(150, cancellationToken);
            }
        }

        var workerCount = Math.Clamp(_options.ScanParallelism, 1, 8);

        var workers = Enumerable
            .Range(0, workerCount)
            .Select(_ => Task.Run(WorkerAsync, cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            lock (_state)
            {
                _lastError ??= "Index scan timed out.";
            }
        }

        lock (_state)
        {
            _complete = !cancellationToken.IsCancellationRequested;
            _builtUtc = DateTime.UtcNow;
        }

        await SaveSnapshotAsync(CancellationToken.None, wait: true);
    }

    private async Task ScanFolderAsync(
        string folder,
        ConcurrentQueue<string> queue,
        CancellationToken cancellationToken)
    {
        var items = await _ftp.GetFolderAsync(folder, cancellationToken);

        Interlocked.Increment(ref _folders);

        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                _entries[item.Path] = item;

                if (_folders < _options.MaxFolders &&
                    _seenFolders.TryAdd(item.Path, 0))
                {
                    queue.Enqueue(item.Path);
                }
            }
            else
            {
                if (_files >= _options.MaxFiles)
                    continue;

                if (_entries.TryAdd(item.Path, item))
                    Interlocked.Increment(ref _files);
            }
        }

        if (_folders % 500 == 0)
            await SaveSnapshotAsync(cancellationToken);
    }

    // ------------------------------------------------
    // PERSISTENCE
    // ------------------------------------------------

    private sealed class IndexSnapshot
    {
        public DateTime BuiltUtc { get; set; }

        public bool Complete { get; set; }

        public List<FtpEntry> Entries { get; set; } = new();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return;

            var json = File.ReadAllText(_indexPath);
            var snapshot = JsonSerializer.Deserialize<IndexSnapshot>(json, JsonOptions);

            if (snapshot?.Entries is null || snapshot.Entries.Count == 0)
                return;

            foreach (var entry in snapshot.Entries)
            {
                if (entry.IsFolder)
                    _seenFolders.TryAdd(entry.Path, 0);

                _entries[entry.Path] = entry;
            }

            lock (_state)
            {
                _complete = snapshot.Complete;
                _builtUtc = snapshot.BuiltUtc;
                _folders = snapshot.Entries.Count(entry => entry.IsFolder);
                _files = snapshot.Entries.Count - _folders;
            }

            _logger.LogInformation(
                "Loaded FTP index snapshot: {Folders} folders, {Files} files",
                _folders,
                _files);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the FTP index snapshot.");
        }
    }

    private async Task SaveSnapshotAsync(
        CancellationToken cancellationToken,
        bool wait = false)
    {
        if (wait)
        {
            await _saveGate.WaitAsync(cancellationToken);
        }
        else if (!await _saveGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            IndexSnapshot snapshot;

            lock (_state)
            {
                snapshot = new IndexSnapshot
                {
                    BuiltUtc = _builtUtc ?? DateTime.UtcNow,
                    Complete = _complete,
                    Entries = _entries.Values
                        .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                        .ToList()
                };
            }

            var directory = Path.GetDirectoryName(_indexPath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var temp = _indexPath + ".tmp";

            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, _indexPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the FTP index snapshot.");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static bool IsInside(string path, string folder)
    {
        if (folder == "/")
        {
            return true;
        }

        return path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private IndexStatus BuildStatusUnsafe()
    {
        var running = _task is { IsCompleted: false };

        var seconds = _startedUtc == default
            ? 0
            : (int)(DateTime.UtcNow - _startedUtc).TotalSeconds;

        return new IndexStatus(
            running,
            _complete,
            _folders,
            _files,
            seconds,
            _builtUtc,
            _lastError);
    }
}
