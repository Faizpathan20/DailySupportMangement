using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Master.Models;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;

namespace Master.Configuration;

// ================================================
// DynamicTableService reads SQL Server metadata on
// demand and turns each table column into a
// MasterColumnViewModel that drives the grid, the
// create/edit form, jQuery-free JS, validation and
// the parameterized INSERT/UPDATE statements.
//
// Unlike the old FieldRegistry approach, columns do
// NOT need to be whitelisted first: ALTER TABLE ...
// ADD [GSTNumber] NVARCHAR(50) is picked up (within
// the cache TTL) and automatically rendered as a
// text input, inserted/updated etc.
//
// A few tiny <optional> conventions refine display
// names / behaviours for well-known system columns;
// they are NOT a column whitelist.
// ================================================
public sealed class DynamicTableService
{
    // Short-lived cache: a new column appears within
    // the TTL without an app restart.
    private const int CacheMinutes = 1;

    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled);

    private static readonly Regex PhoneRegex =
        new(@"^[0-9]{10,15}$",
            RegexOptions.Compiled);

    private static readonly Regex CamelSpaceRegex =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled);

    private static readonly HashSet<string> TextTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "nvarchar", "varchar", "nchar", "char",
            "text", "ntext"
        };

    private static readonly ConcurrentDictionary<string, CacheEntry>
        FieldCache =
            new(StringComparer.OrdinalIgnoreCase);

    // Optional human-friendly labels for known columns.
    // Any column NOT in this map is humanized from its
    // name automatically (GSTNumber -> "GST Number").
    private static readonly Dictionary<string, string> DisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = "ID",
            ["IsActive"] = "Status",
            ["EntryOn"] = "Entry On",
            ["UserId"] = "User Name",
            ["UserName"] = "User Name",
            ["StateName"] = "State Name",
            ["ClientName"] = "Client Name",
            ["ClientId"] = "Client",
            ["StateId"] = "State",
            ["MobileNo"] = "Mobile No",
            ["AlternateMobileNo"] = "Alt Mobile No",
            ["Email"] = "Email",
            ["Password"] = "Password",
            ["SupportType"] = "Support Type",
            ["VisitType"] = "Visit Type",
            ["PersonMet"] = "Person Met"
        };

    // Which column of a referenced table is shown when a
    // foreign key is rendered as a dropdown / grid cell.
    private static readonly Dictionary<string, string>
        LookupDisplayColumns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["States"] = "StateName",
                ["LoginUsers"] = "UserName",
                ["ClientMaster"] = "ClientName"
            };

    public bool TryGetLookupDisplayColumn(
        string table,
        out string displayColumn) =>
        LookupDisplayColumns.TryGetValue(
            table,
            out displayColumn!);

    public string GetLookupDisplayColumn(string table) =>
        LookupDisplayColumns.TryGetValue(
                table,
                out var display)
            ? display
            : table.TrimEnd('s') + "Name";


    // Static dropdown options for enum-like text columns that
    // happen to have a DB default. Columns NOT listed here fall
    // back to the generic metadata rules (a column with a DB
    // default is treated as server-generated and non-editable).
    // Key: "{Table}|{Column}" -> allowed values.
    private static readonly Dictionary<string, string[]>
        StaticDropdownOptions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["DailySupport|Status"] =
                    new[]
                    {
                        "Open",
                        "In Progress",
                        "Pending",
                        "Completed",
                        "Cancelled"
                    },
                ["DailySupport|Priority"] =
                    new[]
                    {
                        "Low",
                        "Medium",
                        "High",
                        "Urgent"
                    },
                ["DailySupport|SupportType"] =
                    new[]
                    {
                        "Software Support",
                        "Installation",
                        "Configuration",
                        "Client Query",
                        "Training",
                        "Data/Report",
                        "Technical Issue",
                        "Other"
                    },
                ["ClientVisiting|Status"] =
                    new[]
                    {
                        "Planned",
                        "Completed",
                        "Cancelled"
                    },
                ["ClientVisiting|VisitType"] =
                    new[]
                    {
                        "New Client",
                        "Follow Up",
                        "Support Visit",
                        "Sales Visit",
                        "Service Visit",
                        "Collection Visit",
                        "Other"
                    }
            };

    // Long text columns are not shown in the grid by default
    // (they clutter it); this map re-enables specific ones.
    private static readonly HashSet<string> ShowInTableOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "DailySupport|Remarks"
        };

    public bool TryGetStaticOptions(
        string table,
        string fieldName,
        out string[]? options) =>
        StaticDropdownOptions.TryGetValue(
            $"{table}|{fieldName}",
            out options);

    public static string[] GetStaticOptions(
        string table,
        string fieldName) =>
        StaticDropdownOptions.TryGetValue(
                $"{table}|{fieldName}",
                out var options)
            ? options
            : Array.Empty<string>();


    // ============================================
    // METADATA
    // ============================================

    public async Task<List<MasterColumnViewModel>> GetTableFieldsAsync(
        SqlConnection connection,
        string table)
    {
        if (FieldCache.TryGetValue(
                table,
                out var cached)
            && !cached.IsExpired)
        {
            return cached.Fields;
        }

        var fields =
            await ReadTableFieldsAsync(
                connection,
                table);

        FieldCache[table] =
            new CacheEntry(fields);

        return fields;
    }


    public async Task<HashSet<string>> GetTableColumnsAsync(
        SqlConnection connection,
        string table)
    {
        var fields =
            await GetTableFieldsAsync(
                connection,
                table);

        return new HashSet<string>(
            fields.Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);
    }


    // A runtime view of which columns actually exist on
    // a set of tables. Queries built against this can
    // conditionally include a column/join/filter, or
    // skip a whole component, when a column is missing.
    public sealed class SchemaColumns
    {
        private readonly IReadOnlyDictionary<
            string,
            HashSet<string>> _tables;

        internal SchemaColumns(
            IReadOnlyDictionary<
                string,
                HashSet<string>> tables)
        {
            _tables = tables;
        }

        public bool HasTable(string table) =>
            _tables.ContainsKey(table);

        public bool Has(string table, string column) =>
            _tables.TryGetValue(
                    table,
                    out var columns)
                && columns.Contains(column);

        public string? Column(string table, string column) =>
            Has(table, column) ? column : null;
    }


    public async Task<SchemaColumns> GetSchemaAsync(
        SqlConnection connection,
        params string[] tables)
    {
        var map =
            new Dictionary<
                string,
                HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            map[table] =
                await GetTableColumnsAsync(
                    connection,
                    table);
        }

        return new SchemaColumns(map);
    }


    // Resolves the primary-key column name from metadata
    // instead of hard-coding "Id" everywhere.
    public async Task<string> GetPrimaryKeyColumnAsync(
        SqlConnection connection,
        string table)
    {
        var fields =
            await GetTableFieldsAsync(
                connection,
                table);

        return fields
            .FirstOrDefault(f => f.IsPrimaryKey)
            ?.Name ?? "Id";
    }


    // Loads FK dropdowns (lookup tables) and static option
    // arrays for the controls used by the create/edit forms.
    public async Task<(
        Dictionary<string, List<LookupOptionViewModel>> Dropdowns,
        Dictionary<string, string[]> StaticOptions)>
        LoadFormOptionsAsync(
            SqlConnection connection,
            string table,
            List<MasterColumnViewModel> formFields)
    {
        var dropdowns =
            new Dictionary<string, List<LookupOptionViewModel>>(
                StringComparer.OrdinalIgnoreCase);

        var staticOptions =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var field in formFields.Where(
                     f => f.Control == "select"
                          && f.LookupKey != null))
        {
            if (!TryGetLookupDisplayColumn(
                    field.LookupKey!,
                    out string displayColumn))
            {
                continue;
            }

            string refColumn =
                field.LookupRefColumn ?? "Id";

            string where = "";

            if (string.Equals(
                    field.LookupKey,
                    "ClientMaster",
                    StringComparison.OrdinalIgnoreCase))
            {
                HashSet<string> columns =
                    await GetTableColumnsAsync(
                        connection,
                        "ClientMaster");

                if (columns.Contains("IsActive"))
                {
                    where = " WHERE [IsActive] = 1";
                }
            }

            string lookupQuery = $@"
                SELECT
                    [{refColumn}],
                    [{displayColumn}]

                FROM
                    [{field.LookupKey}]
                {where}

                ORDER BY
                    [{displayColumn}] ASC";

            using SqlCommand lookupCommand =
                new SqlCommand(lookupQuery, connection);

            lookupCommand.CommandTimeout = 0;

            var options = new List<LookupOptionViewModel>();

            using SqlDataReader lookupReader =
                await lookupCommand.ExecuteReaderAsync();

            while (await lookupReader.ReadAsync())
            {
                options.Add(
                    new LookupOptionViewModel
                    {
                        Id =
                            Convert.ToInt32(
                                lookupReader[refColumn]),
                        Name =
                            lookupReader[displayColumn]
                                .ToString() ?? ""
                    });
            }

            dropdowns[field.LookupKey!] = options;
        }

        foreach (var field in formFields.Where(
                     f => f.Type == "options"))
        {
            if (TryGetStaticOptions(
                    table,
                    field.Name,
                    out var options))
            {
                staticOptions[field.Name] = options!;
            }
        }

        return (dropdowns, staticOptions);
    }

    public static bool IsText(string? sqlType) =>
        sqlType != null
        && TextTypes.Contains(sqlType);


    private async Task<List<MasterColumnViewModel>> ReadTableFieldsAsync(
        SqlConnection connection,
        string table)
    {
        const string query = @"
SELECT
    c.ORDINAL_POSITION,
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.IS_NULLABLE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.COLUMN_DEFAULT,
    CASE WHEN idc.object_id IS NOT NULL THEN 1 ELSE 0 END AS IsIdentity,
    CASE WHEN EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
            ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
           AND tc.TABLE_NAME = kcu.TABLE_NAME
        WHERE tc.TABLE_NAME = @Table
          AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
          AND kcu.COLUMN_NAME = c.COLUMN_NAME
    ) THEN 1 ELSE 0 END AS IsPrimaryKey,
    fk.REF_TABLE AS ReferencedTable,
    fk.REF_COLUMN AS ReferencedColumn
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.identity_columns idc
    ON idc.object_id = OBJECT_ID(@Table)
   AND idc.name = c.COLUMN_NAME
OUTER APPLY (
    SELECT TOP 1
        kcu2.TABLE_NAME AS REF_TABLE,
        kcu2.COLUMN_NAME AS REF_COLUMN
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
        ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
       AND kcu.TABLE_NAME = @Table
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu2
        ON rc.UNIQUE_CONSTRAINT_NAME = kcu2.CONSTRAINT_NAME
    WHERE kcu.COLUMN_NAME = c.COLUMN_NAME
) fk
WHERE c.TABLE_NAME = @Table
ORDER BY c.ORDINAL_POSITION";

        using SqlCommand command =
            new SqlCommand(query, connection);
        command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter(
                "@Table",
                SqlDbType.NVarChar,
                128)
            {
                Value = table
            });

        var fields = new List<MasterColumnViewModel>();

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string name = reader.GetString(1);
            string dataType =
                reader.GetString(2)
                    .ToLowerInvariant();

            bool nullable =
                reader.GetString(3) == "YES";

            int? maxLength =
                reader.IsDBNull(4)
                    ? (int?)null
                    : reader.GetInt32(4);

            bool hasDefault =
                !reader.IsDBNull(5);

            bool isIdentity =
                reader.GetInt32(6) == 1;

            bool isPrimaryKey =
                reader.GetInt32(7) == 1;

            string? referencedTable =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetString(8);

            string? referencedColumn =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9);

            fields.Add(
                BuildField(
                    table,
                    name,
                    dataType,
                    nullable,
                    maxLength,
                    hasDefault,
                    isIdentity,
                    isPrimaryKey,
                    referencedTable,
                    referencedColumn));
        }

        return fields;
    }


    // ============================================
    // FIELD GENERATION (metadata -> view model)
    // ============================================

    private static MasterColumnViewModel BuildField(
        string table,
        string name,
        string sqlType,
        bool nullable,
        int? maxLength,
        bool hasDefault,
        bool isIdentity,
        bool isPrimaryKey,
        string? referencedTable,
        string? referencedColumn)
    {
        var field = new MasterColumnViewModel
        {
            Name = name,
            Display = Humanize(name),
            SqlType = sqlType,
            MaxLength = maxLength,
            IsPrimaryKey = isPrimaryKey,
            IsIdentity = isIdentity,
            HasDefault = hasDefault,
            LookupKey = referencedTable,
            LookupRefColumn = referencedColumn,
            Width = 10
        };

        // --- well-known system columns (conventions) ---
        if (name.EndsWith(
                "UserId",
                StringComparison.OrdinalIgnoreCase))
        {
            field.Editable = false;
            field.AutoWrite = "auth-user";
            field.Type = "text";
            field.SortType = "text";
            field.Control = "input";
            field.InputType = "text";
            return field;
        }

        if (name.Equals(
                "Password",
                StringComparison.OrdinalIgnoreCase))
        {
            field.Editable = true;
            field.Required = !nullable;
            field.CreateOnly = true;
            field.ShowInTable = false;
            field.Type = "text";
            field.SortType = "text";
            field.Control = "input";
            field.InputType = "password";
            field.Width = 25;
            return field;
        }

        if (name.Equals(
                "IsActive",
                StringComparison.OrdinalIgnoreCase))
        {
            field.Editable = false;
            field.AutoWrite = "true";
            field.Type = "status";
            field.SortType = "status";
            field.Control = "input";
            field.InputType = "checkbox";
            field.Required = !nullable;
            field.Width = 7;
            return field;
        }

        // --- enum-like text columns with static dropdowns ---
        if (StaticDropdownOptions.TryGetValue(
                $"{table}|{name}",
                out _))
        {
            field.Editable = true;
            field.Required = !nullable;
            field.Type = "options";
            field.SortType =
                name.Contains(
                    "Status",
                    StringComparison.OrdinalIgnoreCase)
                    ? "status"
                    : name.Contains(
                        "Priority",
                        StringComparison.OrdinalIgnoreCase)
                        ? "priority"
                        : "text";
            field.Control = "select";
            field.InputType = "select";
            field.Width = 15;
            return field;
        }

        // --- server-generated columns are never editable ---
        if (isIdentity || isPrimaryKey || hasDefault)
        {
            field.Editable = false;
            field.ShowInTable = true;
            ApplyDisplayType(field, sqlType);
            return field;
        }

        // --- editable columns, mapped purely from SQL type ---
        field.Editable = true;
        field.Required = !nullable;

        switch (sqlType)
        {
            case "bit":
                field.Type = "status";
                field.SortType = "status";
                field.Control = "checkbox";
                field.InputType = "checkbox";
                break;

            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
                if (referencedTable != null)
                {
                    field.Type = "dropdown";
                    field.SortType = "text";
                    field.Control = "select";
                    field.InputType = "text";
                }
                else
                {
                    field.Type = "number";
                    field.SortType = "num";
                    field.Control = "input";
                    field.InputType = "number";
                }
                break;

            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
                field.Type = "number";
                field.SortType = "num";
                field.Control = "input";
                field.InputType = "number";
                break;

            case "date":
                field.Type = "date";
                field.SortType = "date";
                field.Control = "input";
                field.InputType = "date";
                break;

            case "datetime":
            case "datetime2":
            case "smalldatetime":
                field.Type = "date";
                field.SortType = "date";
                field.Control = "input";

                // name-suffix conventions preserve how the
                // existing DailySupport / ClientVisiting forms
                // render these columns (date-only input for
                // "*Date", time-only input for "*Time").
                field.InputType =
                    name.EndsWith(
                            "Date",
                            StringComparison.OrdinalIgnoreCase)
                        ? "date"
                        : name.EndsWith(
                                "Time",
                                StringComparison.OrdinalIgnoreCase)
                            ? "time"
                            : "datetime-local";
                break;

            case "time":
                field.Type = "text";
                field.SortType = "text";
                field.Control = "input";
                field.InputType = "time";
                break;

            default:
                field.Type = "text";
                field.SortType = "text";
                field.Control =
                    maxLength is null or -1 or > 300
                        ? "textarea"
                        : "input";
                field.InputType = "text";

                // Long text columns are not shown in the grid
                // by default (they clutter it); the override map
                // re-enables specific ones (e.g. Remarks).
                field.ShowInTable =
                    field.Control != "textarea"
                    || ShowInTableOverrides.Contains(
                        $"{table}|{name}");
                break;
        }

        return field;
    }


    private static void ApplyDisplayType(
        MasterColumnViewModel field,
        string sqlType)
    {
        switch (sqlType)
        {
            case "bit":
                field.Type = "status";
                field.SortType = "status";
                field.Control = "input";
                field.InputType = "checkbox";
                break;

            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
                field.Type = "number";
                field.SortType = "num";
                field.Control = "input";
                field.InputType = "number";
                break;

            case "date":
            case "datetime":
            case "datetime2":
            case "smalldatetime":
                field.Type = "date";
                field.SortType = "date";
                field.Control = "input";
                field.InputType = "date";
                break;


            default:
                field.Type = "text";
                field.SortType = "text";
                field.Control = "input";
                field.InputType = "text";
                break;
        }
    }


    private static string Humanize(string name)
    {
        if (DisplayNames.TryGetValue(
                name,
                out var label))
        {
            return label;
        }

        return CamelSpaceRegex
            .Replace(name, " ");
    }


    // ============================================
    // CELL FORMATTING (server side, grid rows)
    // ============================================

    public static string FormatCellValue(
        MasterColumnViewModel field,
        object value)
    {
        if (value == DBNull.Value)
        {
            return "";
        }

        switch (field.Type)
        {
            case "date":
                var date = Convert.ToDateTime(value);

                if (field.Editable)
                {
                    return field.InputType switch
                    {
                        "time" =>
                            date.ToString("hh:mm tt"),
                        "datetime-local" =>
                            date.ToString(
                                "dd/MM/yyyy hh:mm tt"),
                        _ =>
                            date.ToString("dd/MM/yyyy")
                    };
                }

                return date.ToString(
                    "dd/MM/yyyy hh:mm tt");

            case "status":
                return Convert.ToBoolean(value)
                    ? "Active"
                    : "Non Active";

            default:
                return value.ToString() ?? "";
        }
    }


    // ============================================
    // VALIDATION
    // ============================================

    public string? ValidateRequiredFields(
        IEnumerable<MasterColumnViewModel> fields,
        IFormCollection form)
    {
        foreach (var field in fields)
        {
            string raw = form[field.Name].ToString();

            // checkboxes always have a value ("on"/empty)
            if (field.Control == "checkbox"
                || string.Equals(
                    field.SqlType,
                    "bit",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (field.Type == "dropdown")
            {
                if (field.Required
                    && (!int.TryParse(
                            raw,
                            out int value)
                        || value <= 0))
                {
                    return $"Please select a valid {field.Display}.";
                }

                continue;
            }

            if (field.Type == "options")
            {
                if (field.Required
                    && string.IsNullOrWhiteSpace(raw))
                {
                    return $"Please select a valid {field.Display}.";
                }

                continue;
            }

            if (field.Required
                && string.IsNullOrWhiteSpace(raw))
            {
                return $"{field.Display} is required.";
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmed = raw.Trim();

            if (field.InputType == "number")
            {
                if (!decimal.TryParse(
                        trimmed,
                        NumberStyles.Any,
                        CultureInfo.CurrentCulture,
                        out _))
                {
                    return $"Please enter a valid {field.Display}.";
                }

                continue;
            }

            if (field.InputType is "date"
                or "datetime-local"
                or "time")
            {
                if (!DateTime.TryParse(
                        trimmed,
                        out _))
                {
                    return $"Please enter a valid {field.Display}.";
                }

                continue;
            }

            if (field.InputType == "email"
                && !EmailRegex.IsMatch(trimmed))
            {
                return $"Please enter a valid {field.Display}.";
            }

            if (field.InputType == "tel"
                && !PhoneRegex.IsMatch(trimmed))
            {
                return $"Please enter a valid {field.Display}.";
            }

            if (field.MaxLength is > 0
                && trimmed.Length > field.MaxLength.Value)
            {
                return
                    $"{field.Display} cannot exceed "
                    + $"{field.MaxLength.Value} characters.";
            }
        }

        return null;
    }


    // ============================================
    // PARAMETER BUILDING (parameterized SQL only)
    // ============================================

    public void AddParameter(
        SqlCommand command,
        MasterColumnViewModel field,
        string rawValue)
    {
        string type =
            field.SqlType?.ToLowerInvariant() ?? "";

        if (field.Type == "dropdown")
        {
            command.Parameters.Add(
                new SqlParameter(
                    $"@{field.Name}",
                    SqlDbType.Int)
                {
                    Value =
                        int.TryParse(
                            rawValue,
                            out int lookupId)
                            ? lookupId
                            : 0
                });
            return;
        }

        if (type == "bit")
        {
            bool bitValue =
                rawValue == "on"
                || rawValue == "true"
                || rawValue == "1";

            command.Parameters.Add(
                new SqlParameter(
                    $"@{field.Name}",
                    SqlDbType.Bit)
                {
                    Value = bitValue
                });
            return;
        }

        if (type is "int" or "bigint")
        {
            int? intValue =
                int.TryParse(rawValue, out int parsed)
                    ? parsed
                    : (int?)null;

            if (intValue is null && !field.Required)
            {
                intValue = null;
            }

            command.Parameters.Add(
                new SqlParameter(
                    $"@{field.Name}",
                    type == "bigint"
                        ? SqlDbType.BigInt
                        : SqlDbType.Int)
                {
                    Value =
                        (object?)intValue ?? DBNull.Value
                });
            return;
        }

        switch (type)
        {
            case "smallint":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.SmallInt)
                    {
                        Value = ToDecimalOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "tinyint":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.TinyInt)
                    {
                        Value = ToDecimalOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.Decimal)
                    {
                        Value = ToDecimalOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "date":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.Date)
                    {
                        Value = ToDateTimeOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "datetime":
            case "smalldatetime":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.DateTime)
                    {
                        Value = ToDateTimeOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "datetime2":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.DateTime2)
                    {
                        Value = ToDateTimeOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            case "time":
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.Time)
                    {
                        Value = ToDateTimeOrNull(
                            rawValue,
                            field.Required)
                    });
                break;

            default:
                command.Parameters.Add(
                    new SqlParameter(
                        $"@{field.Name}",
                        SqlDbType.NVarChar,
                        field.MaxLength is > 0
                            ? field.MaxLength.Value
                            : -1)
                    {
                        Value =
                            string.IsNullOrWhiteSpace(rawValue)
                                ? ""
                                : rawValue.Trim()
                    });
                break;
        }
    }


    private static object ToDecimalOrNull(
        string rawValue,
        bool required) =>
        decimal.TryParse(
                rawValue,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out decimal parsed)
            ? parsed
            : (object)(required ? 0 : DBNull.Value);


    private static object ToDateTimeOrNull(
        string rawValue,
        bool required) =>
        DateTime.TryParse(
                rawValue,
                out DateTime parsed)
            ? parsed
            : (object)(required
                ? DateTime.MinValue
                : DBNull.Value);


    private sealed record CacheEntry(
        List<MasterColumnViewModel> Fields)
    {
        public DateTime Expires { get; } =
            DateTime.UtcNow.AddMinutes(CacheMinutes);

        public bool IsExpired =>
            DateTime.UtcNow >= Expires;
    }


    // ============================================
    // HTTP / CONTEXT HELPERS
    // ============================================

    public static string? GetLoggedInUserId(
        ClaimsPrincipal user) =>
        user.FindFirst(
            ClaimTypes.NameIdentifier)
        ?.Value;

    public static bool IsAjaxRequest(
        HttpRequest request) =>
        string.Equals(
            request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
}