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

    public AccountController(IConfiguration configuration)
    {
        _configuration = configuration;
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


        using SqlConnection connection =
            new SqlConnection(connectionString);


        string query = $@"
            SELECT
                {DatabaseMapping.LoginUsers.Id},
                {DatabaseMapping.LoginUsers.UserName},
                {DatabaseMapping.LoginUsers.Password},
                {DatabaseMapping.LoginUsers.IsActive}

            FROM
                {DatabaseMapping.LoginUsers.Table}

            WHERE
                {DatabaseMapping.LoginUsers.UserName}
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


        await connection.OpenAsync();


        using SqlDataReader reader =
            await command.ExecuteReaderAsync();


        if (!await reader.ReadAsync())
        {
            ModelState.AddModelError(
                "",
                "Invalid username or password.");

            return View(model);
        }


        bool isActive =
            Convert.ToBoolean(
                reader[
                    DatabaseMapping
                        .LoginUsers
                        .IsActive]);


        if (!isActive)
        {
            ModelState.AddModelError(
                "",
                "This user is inactive.");

            return View(model);
        }


        string databaseUserName =
            reader[
                DatabaseMapping
                    .LoginUsers
                    .UserName]
            .ToString() ?? "";


        string databasePassword =
            reader[
                DatabaseMapping
                    .LoginUsers
                    .Password]
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
                reader[
                    DatabaseMapping
                        .LoginUsers
                        .Id]
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