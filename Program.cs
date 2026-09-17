
using Master.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);


// ============================================
// MVC
// ============================================

builder.Services.AddControllersWithViews();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();


// ============================================
// COOKIE AUTHENTICATION
// ============================================

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)

    .AddCookie(options =>
    {
        options.Cookie.Name = "MasterAuth";

        options.LoginPath = "/Account/Login";

        options.AccessDeniedPath = "/Account/Login";

        options.ExpireTimeSpan =
            TimeSpan.FromHours(8);

        options.SlidingExpiration = true;
    });


builder.Services.AddAuthorization();


var app = builder.Build();


// ============================================
// MIDDLEWARE
// ============================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}


app.UseHttpsRedirection();

app.UseStaticFiles();


// ============================================
// DB WARM-UP + INDEXES
// The hosted SQL instance often sleeps while
// idle; the first request then stalls. Pinging
// it once on startup keeps the first login fast.
// We also ensure the filter/join columns used by
// the app's COUNT/SUM/JOIN/WHERE queries have
// indexes, otherwise every page scans the full
// table and loads feel slow on the shared host.
// Both are best-effort and idempotent.
// ============================================

var warmConnection =
    app.Configuration.GetConnectionString(
        "DefaultConnection");

if (!string.IsNullOrWhiteSpace(warmConnection))
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var warm = new Microsoft.Data.SqlClient.SqlConnection(
                warmConnection);

            await warm.OpenAsync();

            await using var warmCmd =
                warm.CreateCommand();

            warmCmd.CommandText = "SELECT 1";
            warmCmd.CommandTimeout = 0;

            await warmCmd.ExecuteScalarAsync();

            string[] indexes =
            {
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_ClientId') CREATE INDEX IX_DailySupport_ClientId ON DailySupport(ClientId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_UserId') CREATE INDEX IX_DailySupport_UserId ON DailySupport(UserId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_Status') CREATE INDEX IX_DailySupport_Status ON DailySupport(Status)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_Priority') CREATE INDEX IX_DailySupport_Priority ON DailySupport(Priority)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_EntryOn') CREATE INDEX IX_DailySupport_EntryOn ON DailySupport(EntryOn)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_SupportDate') CREATE INDEX IX_DailySupport_SupportDate ON DailySupport(SupportDate)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailySupport_FollowUpDate') CREATE INDEX IX_DailySupport_FollowUpDate ON DailySupport(FollowUpDate)",

                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientVisiting_ClientId') CREATE INDEX IX_ClientVisiting_ClientId ON ClientVisiting(ClientId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientVisiting_UserId') CREATE INDEX IX_ClientVisiting_UserId ON ClientVisiting(UserId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientVisiting_VisitDate') CREATE INDEX IX_ClientVisiting_VisitDate ON ClientVisiting(VisitDate)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientVisiting_Status') CREATE INDEX IX_ClientVisiting_Status ON ClientVisiting(Status)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientVisiting_FollowUpDate') CREATE INDEX IX_ClientVisiting_FollowUpDate ON ClientVisiting(FollowUpDate)",

                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMaster_StateId') CREATE INDEX IX_ClientMaster_StateId ON ClientMaster(StateId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMaster_UserId') CREATE INDEX IX_ClientMaster_UserId ON ClientMaster(UserId)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMaster_IsActive') CREATE INDEX IX_ClientMaster_IsActive ON ClientMaster(IsActive)",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMaster_EntryOn') CREATE INDEX IX_ClientMaster_EntryOn ON ClientMaster(EntryOn)"
            };

            foreach (var indexSql in indexes)
            {
                await using var indexCmd =
                    warm.CreateCommand();

                indexCmd.CommandText = indexSql;
                indexCmd.CommandTimeout = 0;

                await indexCmd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            // Warm-up/indexing is best-effort; never
            // crash startup if the DB is unreachable
            // or the account cannot create indexes.
        }
    });
}

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();


// ============================================
// DEFAULT ROUTE
// ============================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");


app.Run();

