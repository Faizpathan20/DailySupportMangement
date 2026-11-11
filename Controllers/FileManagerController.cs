using Master.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Master.Controllers;

// ================================================
// Google-Drive style file browser for the FTP
// account that backs https://bestit.in/files.
//
// Read-only on purpose:
//   * browsing and search use the FTP catalogue
//   * video / PDF / images use the public IIS URL
//   * Excel and text are fetched through Raw()
//   * Download() always returns an attachment
// ================================================

[Authorize]
public class FileManagerController : Controller
{
    private readonly FtpFileService _ftp;
    private readonly FileIndexService _index;
    private readonly FtpOptions _options;
    private readonly ILogger<FileManagerController> _logger;

    public FileManagerController(
        FtpFileService ftp,
        FileIndexService index,
        Microsoft.Extensions.Options.IOptions<FtpOptions> options,
        ILogger<FileManagerController> logger)
    {
        _ftp = ftp;
        _index = index;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------
    // BROWSE
    // ------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index(
        string? path,
        int page = 1,
        string? sort = "name",
        string? dir = "asc",
        CancellationToken cancellationToken = default)
    {
        sort = (sort ?? "").ToLowerInvariant() switch
        {
            "date" => "date",
            "size" => "size",
            _ => "name"
        };

        dir = (dir ?? "").Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

        var model = new FileManagerPageVm
        {
            PublicBaseUrl = _options.PublicBaseUrl,
            Sort = sort,
            Dir = dir,
            Index = _index.GetStatus()
        };

        try
        {
            var normalized = FtpPaths.Normalize(path);

            model.Path = normalized;
            model.Crumbs = FtpPaths.Crumbs(normalized);

            var entries = await _ftp.GetFolderAsync(normalized, cancellationToken);

            // Keep the search index in step with the live listing.
            _index.SyncFolder(normalized, entries);

            if (entries.Count == 0 &&
                normalized != "/" &&
                !await _ftp.FolderExistsAsync(normalized, cancellationToken))
            {
                model.Error = "Ye folder FTP par maujood nahi hai.";
            }

            var folders = entries
                .Where(entry => entry.IsFolder)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var files = SortFiles(
                    entries.Where(entry => !entry.IsFolder),
                    sort,
                    dir)
                .ToList();

            var pageSize = Math.Clamp(_options.PageSize, 20, 400);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(files.Count / (double)pageSize));

            page = Math.Clamp(page, 1, totalPages);

            model.FolderCount = folders.Count;
            model.FileCount = files.Count;
            model.KindCounts = files
                .GroupBy(file => MimeTypes.Kind(file.Ext))
                .ToDictionary(group => group.Key, group => group.Count());
            model.Page = page;
            model.PageSize = pageSize;
            model.TotalPages = totalPages;

            model.Folders = folders
                .Select(ToItem)
                .ToList();

            model.Files = files
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToItem)
                .ToList();
        }
        catch (ArgumentException)
        {
            model.Path = "/";
            model.Crumbs = FtpPaths.Crumbs("/");
            model.Error = "Invalid folder path.";
        }
        catch (FileStorageException ex)
        {
            model.Path = "/";
            model.Crumbs = FtpPaths.Crumbs("/");
            model.Error = ex.Message;
        }
        catch (OperationCanceledException)
        {
            model.Path = "/";
            model.Crumbs = FtpPaths.Crumbs("/");
            model.Error = "Request was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File Manager failed to open {Path}", path);

            model.Path = "/";
            model.Crumbs = FtpPaths.Crumbs("/");
            model.Error = "File Manager load nahi ho paaya. Dobara try kijiye.";
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? path,
        string? kind = "all",
        bool deep = false,
        CancellationToken cancellationToken = default)
    {
        kind = NormalizeKind(kind);

        try
        {
            if (!deep)
            {
                var localPath = FtpPaths.Normalize(path);

                var folder = await _ftp.GetFolderAsync(
                    localPath,
                    cancellationToken);

                _index.SyncFolder(localPath, folder);

                IEnumerable<FtpEntry> matches = folder;

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var text = q.Trim();

                    matches = matches.Where(entry =>
                        entry.Name.Contains(
                            text,
                            StringComparison.OrdinalIgnoreCase));
                }

                matches = FilterKind(matches, kind);

                var local = matches
                    .OrderBy(entry => entry.IsFolder ? 0 : 1)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .Select(ToDto)
                    .ToList();

                return Json(new
                {
                    scanning = false,
                    items = local,
                    total = local.Count
                });
            }

            // Full-tree search uses the background index.
            var status = _index.EnsureStarted();

            if (!status.Complete)
            {
                return Json(new
                {
                    scanning = true,
                    status = ToDto(status)
                });
            }

            var normalizedPath = FtpPaths.Normalize(path);

            // Refresh this folder from FTP first so files added
            // after the last scan still show up in the results.
            try
            {
                var live = await _ftp.GetFolderAsync(
                    normalizedPath,
                    cancellationToken);

                _index.SyncFolder(normalizedPath, live);
            }
            catch (FileStorageException)
            {
                // Keep serving the existing index if the FTP
                // listing fails; search should not break.
            }

            // Refresh the rest of the tree in the background
            // when the snapshot has gone stale.
            _index.EnsureFresh();

            var results = _index
                .Search(q, kind, 500, normalizedPath)
                .Select(ToDto)
                .ToList();

            return Json(new
            {
                scanning = false,
                status = ToDto(status),
                items = results,
                total = results.Count
            });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Invalid folder path." });
        }
        catch (FileStorageException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File Manager search failed");

            return StatusCode(
                500,
                new { error = "Search failed. Please try again." });
        }
    }

    // ------------------------------------------------
    // INDEX STATUS
    // ------------------------------------------------

    [HttpGet]
    public IActionResult ScanStatus()
    {
        return Json(ToDto(_index.GetStatus()));
    }

    [HttpGet]
    public IActionResult Scan()
    {
        return Json(ToDto(_index.Start()));
    }

    [HttpGet]
    public IActionResult Warm()
    {
        var status = _index.EnsureStarted();

        _index.EnsureFresh();

        return Json(ToDto(status));
    }

    [HttpGet]
    public IActionResult Refresh(string? path)
    {
        string normalized;

        try
        {
            normalized = FtpPaths.Normalize(path);
        }
        catch (ArgumentException)
        {
            normalized = "/";
        }

        _ftp.ForgetFolder(normalized);

        return RedirectToAction(nameof(Index), new { path = normalized });
    }

    // ------------------------------------------------
    // FILE BYTES
    // ------------------------------------------------

    [HttpGet]
    [HttpHead]
    public Task<IActionResult> Raw(
        string path,
        bool download = false,
        CancellationToken cancellationToken = default)
    {
        return StreamAsync(path, download, cancellationToken);
    }

    [HttpGet]
    [HttpHead]
    public Task<IActionResult> Download(
        string path,
        CancellationToken cancellationToken = default)
    {
        return StreamAsync(path, download: true, cancellationToken);
    }

    private async Task<IActionResult> StreamAsync(
        string path,
        bool download,
        CancellationToken cancellationToken)
    {
        string relativePath;

        try
        {
            relativePath = FtpPaths.Normalize(path);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid path.");
        }

        try
        {
            var entry = await _ftp.FindFileAsync(
                relativePath,
                cancellationToken);

            if (entry is null)
                return NotFound();

            var size = entry.Size > 0
                ? entry.Size
                : await _ftp.GetSizeAsync(relativePath, cancellationToken);

            if (size < 0)
                return NotFound();

            var inline = !download && MimeTypes.CanInline(entry.Ext);

            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Cache-Control"] = "private,max-age=60";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.ContentType = MimeTypes.For(entry.Ext);

            var safeName = new string(entry.Name
                .Select(character =>
                    character < 128 && character is not '"' and not '\\'
                        ? character
                        : '_')
                .ToArray());

            Response.Headers["Content-Disposition"] =
                (inline ? "inline" : "attachment") +
                $"; filename=\"{safeName}\"" +
                $"; filename*=UTF-8''{Uri.EscapeDataString(entry.Name)}";

            var start = 0L;
            var end = size - 1;
            var partial = false;

            var range = Request.GetTypedHeaders().Range;

            if (range is { Ranges.Count: > 0 })
            {
                var requested = range.Ranges.FirstOrDefault();

                if (requested is null)
                {
                    // Malformed range header; fall through and
                    // serve the whole file.
                }
                else if (requested.From is long from)
                {
                    start = Math.Max(0, from);
                    end = requested.To is long to
                        ? Math.Min(to, size - 1)
                        : size - 1;
                }
                else if (requested.To is long suffix)
                {
                    // bytes=-N means the last N bytes.
                    start = Math.Max(0, size - suffix);
                    end = size - 1;
                }

                if (start >= size)
                {
                    Response.StatusCode =
                        StatusCodes.Status416RangeNotSatisfiable;

                    Response.Headers["Content-Range"] = $"bytes */{size}";

                    return new EmptyResult();
                }

                partial = true;
            }

            Response.StatusCode = partial
                ? StatusCodes.Status206PartialContent
                : StatusCodes.Status200OK;

            if (partial)
            {
                Response.Headers["Content-Range"] =
                    $"bytes {start}-{end}/{size}";
            }

            Response.ContentLength = end - start + 1;

            if (size == 0 || HttpMethods.IsHead(Request.Method))
                return new EmptyResult();

            await _ftp.CopyToAsync(
                relativePath,
                start,
                end - start + 1,
                Response.Body,
                cancellationToken);

            return new EmptyResult();
        }
        catch (FileStorageException ex)
        {
            _logger.LogWarning(ex, "Could not stream {Path}", relativePath);

            return StatusCode(502, "File could not be read from FTP.");
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
    }

    // ------------------------------------------------
    // MAPPING HELPERS
    // ------------------------------------------------

    private FileItemVm ToItem(FtpEntry entry)
    {
        var isFolder = entry.IsFolder;

        return new FileItemVm
        {
            Name = entry.Name,
            Path = entry.Path,
            Ext = entry.Ext,
            Kind = isFolder ? "folder" : MimeTypes.Kind(entry.Ext),
            Label = isFolder ? "" : MimeTypes.Label(entry.Ext),
            IsFolder = isFolder,
            Size = entry.Size,
            SizeText = isFolder ? "Folder" : MimeTypes.Size(entry.Size),
            ModifiedText = entry.Modified?.ToString("dd MMM yyyy") ?? "—",
            ViewUrl = isFolder
                ? Url.Action(nameof(Index), new { path = entry.Path }) ?? ""
                : BuildViewUrl(entry),
            DownloadUrl = isFolder
                ? ""
                : Url.Action(nameof(Download), new { path = entry.Path }) ?? ""
        };
    }

    private object ToDto(FtpEntry entry)
    {
        var isFolder = entry.IsFolder;

        return new
        {
            name = entry.Name,
            path = entry.Path,
            ext = entry.Ext,
            kind = isFolder ? "folder" : MimeTypes.Kind(entry.Ext),
            label = isFolder ? "" : MimeTypes.Label(entry.Ext),
            folder = isFolder,
            size = entry.Size,
            sizeText = isFolder ? "Folder" : MimeTypes.Size(entry.Size),
            modified = entry.Modified?.ToString("dd MMM yyyy") ?? "—",
            parent = FtpPaths.Parent(entry.Path),
            viewUrl = isFolder
                ? Url.Action(nameof(Index), new { path = entry.Path }) ?? ""
                : BuildViewUrl(entry),
            downloadUrl = isFolder
                ? ""
                : Url.Action(nameof(Download), new { path = entry.Path }) ?? ""
        };
    }

    private static object ToDto(IndexStatus status)
    {
        return new
        {
            running = status.Running,
            complete = status.Complete,
            folders = status.Folders,
            files = status.Files,
            seconds = status.Seconds,
            builtUtc = status.BuiltUtc,
            error = status.Error
        };
    }

    private string BuildViewUrl(FtpEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return FtpPaths.PublicUrl(_options.PublicBaseUrl, entry.Path);
        }

        return Url.Action(nameof(Raw), new { path = entry.Path }) ?? "";
    }

    private static IOrderedEnumerable<FtpEntry> SortFiles(
        IEnumerable<FtpEntry> files,
        string sort,
        string dir)
    {
        var descending = dir == "desc";

        return sort switch
        {
            "date" => descending
                ? files.OrderByDescending(entry =>
                    entry.Modified ?? DateTime.MinValue)
                : files.OrderBy(entry =>
                    entry.Modified ?? DateTime.MinValue),

            "size" => descending
                ? files.OrderByDescending(entry => entry.Size)
                : files.OrderBy(entry => entry.Size),

            _ => descending
                ? files.OrderByDescending(
                    entry => entry.Name,
                    StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(
                    entry => entry.Name,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<FtpEntry> FilterKind(
        IEnumerable<FtpEntry> entries,
        string kind)
    {
        if (kind == "all")
            return entries;

        if (kind == "folder")
            return entries.Where(entry => entry.IsFolder);

        return entries.Where(entry =>
            !entry.IsFolder &&
            MimeTypes.Kind(entry.Ext)
                .Equals(kind, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeKind(string? kind)
    {
        return (kind ?? "").ToLowerInvariant() switch
        {
            "folder" => "folder",
            "video" => "video",
            "audio" => "audio",
            "pdf" => "pdf",
            "excel" => "excel",
            "image" => "image",
            "archive" => "archive",
            "doc" => "doc",
            "other" => "other",
            _ => "all"
        };
    }
}
