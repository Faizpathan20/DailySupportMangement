using System.Security.Claims;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

public class AccountController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly DynamicTableService _tableService;

    public AccountController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }


    // ============================================
    // LOGIN PAGE
    // ============================================

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(
                "Index",
                "Dashboard");
        }

        return View();
    }


    // ============================================
    // LOGIN
    // ============================================

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.UserName))
        {
            ModelState.AddModelError(
                "UserName",
                "Username is required.");

            return View(model);
        }


        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(
                "Password",
                "Password is required.");

            return View(model);
        }


        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ModelState.AddModelError(
                "",
                "Database connection string is missing.");

            return View(model);
        }


        const string LoginUsersTable = "LoginUsers";


        using SqlConnection connection =
            new SqlConnection(connectionString);


        // Open the connection BEFORE running any query.
        // GetSchemaAsync executes a query immediately, so
        // the connection must already be open, otherwise
        // SqlCommand throws "BeginExecuteReader requires
        // an open and available Connection".
        await connection.OpenAsync();


        DynamicTableService.SchemaColumns schema =
            await _tableService.GetSchemaAsync(
                connection,
                LoginUsersTable);


        string userNameColumn =
            schema.Column(
                    LoginUsersTable,
                    "UserName") ?? "";

        string passwordColumn =
            schema.Column(
                    LoginUsersTable,
                    "Password") ?? "";

        string idColumn =
            schema.Column(
                    LoginUsersTable,
                    "Id") ?? "";

        string? isActiveColumn =
            schema.Column(
                    LoginUsersTable,
                    "IsActive");


        // Login cannot work without a username and a
        // password column on the users table.
        if (userNameColumn.Length == 0
            || passwordColumn.Length == 0)
        {
            ModelState.AddModelError(
                "",
                "User table is missing the UserName or "
                    + "Password column.");

            return View(model);
        }


        string isActiveSelect =
            isActiveColumn is not null
                ? $", {isActiveColumn}"
                : "";


        string query = $@"
            SELECT
                {idColumn},
                {userNameColumn},
                {passwordColumn}
                {isActiveSelect}

            FROM
                {LoginUsersTable}

            WHERE
                {userNameColumn}
                = @UserName";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.Add(
            new SqlParameter(
                "@UserName",
                System.Data.SqlDbType.NVarChar,
                100)
            {
                Value = model.UserName.Trim()
            });


        using SqlDataReader reader =
            await command.ExecuteReaderAsync();


        if (!await reader.ReadAsync())
        {
            ModelState.AddModelError(
                "",
                "Invalid username or password.");

            return View(model);
        }


        if (isActiveColumn is not null)
        {
            bool isActive =
                Convert.ToBoolean(
                    reader[isActiveColumn]);

            if (!isActive)
            {
                ModelState.AddModelError(
                    "",
                    "This user is inactive.");

                return View(model);
            }
        }


        string databaseUserName =
            reader[userNameColumn]
            .ToString() ?? "";


        string databasePassword =
            reader[passwordColumn]
            .ToString() ?? "";


        // Simple password comparison
        if (databasePassword != model.Password)
        {
            ModelState.AddModelError(
                "",
                "Invalid username or password.");

            return View(model);
        }


        // ========================================
        // CREATE LOGIN COOKIE
        // ========================================

        var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.Name,
                databaseUserName),

            new Claim(
                ClaimTypes.NameIdentifier,
                reader[idColumn]
                .ToString() ?? "")
        };


        var identity =
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults
                    .AuthenticationScheme);


        var principal =
            new ClaimsPrincipal(identity);


        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            principal);


        return RedirectToAction(
            "Index",
            "Dashboard");
    }


    // ============================================
    // LOGOUT
    // ============================================

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme);


        return RedirectToAction(
            "Login",
            "Account");
    }
}