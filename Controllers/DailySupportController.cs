using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class DailySupportController : Controller
{
    private readonly IConfiguration _configuration;

    public DailySupportController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool IsAjax =>
        DynamicTableHelper.IsAjaxRequest(
            Request);


    private static readonly string[] AllowedStatuses =
    {
        "Open",
        "In Progress",
        "Pending",
        "Completed",
        "Cancelled"
    };


    private static readonly string[] AllowedPriorities =
    {
        "Low",
        "Medium",
        "High",
        "Urgent"
    };


    private static readonly (string From, string To)[] AllowedTransitions =
    {
        ("Open", "In Progress"),
        ("Open", "Cancelled"),
        ("In Progress", "Pending"),
        ("In Progress", "Completed"),
        ("In Progress", "Cancelled"),
        ("Pending", "In Progress"),
        ("Pending", "Completed"),
        ("Pending", "Cancelled"),
        ("Completed", "Cancelled")
    };


    private static readonly (string Name, string Display, bool Required)[]
        FormColumns =
    {
        (DatabaseMapping.DailySupport.SupportDate,
            "Support Date", true),
        (DatabaseMapping.DailySupport.ClientId,
            "Client", true),
        (DatabaseMapping.DailySupport.SupportType,
            "Support Type", true),
        (DatabaseMapping.DailySupport.Subject,
            "Subject", true),
        (DatabaseMapping.DailySupport.Description,
            "Description", true),
       
        (DatabaseMapping.DailySupport.Status,
            "Status", true),
        (DatabaseMapping.DailySupport.Priority,
            "Priority", true),
        (DatabaseMapping.DailySupport.FollowUpDate,
            "Follow-Up Date", false),
        (DatabaseMapping.DailySupport.Remarks,
            "Remarks", false)
    };


    // ============================================
    // SUPPORT LIST
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(
                500,
                "Database connection string is missing.");
        }


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        var model =
            await BuildIndexAsync(
                connection,
                columns,
                search);


        ViewBag.Search = search;

        return View(model);
    }


    private static async Task<DailySupportViewModel> BuildIndexAsync(
        SqlConnection connection,
        HashSet<string> columns,
        string? search)
    {
        var model =
            new DailySupportViewModel();

        model.AvailableColumns = columns;


        // ========================================
        // ACTIVE CLIENTS FOR DROPDOWN
        // ========================================

        string clientQuery = $@"
            SELECT
                {DatabaseMapping.ClientMaster.Id},
                {DatabaseMapping.ClientMaster.ClientName}

            FROM
                {DatabaseMapping.ClientMaster.Table}

            WHERE
                {DatabaseMapping.ClientMaster.IsActive}
                = 1

            ORDER BY
                {DatabaseMapping.ClientMaster.ClientName}";


        using (SqlCommand clientCommand =
            new SqlCommand(clientQuery, connection))
        {
            clientCommand.CommandTimeout = 0;
            using SqlDataReader clientReader =
                await clientCommand.ExecuteReaderAsync();

            while (await clientReader.ReadAsync())
            {
                model.Clients.Add(
                    new LookupOptionViewModel
                    {
                        Id =
                            Convert.ToInt32(
                                clientReader[0]),
                        Name =
                            clientReader[1]
                                .ToString() ?? ""
                    });
            }
        }


        // ========================================
        // KPI COUNTS
        // ========================================

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            string kpiQuery = $@"
                SELECT
                    COUNT(*) AS TotalRecords,
                    SUM(CASE WHEN {DatabaseMapping.DailySupport.Status}
                        = 'Open' THEN 1 ELSE 0 END)
                        AS OpenCount,
                    SUM(CASE WHEN {DatabaseMapping.DailySupport.Status}
                        = 'In Progress' THEN 1 ELSE 0 END)
                        AS InProgressCount,
                    SUM(CASE WHEN {DatabaseMapping.DailySupport.Status}
                        = 'Completed' THEN 1 ELSE 0 END)
                        AS CompletedCount

                FROM
                    {DatabaseMapping.DailySupport.Table}";


            using (SqlCommand kpiCommand =
                new SqlCommand(kpiQuery, connection))
            {
                kpiCommand.CommandTimeout = 0;
                using SqlDataReader kpiReader =
                    await kpiCommand.ExecuteReaderAsync();

                if (await kpiReader.ReadAsync())
                {
                    model.TotalRecords =
                        Convert.ToInt32(
                            kpiReader["TotalRecords"]);

                    model.OpenCount =
                        kpiReader["OpenCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["OpenCount"]);

                    model.InProgressCount =
                        kpiReader["InProgressCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["InProgressCount"]);

                    model.CompletedCount =
                        kpiReader["CompletedCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["CompletedCount"]);
                }
            }
        }
        else
        {
            string countQuery = $@"
                SELECT
                    COUNT(*) AS TotalRecords

                FROM
                    {DatabaseMapping.DailySupport.Table}";


            using (SqlCommand countCommand =
                new SqlCommand(countQuery, connection))
            {
                countCommand.CommandTimeout = 0;
                using SqlDataReader countReader =
                    await countCommand.ExecuteReaderAsync();

                if (await countReader.ReadAsync())
                {
                    model.TotalRecords =
                        Convert.ToInt32(
                            countReader["TotalRecords"]);
                }
            }
        }


        // ========================================
        // SUPPORT RECORDS (JOINS FOR DISPLAY)
        // ========================================

        var selectColumns =
            FormColumns
                .Select(c => c.Name)
                .Where(columns.Contains)
                .ToList();

        selectColumns.Add(
            DatabaseMapping.DailySupport.StartTime);

        selectColumns.Add(
            DatabaseMapping.DailySupport.EndTime);

        selectColumns.Add(
            DatabaseMapping.DailySupport.EntryOn);

        selectColumns =
            selectColumns
                .Where(columns.Contains)
                .ToList();

        var selectParts =
            selectColumns
                .Select(c => $"s.{c}")
                .ToList();

        string selectedColumnList =
            selectParts.Count > 0
                ? string.Join(", ", selectParts)
                : $"s.{DatabaseMapping.DailySupport.Id}";


        var searchParts =
            new List<string>();

        foreach (string candidate in new[]
        {
            DatabaseMapping.DailySupport.Subject,
            DatabaseMapping.DailySupport.SupportType,
            DatabaseMapping.DailySupport.Description,
            DatabaseMapping.DailySupport.Remarks,
            DatabaseMapping.DailySupport.Status
        })
        {
            if (columns.Contains(candidate))
            {
                searchParts.Add(
                    $"s.{candidate} LIKE '%' + @Search + '%'");
            }
        }

        searchParts.Add(
            $"c.{DatabaseMapping.ClientMaster.ClientName}"
                + " LIKE '%' + @Search + '%'");

        searchParts.Add(
            $"u.{DatabaseMapping.LoginUsers.UserName}"
                + " LIKE '%' + @Search + '%'");

        if (columns.Contains(
                DatabaseMapping.DailySupport.Id))
        {
            searchParts.Add(
                $"CAST(s.{DatabaseMapping.DailySupport.Id}"
                    + " AS NVARCHAR(10)) LIKE '%' + @Search + '%'");
        }


        string query = $@"
            SELECT
                s.{DatabaseMapping.DailySupport.Id},
                {selectedColumnList},
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName,
                u.{DatabaseMapping.LoginUsers.UserName}
                    AS UserName

            FROM
                {DatabaseMapping.DailySupport.Table} s

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = s.{DatabaseMapping.DailySupport.ClientId}

            INNER JOIN
                {DatabaseMapping.LoginUsers.Table} u
                ON u.{DatabaseMapping.LoginUsers.Id}
                = s.{DatabaseMapping.DailySupport.UserId}

            WHERE
                (@Search = '' OR
                    {string.Join(" OR ", searchParts)})

            ORDER BY
                s.{DatabaseMapping.DailySupport.Id} ASC";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.Add(
            new SqlParameter(
                "@Search",
                System.Data.SqlDbType.NVarChar,
                100)
            {
                Value =
                    search?.Trim() ?? ""
            });


        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new MasterRowViewModel();

            row.Values["Id"] =
                reader[
                    DatabaseMapping.DailySupport.Id]
                .ToString() ?? "";

            foreach (string name in selectColumns)
            {
                object value = reader[name];

                switch (name)
                {
                    case string _ when name ==
                        DatabaseMapping.DailySupport.SupportDate:
                        row.Values[name] =
                            value == DBNull.Value
                                ? ""
                                : Convert.ToDateTime(value)
                                    .ToString("dd/MM/yyyy");

                        if (value != DBNull.Value)
                        {
                            row.Values["SupportDateISO"] =
                                Convert.ToDateTime(value)
                                    .ToString("yyyy-MM-dd");
                        }
                        break;

                    case string _ when name ==
                        DatabaseMapping.DailySupport.FollowUpDate:
                        row.Values[name] =
                            FormatDate(value);

                        if (value != DBNull.Value)
                        {
                            row.Values["FollowUpDateISO"] =
                                Convert.ToDateTime(value)
                                    .ToString("yyyy-MM-dd");
                        }
                        break;

                    case string _ when name ==
                        DatabaseMapping.DailySupport.StartTime:
                    case string _ when name ==
                        DatabaseMapping.DailySupport.EndTime:
                        row.Values[name] =
                            FormatTime(value);
                        break;

                    case string _ when name ==
                        DatabaseMapping.DailySupport.EntryOn:
                        row.Values[name] =
                            value == DBNull.Value
                                ? ""
                                : Convert.ToDateTime(value)
                                    .ToString("dd/MM/yyyy hh:mm tt");
                        break;

                    default:
                        row.Values[name] =
                            value == DBNull.Value
                                ? ""
                                : value.ToString() ?? "";
                        break;
                }
            }

            row.Values["ClientName"] =
                reader["ClientName"]
                .ToString() ?? "";

            row.Values["UserName"] =
                reader["UserName"]
                .ToString() ?? "";

            model.Supports.Add(row);
        }


        reader.Close();


        return model;
    }


    // ============================================
    // CREATE
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Create(
        string? returnUrl = null)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(
                500,
                "Database connection string is missing.");
        }


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        (HashSet<string> columns,
         List<LookupOptionViewModel> clients) =
            await LoadFormDataAsync(connection);


        var model =
            new DailySupportViewModel
            {
                AvailableColumns = columns,
                Clients = clients
            };

        return View(model);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        var form = Request.Form;

        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        (HashSet<string> columns,
         List<LookupOptionViewModel> clients) =
            await LoadFormDataAsync(connection);


        string? loggedInUserId =
            DynamicTableHelper.GetLoggedInUserId(User);


        if (string.IsNullOrWhiteSpace(loggedInUserId))
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "User authentication is required."
                });
            }

            return Unauthorized();
        }


        var fieldErrors =
            ValidateFields(
                columns,
                form,
                null,
                null,
                out DateTime supportDate,
                out DateTime? followUpDate,
                out DateTime? startTime,
                out DateTime? endTime);


        if (fieldErrors.Count > 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        fieldErrors.Values.First()
                });
            }

            return View(new DailySupportViewModel
            {
                AvailableColumns = columns,
                Clients = clients,
                FormValues =
                    CollectFormValues(form),
                FieldErrors = fieldErrors
            });
        }


        var insertColumns =
            new List<string>();

        var insertPlaceholders =
            new List<string>();

        foreach (var (name, _, _) in FormColumns)
        {
            if (columns.Contains(name))
            {
                insertColumns.Add(name);
                insertPlaceholders.Add($"@{name}");
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            insertColumns.Add(
                DatabaseMapping.DailySupport.StartTime);
            insertPlaceholders.Add("@StartTime");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            insertColumns.Add(
                DatabaseMapping.DailySupport.EndTime);
            insertPlaceholders.Add("@EndTime");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.UserId))
        {
            insertColumns.Add(
                DatabaseMapping.DailySupport.UserId);
            insertPlaceholders.Add("@UserId");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EntryOn))
        {
            insertColumns.Add(
                DatabaseMapping.DailySupport.EntryOn);
            insertPlaceholders.Add("GETDATE()");
        }


        string query = $@"
            INSERT INTO
                {DatabaseMapping.DailySupport.Table}
                (
                    {string.Join(", ", insertColumns)}
                )

            VALUES
                (
                    {string.Join(", ", insertPlaceholders)}
                )";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        AddFormParameters(
            columns,
            command,
            form,
            supportDate,
            followUpDate,
            startTime,
            endTime,
            Convert.ToInt32(
                loggedInUserId));


        await command.ExecuteNonQueryAsync();


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message =
                    "Support record added successfully."
            });
        }

        TempData["Success"] =
            "Support record added successfully.";

        return RedirectToAction(nameof(Index));
    }


    // ============================================
    // EDIT
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id)
    {
        var form = Request.Form;

        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                columns,
                id);


        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        var fieldErrors =
            ValidateFields(
                columns,
                form,
                existingStart,
                existingEnd,
                out DateTime supportDate,
                out DateTime? followUpDate,
                out DateTime? startTime,
                out DateTime? endTime);


        string newStatus =
            form[
                DatabaseMapping.DailySupport.Status]
                .ToString()
                .Trim();


        if (!string.Equals(
                existingStatus,
                newStatus,
                StringComparison.OrdinalIgnoreCase)
            && !IsAllowedTransition(
                existingStatus,
                newStatus))
        {
            fieldErrors[
                DatabaseMapping.DailySupport.Status] =
                    $"Status transition from "
                    + $"'{existingStatus}' to '{newStatus}'"
                    + " is not allowed.";
        }


        if (fieldErrors.Count > 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        fieldErrors.Values.First()
                });
            }

            var formValues =
                CollectFormValues(form);

            if (string.IsNullOrWhiteSpace(
                    formValues[
                        DatabaseMapping.DailySupport.StartTime])
                && startTime.HasValue)
            {
                formValues[
                    DatabaseMapping.DailySupport.StartTime] =
                        startTime.Value.ToString("HH:mm");
            }

            if (string.IsNullOrWhiteSpace(
                    formValues[
                        DatabaseMapping.DailySupport.EndTime])
                && endTime.HasValue)
            {
                formValues[
                    DatabaseMapping.DailySupport.EndTime] =
                        endTime.Value.ToString("HH:mm");
            }

            var model =
                await BuildIndexAsync(
                    connection,
                    columns,
                    null);

            model.FormValues = formValues;
            model.FieldErrors = fieldErrors;
            model.OpenEditModal = true;
            model.EditPreviousStatus = existingStatus;

            return View("Index", model);
        }


        var setParts =
            new List<string>();

        foreach (var (name, _, _) in FormColumns)
        {
            if (name ==
                DatabaseMapping.DailySupport.StartTime
                || name ==
                DatabaseMapping.DailySupport.EndTime)
            {
                continue;
            }

            if (columns.Contains(name))
            {
                setParts.Add($"{name} = @{name}");
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.StartTime}"
                    + " = @StartTime");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.EndTime}"
                    + " = @EndTime");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.DailySupport.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.DailySupport.Id} = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.Add(
            new SqlParameter(
                "@Id",
                System.Data.SqlDbType.Int)
            {
                Value = id
            });

        AddFormParameters(
            columns,
            command,
            form,
            supportDate,
            followUpDate,
            startTime,
            endTime,
            null);


        await command.ExecuteNonQueryAsync();


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message =
                    "Support record updated successfully."
            });
        }

        TempData["Success"] =
            "Support record updated successfully.";

        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
        int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                columns,
                id);


        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        bool fromOpen =
            string.Equals(
                existingStatus,
                "Open",
                StringComparison.OrdinalIgnoreCase);

        bool fromPending =
            string.Equals(
                existingStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase);

        if (!fromOpen && !fromPending)
        {
            const string message =
                "Cannot start support. "
                    + "Allowed only from Open or Pending.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        var setParts =
            new List<string>();

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.Status}"
                    + " = 'In Progress'");
        }

        if (fromOpen
            && columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.StartTime}"
                    + " = GETDATE()");
        }

        if (setParts.Count == 0)
        {
            const string message =
                "Cannot start support:"
                    + " required columns are missing.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.DailySupport.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.DailySupport.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            string message =
                fromOpen
                    ? "Support started."
                    : "Support resumed.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message
                });
            }

            TempData["Success"] = message;
        }


        return RedirectToAction(
            "Index",
            "DailySupport");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(
        int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                columns,
                id);


        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        if (!string.Equals(
                existingStatus,
                "In Progress",
                StringComparison.OrdinalIgnoreCase))
        {
            const string message =
                "Cannot pause support. "
                    + "Allowed only from In Progress.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        var setParts =
            new List<string>();

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.Status}"
                    + " = 'Pending'");
        }

        if (setParts.Count == 0)
        {
            const string message =
                "Cannot pause support:"
                    + " required columns are missing.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.DailySupport.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.DailySupport.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            const string message =
                "Support paused. Status updated to Pending.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message
                });
            }

            TempData["Success"] = message;
        }


        return RedirectToAction(
            "Index",
            "DailySupport");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                columns,
                id);


        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        if (!string.Equals(
                existingStatus,
                "In Progress",
                StringComparison.OrdinalIgnoreCase))
        {
            const string message =
                "Cannot complete support. "
                    + "Allowed only from In Progress.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        var setParts =
            new List<string>();

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.Status}"
                    + " = 'Completed'");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.EndTime}"
                    + " = GETDATE()");
        }

        if (setParts.Count == 0)
        {
            const string message =
                "Cannot complete support:"
                    + " required columns are missing.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.DailySupport.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.DailySupport.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            const string message =
                "Support completed. Status updated to Completed.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message
                });
            }

            TempData["Success"] = message;
        }


        return RedirectToAction(
            "Index",
            "DailySupport");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                columns,
                id);


        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        bool fromOpen =
            string.Equals(
                existingStatus,
                "Open",
                StringComparison.OrdinalIgnoreCase);

        bool fromInProgress =
            string.Equals(
                existingStatus,
                "In Progress",
                StringComparison.OrdinalIgnoreCase);

        if (!fromOpen && !fromInProgress)
        {
            const string message =
                "Cannot cancel support. "
                    + "Allowed only from Open or In Progress.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        var setParts =
            new List<string>();

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            setParts.Add(
                $"{DatabaseMapping.DailySupport.Status}"
                    + " = 'Cancelled'");
        }

        if (setParts.Count == 0)
        {
            const string message =
                "Cannot cancel support:"
                    + " required columns are missing.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "DailySupport");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.DailySupport.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.DailySupport.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            const string message =
                "Support cancelled. Status updated to Cancelled.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message
                });
            }

            TempData["Success"] = message;
        }


        return RedirectToAction(
            "Index",
            "DailySupport");
    }


    // ============================================
    // DELETE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        string query = $@"
            DELETE FROM
                {DatabaseMapping.DailySupport.Table}

            WHERE
                {DatabaseMapping.DailySupport.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Support record deleted successfully."
                });
            }

            TempData["Success"] =
                "Support record deleted successfully.";
        }


        return RedirectToAction(
            "Index",
            "DailySupport");
    }


    // ============================================
    // HELPERS
    // ============================================

    private static async Task<(
        HashSet<string> columns,
        List<LookupOptionViewModel> clients)>
        LoadFormDataAsync(
            SqlConnection connection)
    {
        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.DailySupport.Table);


        var clients =
            new List<LookupOptionViewModel>();

        string clientQuery = $@"
            SELECT
                {DatabaseMapping.ClientMaster.Id},
                {DatabaseMapping.ClientMaster.ClientName}

            FROM
                {DatabaseMapping.ClientMaster.Table}

            WHERE
                {DatabaseMapping.ClientMaster.IsActive}
                = 1

            ORDER BY
                {DatabaseMapping.ClientMaster.ClientName}";


        using SqlCommand command =
            new SqlCommand(clientQuery, connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            clients.Add(
                new LookupOptionViewModel
                {
                    Id =
                        Convert.ToInt32(
                            reader[0]),
                    Name =
                        reader[1]
                            .ToString() ?? ""
                });
        }


        return (columns, clients);
    }


    private static bool IsAllowedTransition(
        string from,
        string to)
    {
        if (string.Equals(
                from,
                to,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var (allowedFrom, allowedTo)
                in AllowedTransitions)
        {
            if (string.Equals(
                    from,
                    allowedFrom,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    to,
                    allowedTo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }


    private static async Task<(bool found, string status,
        DateTime? startTime, DateTime? endTime)>
        ReadCurrentAsync(
            SqlConnection connection,
            HashSet<string> columns,
            int id)
    {
        var select = new List<string>
        {
            DatabaseMapping.DailySupport.Id
        };

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            select.Add(
                DatabaseMapping.DailySupport.Status);
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            select.Add(
                DatabaseMapping.DailySupport.StartTime);
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            select.Add(
                DatabaseMapping.DailySupport.EndTime);
        }


        string query = $@"
            SELECT
                {string.Join(", ", select)}

            FROM
                {DatabaseMapping.DailySupport.Table}

            WHERE
                {DatabaseMapping.DailySupport.Id} = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        using SqlDataReader reader =
            await command.ExecuteReaderAsync();


        if (!await reader.ReadAsync())
        {
            return (false, "", null, null);
        }


        string status =
            columns.Contains(
                DatabaseMapping.DailySupport.Status)
                ? (reader[
                    DatabaseMapping.DailySupport.Status]
                    .ToString() ?? "")
                : "";

        DateTime? startTime =
            columns.Contains(
                DatabaseMapping.DailySupport.StartTime)
                ? ReadAsDateTime(
                    reader[
                        DatabaseMapping
                            .DailySupport.StartTime])
                : null;

        DateTime? endTime =
            columns.Contains(
                DatabaseMapping.DailySupport.EndTime)
                ? ReadAsDateTime(
                    reader[
                        DatabaseMapping
                            .DailySupport.EndTime])
                : null;

        return (true, status, startTime, endTime);
    }


    private static Dictionary<string, string> ValidateFields(
        HashSet<string> columns,
        IFormCollection form,
        DateTime? existingStart,
        DateTime? existingEnd,
        out DateTime supportDate,
        out DateTime? followUpDate,
        out DateTime? startTime,
        out DateTime? endTime)
    {
        var errors =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        supportDate = default;
        followUpDate = null;
        startTime = null;
        endTime = null;

        void AddError(string name, string message)
        {
            if (!errors.ContainsKey(name))
            {
                errors[name] = message;
            }
        }

        foreach (var (name, display, required) in FormColumns)
        {
            if (!columns.Contains(name)
                || !required)
            {
                continue;
            }

            string raw =
                form[name].ToString().Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                AddError(name, $"{display} is required.");
            }
            else if (name ==
                DatabaseMapping.DailySupport.ClientId
                && (!int.TryParse(
                        raw,
                        out int clientId)
                    || clientId <= 0))
            {
                AddError(
                    name,
                    $"Please select a valid {display}.");
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.SupportType))
        {
            string supportTypeRaw =
                form[
                    DatabaseMapping.DailySupport.SupportType]
                    .ToString()
                    .Trim();

            if (string.Equals(
                    supportTypeRaw,
                    "Other",
                    StringComparison.OrdinalIgnoreCase))
            {
                string otherText =
                    form["OtherSupportType"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(otherText))
                {
                    AddError(
                        "OtherSupportType",
                        "Other Support Type is required.");
                }
                else if (otherText.Length > 92)
                {
                    AddError(
                        "OtherSupportType",
                        "Other Support Type is too long"
                            + " (maximum 92 characters).");
                }
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.SupportDate)
            && !errors.ContainsKey(
                DatabaseMapping.DailySupport.SupportDate)
            && !DateTime.TryParse(
                form[
                    DatabaseMapping.DailySupport.SupportDate]
                    .ToString(),
                out supportDate))
        {
            AddError(
                DatabaseMapping.DailySupport.SupportDate,
                "Please select a valid Support Date.");
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            string status =
                form[
                    DatabaseMapping.DailySupport.Status]
                    .ToString()
                    .Trim();

            if (!string.IsNullOrWhiteSpace(status)
                && !AllowedStatuses.Contains(
                    status,
                    StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    DatabaseMapping.DailySupport.Status,
                    "Please select a valid Status.");
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Priority))
        {
            string priority =
                form[
                    DatabaseMapping.DailySupport.Priority]
                    .ToString()
                    .Trim();

            if (!string.IsNullOrWhiteSpace(priority)
                && !AllowedPriorities.Contains(
                    priority,
                    StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    DatabaseMapping.DailySupport.Priority,
                    "Please select a valid Priority.");
            }
        }

        string currentStatus =
            form[
                DatabaseMapping.DailySupport.Status]
                .ToString()
                .Trim();

        bool Is(string check) =>
            string.Equals(
                currentStatus,
                check,
                StringComparison.OrdinalIgnoreCase);

        if (columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            string startRaw =
                form[
                    DatabaseMapping.DailySupport.StartTime]
                    .ToString()
                    .Trim();

            if (string.IsNullOrWhiteSpace(startRaw))
            {
                if (Is("In Progress")
                    || Is("Pending")
                    || Is("Completed"))
                {
                    if (existingStart.HasValue)
                    {
                        startTime = existingStart;
                    }
                    else
                    {
                        AddError(
                            DatabaseMapping
                                .DailySupport.StartTime,
                            "Start Time is required for this status.");
                    }
                }
            }
            else
            {
                startTime =
                    ParseNullableTime(startRaw);

                if (!startTime.HasValue)
                {
                    AddError(
                        DatabaseMapping
                            .DailySupport.StartTime,
                        "Please enter a valid Start Time.");
                }
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            string endRaw =
                form[
                    DatabaseMapping.DailySupport.EndTime]
                    .ToString()
                    .Trim();

            if (Is("Pending")
                && !string.IsNullOrWhiteSpace(endRaw))
            {
                AddError(
                    DatabaseMapping.DailySupport.EndTime,
                    "End Time must be blank for Pending status.");
            }
            else if (string.IsNullOrWhiteSpace(endRaw))
            {
                if (Is("Completed"))
                {
                    if (existingEnd.HasValue)
                    {
                        endTime = existingEnd;
                    }
                    else
                    {
                        AddError(
                            DatabaseMapping
                                .DailySupport.EndTime,
                            "End Time is required for Completed status.");
                    }
                }
            }
            else
            {
                endTime =
                    ParseNullableTime(endRaw);

                if (!endTime.HasValue)
                {
                    AddError(
                        DatabaseMapping
                            .DailySupport.EndTime,
                        "Please enter a valid End Time.");
                }
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Remarks))
        {
            string remarksRaw =
                form[
                    DatabaseMapping.DailySupport.Remarks]
                    .ToString()
                    .Trim();

            if ((Is("Pending")
                    || Is("Completed")
                    || Is("Cancelled"))
                && string.IsNullOrWhiteSpace(remarksRaw))
            {
                AddError(
                    DatabaseMapping.DailySupport.Remarks,
                    "Remarks is required for this status.");
            }
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.FollowUpDate))
        {
            followUpDate =
                ParseNullableDate(
                    form[
                        DatabaseMapping
                            .DailySupport.FollowUpDate]
                        .ToString());
        }

        if (startTime.HasValue
            && endTime.HasValue
            && endTime.Value.TimeOfDay
                < startTime.Value.TimeOfDay)
        {
            AddError(
                DatabaseMapping
                    .DailySupport.EndTime,
                "End time cannot be earlier"
                    + " than start time.");
        }

        if (followUpDate.HasValue
            && !errors.ContainsKey(
                DatabaseMapping
                    .DailySupport.SupportDate)
            && followUpDate.Value.Date
                < supportDate.Date)
        {
            AddError(
                DatabaseMapping
                    .DailySupport.FollowUpDate,
                "Follow-up date cannot be earlier"
                    + " than support date.");
        }

        return errors;
    }


    private static Dictionary<string, string> CollectFormValues(
        IFormCollection form)
    {
        var values =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string name in new[]
        {
            DatabaseMapping.DailySupport.Id,
            DatabaseMapping.DailySupport.SupportDate,
            DatabaseMapping.DailySupport.ClientId,
            DatabaseMapping.DailySupport.SupportType,
            "OtherSupportType",
            DatabaseMapping.DailySupport.Subject,
            DatabaseMapping.DailySupport.Description,
            DatabaseMapping.DailySupport.Status,
            DatabaseMapping.DailySupport.Priority,
            DatabaseMapping.DailySupport.StartTime,
            DatabaseMapping.DailySupport.EndTime,
            DatabaseMapping.DailySupport.FollowUpDate,
            DatabaseMapping.DailySupport.Remarks
        })
        {
            values[name] =
                form[name].ToString().Trim();
        }

        return values;
    }


    private static DateTime? ParseNullableDate(
        string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(raw, out DateTime value)
            ? value
            : null;
    }


    private static DateTime? ParseNullableTime(
        string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(raw, out DateTime value))
        {
            return value;
        }

        return null;
    }


    private static string FormatTime(object value)
    {
        if (value == DBNull.Value
            || value == null)
        {
            return "";
        }

        if (value is TimeSpan timeSpan)
        {
            return DateTime.Today
                .Add(timeSpan)
                .ToString("hh:mm tt");
        }

        return Convert.ToDateTime(value)
            .ToString("hh:mm tt");
    }


    private static DateTime? ReadAsDateTime(object value)
    {
        if (value == DBNull.Value
            || value == null)
        {
            return null;
        }

        if (value is TimeSpan timeSpan)
        {
            return DateTime.Today.Add(timeSpan);
        }

        return Convert.ToDateTime(value);
    }


    private static string FormatDate(object value)
    {
        if (value == DBNull.Value
            || value == null)
        {
            return "";
        }

        return Convert.ToDateTime(value)
            .ToString("dd/MM/yyyy");
    }


    private static string BuildSupportType(
        IFormCollection form)
    {
        string raw =
            form[
                DatabaseMapping.DailySupport.SupportType]
                .ToString()
                .Trim();

        string value = raw;

        if (string.Equals(
                raw,
                "Other",
                StringComparison.OrdinalIgnoreCase))
        {
            string otherText =
                form["OtherSupportType"]
                    .ToString()
                    .Trim();

            value = "Other - " + otherText;
        }

        if (value.Length > 100)
        {
            value = value.Substring(0, 100);
        }

        return value;
    }


    private void AddFormParameters(
        HashSet<string> columns,
        SqlCommand command,
        IFormCollection form,
        DateTime supportDate,
        DateTime? followUpDate,
        DateTime? startTime,
        DateTime? endTime,
        int? userId)
    {
        if (columns.Contains(
                DatabaseMapping.DailySupport.SupportDate))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@SupportDate",
                    System.Data.SqlDbType.DateTime)
                {
                    Value = supportDate
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.ClientId))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@ClientId",
                    System.Data.SqlDbType.Int)
                {
                    Value = Convert.ToInt32(
                        form[
                            DatabaseMapping.DailySupport.ClientId]
                            .ToString())
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.UserId)
            && userId.HasValue)
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@UserId",
                    System.Data.SqlDbType.Int)
                {
                    Value = userId.Value
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.SupportType))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@SupportType",
                    System.Data.SqlDbType.NVarChar,
                    100)
                {
                    Value = BuildSupportType(form)
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Subject))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Subject",
                    System.Data.SqlDbType.NVarChar,
                    300)
                {
                    Value = form[
                        DatabaseMapping.DailySupport.Subject]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Description))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Description",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.DailySupport.Description]
                        .ToString().Trim()
                });
        }

      

        if (columns.Contains(
                DatabaseMapping.DailySupport.Status))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Status",
                    System.Data.SqlDbType.NVarChar,
                    30)
                {
                    Value = form[
                        DatabaseMapping.DailySupport.Status]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Priority))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Priority",
                    System.Data.SqlDbType.NVarChar,
                    20)
                {
                    Value = form[
                        DatabaseMapping.DailySupport.Priority]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.StartTime))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@StartTime",
                    System.Data.SqlDbType.DateTime)
                {
                    Value =
                        startTime.HasValue
                            ? startTime.Value
                            : DBNull.Value
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.EndTime))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@EndTime",
                    System.Data.SqlDbType.DateTime)
                {
                    Value =
                        endTime.HasValue
                            ? endTime.Value
                            : DBNull.Value
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.FollowUpDate))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@FollowUpDate",
                    System.Data.SqlDbType.DateTime)
                {
                    Value =
                        followUpDate.HasValue
                            ? followUpDate.Value
                            : DBNull.Value
                });
        }

        if (columns.Contains(
                DatabaseMapping.DailySupport.Remarks))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Remarks",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.DailySupport.Remarks]
                        .ToString().Trim()
                });
        }
    }
}