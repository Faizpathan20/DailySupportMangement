using System.Security.Claims;
using System.Text.RegularExpressions;
using Master.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace Master.Configuration;

public static class DynamicTableHelper
{
    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled);

    private static readonly Regex PhoneRegex =
        new(@"^[0-9]{10,15}$",
            RegexOptions.Compiled);

    // The active table schema is effectively static in
    // production, so the INFORMATION_SCHEMA round-trip
    // on every page load / CRUD call is wasteful. Cache
    // the column sets (keyed by table name) in memory.
    private static readonly
        System.Collections.Concurrent
            .ConcurrentDictionary<string, HashSet<string>>
        ColumnCache =
            new System.Collections.Concurrent
                .ConcurrentDictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase);

    public static async Task<HashSet<string>> GetTableColumnsAsync(
        SqlConnection connection,
        string table)
    {
        if (ColumnCache.TryGetValue(table, out var cached))
        {
            return cached;
        }

        var columns =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);


        string query = @"
            SELECT
                COLUMN_NAME

            FROM
                INFORMATION_SCHEMA.COLUMNS

            WHERE
                TABLE_NAME = @Table";


        using SqlCommand command =
            new SqlCommand(query, connection);


        command.Parameters.Add(
            new SqlParameter(
                "@Table",
                System.Data.SqlDbType.NVarChar,
                128)
            {
                Value = table
            });


        using SqlDataReader reader =
            await command.ExecuteReaderAsync();


        while (await reader.ReadAsync())
        {
            columns.Add(
                reader.GetString(0));
        }


        return ColumnCache.GetOrAdd(
            table,
            columns);
    }


    public static List<MasterColumnViewModel> BuildActiveFields(
        IEnumerable<MasterColumnViewModel> registry,
        HashSet<string> columns)
    {
        var fields =
            new List<MasterColumnViewModel>();


        foreach (var candidate in registry)
        {
            if (columns.Contains(
                    candidate.Name))
            {
                fields.Add(candidate);
            }
        }


        return fields;
    }


    public static string? ValidateRequiredFields(
        IEnumerable<MasterColumnViewModel> fields,
        IFormCollection form)
    {
        foreach (var field in fields)
        {
            string raw = form[field.Name].ToString();

            if (field.Type == "dropdown")
            {
                if (field.Required
                    && (!int.TryParse(raw, out int value)
                        || value <= 0))
                {
                    return
                        $"Please select a valid {field.Display}.";
                }

                continue;
            }

            if (field.Required
                && string.IsNullOrWhiteSpace(raw))
            {
                return
                    $"{field.Display} is required.";
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmedValue = raw.Trim();

            if (field.InputType == "email"
                && !EmailRegex.IsMatch(trimmedValue))
            {
                return
                    $"Please enter a valid {field.Display}.";
            }

            if (field.InputType == "tel"
                && !PhoneRegex.IsMatch(trimmedValue))
            {
                return
                    $"Please enter a valid {field.Display}.";
            }
        }


        return null;
    }


    public static void AddEditableParameter(
        SqlCommand command,
        MasterColumnViewModel field,
        string rawValue)
    {
        if (field.Type == "dropdown")
        {
            command.Parameters.Add(
                new SqlParameter(
                    $"@{field.Name}",
                    System.Data.SqlDbType.Int)
                {
                    Value =
                        int.Parse(rawValue)
                });
        }
        else
        {
            command.Parameters.Add(
                new SqlParameter(
                    $"@{field.Name}",
                    System.Data.SqlDbType.NVarChar,
                    -1)
                {
                    Value =
                        string.IsNullOrWhiteSpace(rawValue)
                            ? ""
                            : rawValue.Trim()
                });
        }
    }


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