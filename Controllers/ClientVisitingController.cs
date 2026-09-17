using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class ClientVisitingController : Controller
{
    private readonly IConfiguration _configuration;

    public ClientVisitingController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool IsAjax =>
        DynamicTableHelper.IsAjaxRequest(
            Request);


    private static readonly string[] AllowedStatuses =
    {
        "Planned",
        "Completed",
        "Cancelled"
    };


    private static readonly string[] AllowedVisitTypes =
    {
        "New Client",
        "Follow Up",
        "Support Visit",
        "Sales Visit",
        "Service Visit",
        "Collection Visit",
        "Other"
    };


    private static readonly (string Name, string Display, bool Required)[]
        FormColumns =
    {
        (DatabaseMapping.ClientVisiting.VisitDate,
            "Visit Date", true),
        (DatabaseMapping.ClientVisiting.ClientId,
            "Client", true),
        (DatabaseMapping.ClientVisiting.VisitType,
            "Visit Type", true),
        (DatabaseMapping.ClientVisiting.PersonMet,
            "Person Met", false),
        (DatabaseMapping.ClientVisiting.Subject,
            "Subject", true),
        (DatabaseMapping.ClientVisiting.Discussion,
            "Discussion", false),
        (DatabaseMapping.ClientVisiting.Outcome,
            "Outcome", false),
        (DatabaseMapping.ClientVisiting.NextAction,
            "Next Action", false),
        (DatabaseMapping.ClientVisiting.FollowUpDate,
            "Follow-Up Date", false),
        (DatabaseMapping.ClientVisiting.Remarks,
            "Remarks", false),
        (DatabaseMapping.ClientVisiting.Status,
            "Status", true)
    };


    // ============================================
    // VISIT LIST
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


        var model =
            await BuildIndexAsync(
                connection,
                search);


        ViewBag.Search = search;

        return View(model);
    }


    private static async Task<ClientVisitingViewModel> BuildIndexAsync(
        SqlConnection connection,
        string? search)
    {
        var model =
            new ClientVisitingViewModel();

        (model.AvailableColumns,
         model.Clients) =
            await LoadFormDataAsync(connection);

        model.Meetings =
            await LoadMeetingsAsync(
                connection);


        var columns =
            model.AvailableColumns;


        // ========================================
        // KPI COUNTS
        // ========================================

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Status))
        {
            string kpiQuery = $@"
                SELECT
                    COUNT(*) AS TotalRecords,
                    SUM(CASE WHEN {DatabaseMapping.ClientVisiting.Status}
                        = 'Planned' THEN 1 ELSE 0 END)
                        AS PlannedCount,
                    SUM(CASE WHEN {DatabaseMapping.ClientVisiting.Status}
                        = 'Completed' THEN 1 ELSE 0 END)
                        AS CompletedCount,
                    SUM(CASE WHEN {DatabaseMapping.ClientVisiting.Status}
                        = 'Cancelled' THEN 1 ELSE 0 END)
                        AS CancelledCount

                FROM
                    {DatabaseMapping.ClientVisiting.Table}";


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

                    model.PlannedCount =
                        kpiReader["PlannedCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["PlannedCount"]);

                    model.CompletedCount =
                        kpiReader["CompletedCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["CompletedCount"]);

                    model.CancelledCount =
                        kpiReader["CancelledCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                kpiReader["CancelledCount"]);
                }
            }
        }
        else
        {
            string countQuery = $@"
                SELECT
                    COUNT(*) AS TotalRecords

                FROM
                    {DatabaseMapping.ClientVisiting.Table}";


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
        // VISIT RECORDS (JOINS FOR DISPLAY)
        // ========================================

        var selectColumns =
            FormColumns
                .Select(c => c.Name)
                .Where(columns.Contains)
                .ToList();

        selectColumns.Add(
            DatabaseMapping.ClientVisiting.EntryOn);

        selectColumns.Add(
            DatabaseMapping.ClientVisiting.PreviousMeetingId);

        selectColumns =
            selectColumns
                .Where(columns.Contains)
                .ToList();

        var selectParts =
            selectColumns
                .Select(c => $"v.{c}")
                .ToList();

        string selectedColumnList =
            selectParts.Count > 0
                ? string.Join(", ", selectParts)
                : $"v.{DatabaseMapping.ClientVisiting.Id}";


        var searchParts =
            new List<string>();

        foreach (string candidate in new[]
        {
            DatabaseMapping.ClientVisiting.Subject,
            DatabaseMapping.ClientVisiting.VisitType,
            DatabaseMapping.ClientVisiting.PersonMet,
            DatabaseMapping.ClientVisiting.Discussion,
            DatabaseMapping.ClientVisiting.Outcome,
            DatabaseMapping.ClientVisiting.NextAction,
            DatabaseMapping.ClientVisiting.Remarks,
            DatabaseMapping.ClientVisiting.Status
        })
        {
            if (columns.Contains(candidate))
            {
                searchParts.Add(
                    $"v.{candidate} LIKE '%' + @Search + '%'");
            }
        }

        searchParts.Add(
            $"c.{DatabaseMapping.ClientMaster.ClientName}"
                + " LIKE '%' + @Search + '%'");

        searchParts.Add(
            $"u.{DatabaseMapping.LoginUsers.UserName}"
                + " LIKE '%' + @Search + '%'");

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Id))
        {
            searchParts.Add(
                $"CAST(v.{DatabaseMapping.ClientVisiting.Id}"
                    + " AS NVARCHAR(10)) LIKE '%' + @Search + '%'");
        }


        string query = $@"
            SELECT
                v.{DatabaseMapping.ClientVisiting.Id},
                {selectedColumnList},
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName,
                u.{DatabaseMapping.LoginUsers.UserName}
                    AS UserName

            FROM
                {DatabaseMapping.ClientVisiting.Table} v

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}

            INNER JOIN
                {DatabaseMapping.LoginUsers.Table} u
                ON u.{DatabaseMapping.LoginUsers.Id}
                = v.{DatabaseMapping.ClientVisiting.UserId}

            WHERE
                (@Search = '' OR
                    {string.Join(" OR ", searchParts)})

            ORDER BY
                v.{DatabaseMapping.ClientVisiting.Id} ASC";


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
                    DatabaseMapping.ClientVisiting.Id]
                .ToString() ?? "";

            foreach (string name in selectColumns)
            {
                object value = reader[name];

                switch (name)
                {
                    case string _ when name ==
                        DatabaseMapping.ClientVisiting.VisitDate:
                        row.Values[name] =
                            FormatDate(value);

                        if (value != DBNull.Value)
                        {
                            row.Values["VisitDateISO"] =
                                Convert.ToDateTime(value)
                                    .ToString("yyyy-MM-dd");
                        }
                        break;

                    case string _ when name ==
                        DatabaseMapping.ClientVisiting.FollowUpDate:
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
                        DatabaseMapping.ClientVisiting.EntryOn:
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

            model.Visits.Add(row);
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
            new ClientVisitingViewModel
            {
                AvailableColumns = columns,
                Clients = clients,
                Meetings =
                    await LoadMeetingsAsync(connection)
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
                out DateTime visitDate,
                out DateTime? followUpDate,
                out int previousMeetingId);


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

            return View(new ClientVisitingViewModel
            {
                AvailableColumns = columns,
                Clients = clients,
                Meetings =
                    await LoadMeetingsAsync(connection),
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
                DatabaseMapping.ClientVisiting.PreviousMeetingId))
        {
            insertColumns.Add(
                DatabaseMapping.ClientVisiting.PreviousMeetingId);
            insertPlaceholders.Add("@PreviousMeetingId");
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.UserId))
        {
            insertColumns.Add(
                DatabaseMapping.ClientVisiting.UserId);
            insertPlaceholders.Add("@UserId");
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.EntryOn))
        {
            insertColumns.Add(
                DatabaseMapping.ClientVisiting.EntryOn);
            insertPlaceholders.Add("GETDATE()");
        }


        string query = $@"
            INSERT INTO
                {DatabaseMapping.ClientVisiting.Table}
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
            visitDate,
            followUpDate,
            previousMeetingId,
            true,
            Convert.ToInt32(
                loggedInUserId));


        await command.ExecuteNonQueryAsync();


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message =
                    "Visit record added successfully."
            });
        }

        TempData["Success"] =
            "Visit record added successfully.";

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


        (HashSet<string> columns,
         List<LookupOptionViewModel> clients) =
            await LoadFormDataAsync(connection);


        var fieldErrors =
            ValidateFields(
                columns,
                form,
                out DateTime visitDate,
                out DateTime? followUpDate,
                out int previousMeetingId);


        bool meetingModePosted =
            columns.Contains(
                DatabaseMapping.ClientVisiting.PreviousMeetingId)
            && form.ContainsKey("MeetingMode");


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

            var model =
                await BuildIndexAsync(
                    connection,
                    null);

            model.FormValues =
                CollectFormValues(form);

            model.FieldErrors =
                fieldErrors;

            model.OpenModalMode = "Edit";

            return View("Index", model);
        }


        var setParts =
            new List<string>();

        foreach (var (name, _, _) in FormColumns)
        {
            if (columns.Contains(name))
            {
                setParts.Add($"{name} = @{name}");
            }
        }

        if (meetingModePosted)
        {
            setParts.Add(
                $"{DatabaseMapping.ClientVisiting.PreviousMeetingId}"
                    + " = @PreviousMeetingId");
        }


        string query = $@"
            UPDATE
                {DatabaseMapping.ClientVisiting.Table}

            SET
                {string.Join(", ", setParts)}

            WHERE
                {DatabaseMapping.ClientVisiting.Id} = @Id";


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
            visitDate,
            followUpDate,
            previousMeetingId,
            meetingModePosted,
            null);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = "Visit record not found."
                });
            }

            TempData["Error"] =
                "Visit record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Visit record updated successfully."
                });
            }

            TempData["Success"] =
                "Visit record updated successfully.";
        }

        return RedirectToAction(nameof(Index));
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
                {DatabaseMapping.ClientVisiting.Table}

            WHERE
                {DatabaseMapping.ClientVisiting.Id}
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
                    message = "Visit record not found."
                });
            }

            TempData["Error"] =
                "Visit record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Visit record deleted successfully."
                });
            }

            TempData["Success"] =
                "Visit record deleted successfully.";
        }


        return RedirectToAction(
            "Index",
            "ClientVisiting");
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
                DatabaseMapping.ClientVisiting.Table);


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


    private static async Task<List<LookupOptionViewModel>> LoadMeetingsAsync(
        SqlConnection connection)
    {
        var meetings =
            new List<LookupOptionViewModel>();

        string query = $@"
            SELECT
                v.{DatabaseMapping.ClientVisiting.Id},
                v.{DatabaseMapping.ClientVisiting.VisitDate},
                v.{DatabaseMapping.ClientVisiting.Subject},
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName

            FROM
                {DatabaseMapping.ClientVisiting.Table} v

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}

            ORDER BY
                v.{DatabaseMapping.ClientVisiting.Id} ASC";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int id =
                Convert.ToInt32(
                    reader[
                        DatabaseMapping.ClientVisiting.Id]);

            string date =
                FormatDate(
                    reader[
                        DatabaseMapping.ClientVisiting.VisitDate]);

            string subject =
                reader[
                    DatabaseMapping.ClientVisiting.Subject]
                .ToString() ?? "";

            string clientName =
                reader["ClientName"]
                .ToString() ?? "";

            meetings.Add(
                new LookupOptionViewModel
                {
                    Id = id,
                    Name =
                        $"MTG-{id:D3}"
                            + $" - {clientName}"
                            + $" - {date}"
                            + $" - {subject}"
                });
        }

        return meetings;
    }


    private static Dictionary<string, string> ValidateFields(
        HashSet<string> columns,
        IFormCollection form,
        out DateTime visitDate,
        out DateTime? followUpDate,
        out int previousMeetingId)
    {
        var errors =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        visitDate = default;
        followUpDate = null;
        previousMeetingId = 0;

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
                DatabaseMapping.ClientVisiting.ClientId
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
                DatabaseMapping.ClientVisiting.VisitType))
        {
            string visitTypeRaw =
                form[
                    DatabaseMapping.ClientVisiting.VisitType]
                    .ToString()
                    .Trim();

            if (string.Equals(
                    visitTypeRaw,
                    "Other",
                    StringComparison.OrdinalIgnoreCase))
            {
                string otherText =
                    form["OtherVisitType"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(otherText))
                {
                    AddError(
                        "OtherVisitType",
                        "Other Visit Type is required.");
                }
                else if (otherText.Length > 92)
                {
                    AddError(
                        "OtherVisitType",
                        "Other Visit Type is too long"
                            + " (maximum 92 characters).");
                }
            }
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.VisitDate)
            && !errors.ContainsKey(
                DatabaseMapping.ClientVisiting.VisitDate)
            && !DateTime.TryParse(
                form[
                    DatabaseMapping.ClientVisiting.VisitDate]
                    .ToString(),
                out visitDate))
        {
            AddError(
                DatabaseMapping.ClientVisiting.VisitDate,
                "Please select a valid Visit Date.");
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Status))
        {
            string status =
                form[
                    DatabaseMapping.ClientVisiting.Status]
                    .ToString()
                    .Trim();

            if (!string.IsNullOrWhiteSpace(status)
                && !AllowedStatuses.Contains(
                    status,
                    StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    DatabaseMapping.ClientVisiting.Status,
                    "Please select a valid Status.");
            }
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.FollowUpDate)
            && string.IsNullOrWhiteSpace(
                form[
                    DatabaseMapping.ClientVisiting.FollowUpDate]
                    .ToString()))
        {
            followUpDate = null;
        }
        else if (columns.Contains(
                DatabaseMapping.ClientVisiting.FollowUpDate))
        {
            followUpDate =
                ParseNullableDate(
                    form[
                        DatabaseMapping
                            .ClientVisiting.FollowUpDate]
                        .ToString());
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.PreviousMeetingId))
        {
            string meetingMode =
                form["MeetingMode"]
                    .ToString()
                    .Trim();

            string previousMeetingRaw =
                form["PreviousMeetingId"]
                    .ToString()
                    .Trim();

            if (string.Equals(
                    meetingMode,
                    "Add to Existing Meeting",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(
                        previousMeetingRaw,
                        out int selectedMeetingId)
                    || selectedMeetingId <= 0)
                {
                    AddError(
                        "PreviousMeetingId",
                        "Please select an existing meeting.");
                }
                else
                {
                    string idRaw =
                        form["id"]
                            .ToString()
                            .Trim();

                    if (int.TryParse(
                            idRaw,
                            out int postedId)
                        && postedId == selectedMeetingId)
                    {
                        AddError(
                            "PreviousMeetingId",
                            "A meeting cannot reference itself.");
                    }
                    else
                    {
                        previousMeetingId =
                            selectedMeetingId;
                    }
                }
            }
            else
            {
                previousMeetingId = 0;
            }
        }

        if (followUpDate.HasValue
            && !errors.ContainsKey(
                DatabaseMapping.ClientVisiting.VisitDate)
            && followUpDate.Value.Date
                < visitDate.Date)
        {
            AddError(
                DatabaseMapping.ClientVisiting.FollowUpDate,
                "Follow-up date cannot be earlier"
                    + " than visit date.");
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
            DatabaseMapping.ClientVisiting.Id,
            DatabaseMapping.ClientVisiting.VisitDate,
            DatabaseMapping.ClientVisiting.ClientId,
            DatabaseMapping.ClientVisiting.VisitType,
            "OtherVisitType",
            DatabaseMapping.ClientVisiting.PersonMet,
            DatabaseMapping.ClientVisiting.Subject,
            DatabaseMapping.ClientVisiting.Discussion,
            DatabaseMapping.ClientVisiting.Outcome,
            DatabaseMapping.ClientVisiting.NextAction,
            DatabaseMapping.ClientVisiting.FollowUpDate,
            DatabaseMapping.ClientVisiting.Remarks,
            DatabaseMapping.ClientVisiting.Status,
            "MeetingMode",
            "PreviousMeetingId"
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


    private static string BuildVisitType(
        IFormCollection form)
    {
        string raw =
            form[
                DatabaseMapping.ClientVisiting.VisitType]
                .ToString()
                .Trim();

        string value = raw;

        if (string.Equals(
                raw,
                "Other",
                StringComparison.OrdinalIgnoreCase))
        {
            string otherText =
                form["OtherVisitType"]
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
        DateTime visitDate,
        DateTime? followUpDate,
        int previousMeetingId,
        bool writePreviousMeetingId,
        int? userId)
    {
        if (columns.Contains(
                DatabaseMapping.ClientVisiting.PreviousMeetingId)
            && writePreviousMeetingId)
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@PreviousMeetingId",
                    System.Data.SqlDbType.Int)
                {
                    Value = previousMeetingId
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.VisitDate))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@VisitDate",
                    System.Data.SqlDbType.DateTime)
                {
                    Value = visitDate
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.ClientId))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@ClientId",
                    System.Data.SqlDbType.Int)
                {
                    Value = Convert.ToInt32(
                        form[
                            DatabaseMapping.ClientVisiting.ClientId]
                            .ToString())
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.UserId)
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
                DatabaseMapping.ClientVisiting.VisitType))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@VisitType",
                    System.Data.SqlDbType.NVarChar,
                    100)
                {
                    Value = BuildVisitType(form)
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.PersonMet))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@PersonMet",
                    System.Data.SqlDbType.NVarChar,
                    150)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.PersonMet]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Subject))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Subject",
                    System.Data.SqlDbType.NVarChar,
                    300)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.Subject]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Discussion))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Discussion",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.Discussion]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Outcome))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Outcome",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.Outcome]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.NextAction))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@NextAction",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.NextAction]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.FollowUpDate))
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
                DatabaseMapping.ClientVisiting.Remarks))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Remarks",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.Remarks]
                        .ToString().Trim()
                });
        }

        if (columns.Contains(
                DatabaseMapping.ClientVisiting.Status))
        {
            command.Parameters.Add(
                new SqlParameter(
                    "@Status",
                    System.Data.SqlDbType.NVarChar,
                    30)
                {
                    Value = form[
                        DatabaseMapping.ClientVisiting.Status]
                        .ToString().Trim()
                });
        }
    }
}