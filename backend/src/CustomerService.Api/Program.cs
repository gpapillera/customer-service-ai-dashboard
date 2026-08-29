using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain.Interfaces;
using CustomerService.Infrastructure.Data;
using CustomerService.Infrastructure.Repositories;
using CustomerService.ML;
using CustomerService.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Api;

/// <summary>
/// Application entry point and composition root for the Customer Service AI
/// Dashboard Web API.
/// See docs/DIY.md §1 (layering), §2 (SQLite fallback + seed), §3 (string enums).
/// </summary>
public class Program
{
    /// <summary>Builds the host, configures services, and starts the API.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var app = CreateHostBuilder(args).Build();
        ConfigurePipeline(app);
        SeedDatabase(app);
        app.Run();
    }

    /// <summary>Configures the HTTP request pipeline (middleware + endpoints).</summary>
    /// <param name="app">The built application.</param>
    private static void ConfigurePipeline(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            // Redirect plaintext HTTP -> HTTPS and send HSTS in production.
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        app.UseCors("AllowAngularDev");
        app.UseMiddleware<CustomerService.Api.Middleware.SecurityHeadersMiddleware>();
        app.UseMiddleware<CustomerService.Api.Middleware.ApiExceptionMiddleware>();
        // Resolve the endpoint early so rate limiting + authorization see it.
        app.UseRouting();
        // Throttle anonymous auth endpoints before authentication runs.
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
    }

    /// <summary>Configures the web application builder and service container.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A configured <see cref="WebApplication"/> builder.</returns>
    public static WebApplicationBuilder CreateHostBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

        // SECURITY: never run with a missing or default JWT signing key. A known
        // key lets anyone forge an Admin token. Fail fast (closed), not open.
        var jwtKey = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == "dev-insecure-key-change-me-1234567890")
        {
            throw new InvalidOperationException(
                "Jwt:Key is missing or still set to the insecure development default. " +
                "Set it via 'dotnet user-secrets set \"Jwt:Key\" \"<64+ char random>\"' " +
                "or the Jwt__Key environment variable before running in any non-local environment.");
        }

        var provider = config["Database:Provider"] ?? "SqlServer";
        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            // Soft-delete (IsDeleted) global filters on Case/Customer are
            // intentional; child rows (CallLog/CaseComment/CustomerActivity/
            // CustomerAccount) deliberately survive a parent soft-delete as
            // history. EF's RequiredNavigationWithGlobalQueryFilter warning
            // (10622) is noise here: no read path queries a child as the root
            // and includes the filtered parent, so no silent row-drop occurs
            // in practice. See AppDbContext.cs + the recycle-bin paths in
            // CaseService/CustomerService, which use IgnoreQueryFilters() to
            // reach binned rows deliberately.
            options.ConfigureWarnings(w =>
                w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(config.GetConnectionString("Sqlite")!);
            }
            else
            {
                options.UseSqlServer(config.GetConnectionString("SqlServer"));
            }
        });

        builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();

        builder.Services.AddScoped<ICustomerService, Application.Services.CustomerService>();
        builder.Services.AddScoped<ICaseService, CaseService>();
        builder.Services.AddScoped<ICaseCommentService, CaseCommentService>();
        builder.Services.AddScoped<IViewEventService, Application.Services.ViewEventService>();
        builder.Services.AddScoped<ICallLogService, CallLogService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<ICustomerAuthService, CustomerAuthService>();
        builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        builder.Services.AddScoped<ITokenCookieService, TokenCookieService>();
        builder.Services.AddScoped<IDashboardService, DashboardService>();
        // Monotonic sequence generator for customer display IDs (C-NNNNN). Singleton
        // so the counter is shared/consistent across the process; seeded from the
        // existing rows in SeedDatabase() before any request can call Next().
        builder.Services.AddSingleton<ICustomerDisplayIdGenerator, CustomerDisplayIdGenerator>();

        // In-process SSE hub for instant case-assignment push (Phase 54). Singleton
        // so every SSE subscriber reads the same channel the service writes to.
        builder.Services.AddSingleton<ILiveUpdateHub, LiveUpdateHub>();

        builder.Services.AddScoped<InAppNotificationSender>();
        builder.Services.AddScoped<EmailNotificationSender>();
        builder.Services.AddScoped<INotificationSender>(sp => sp.GetRequiredService<CompositeNotificationSender>());
        // CompositeNotificationSender routes each notification to the sender
        // that handles its channel; the app consumes only this single
        // INotificationSender. See docs/DIY.md §7.
        builder.Services.AddScoped<CompositeNotificationSender>();
        builder.Services.Configure<CustomerService.Application.Options.NotificationOptions>(
            builder.Configuration.GetSection("Notifications"));
        // Register the resolved options as a concrete service so the Email
        // sender can take NotificationOptions directly (not just IOptions<>).
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerService.Application.Options.NotificationOptions>>().Value);
        builder.Services.Configure<CustomerService.Application.Options.EmailOptions>(
            builder.Configuration.GetSection("Email"));
        // Register the resolved EmailOptions as a concrete service so the email
        // sender can take it directly (not just IOptions<>).
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerService.Application.Options.EmailOptions>>().Value);
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<IEmailConfigService, EmailConfigService>();
        builder.Services.AddScoped<IEmailTestService, EmailTestService>();
        // Background worker: periodically scans for overdue cases and triggers the
        // agent-facing overdue email. Interval is configurable (Notifications:OverdueCheckIntervalMinutes).
        builder.Services.AddHostedService<OverdueEmailHostedService>();

        builder.Services.AddSingleton<IPriorityPredictor>(serviceProvider =>
        {
            var configuredPath = config["ML:ModelPath"];
            var logger = serviceProvider.GetRequiredService<ILogger<OnnxPriorityPredictor>>();
            // The configured path may be relative to the current working directory
            // (which varies by how the app is launched). Resolve it against the
            // content root, and also try the repo/solution root (the model lives at
            // <repo>/ml/models/priority_model.onnx) so the model is found regardless
            // of where the API process is started from.
            var resolvedPath = ResolveModelPath(configuredPath, builder.Environment.ContentRootPath);
            var predictor = new OnnxPriorityPredictor(resolvedPath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                logger.LogWarning(
                    "Priority model not found (looked for '{ConfiguredPath}', resolved to '{ResolvedPath}'). " +
                    "The API will use the deterministic rule-based fallback for priority suggestions. " +
                    "To enable the ML model, run the Python training pipeline (ml/train_model.py) which " +
                    "exports ml/models/priority_model.onnx.",
                    configuredPath ?? "(unset)", resolvedPath ?? "(unset)");
            }
            else
            {
                logger.LogInformation("Priority model loaded from '{ModelPath}'. ML-based priority suggestions enabled.", resolvedPath);
            }
            return predictor;
        });

        // jwtKey is validated for presence + non-default at startup (fail-fast guard above).
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                };
                // Dual-source token: prefer the HttpOnly access_token cookie set on
                // login/refresh, but fall back to the Authorization header so the
                // legacy header flow (SSE fetch, older clients) still works.
                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var cookie = context.Request.Cookies["access_token"];
                        if (!string.IsNullOrEmpty(cookie))
                        {
                            context.Token = cookie;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
                // Serialize DateTime as UTC (with "Z"). EF Core returns
                // Kind=Unspecified after a DB round-trip, which would otherwise
                // drop the "Z" and make the frontend parse timestamps as local
                // time (breaking date-filter boundaries). See UtcDateTimeJsonConverter.
                options.JsonSerializerOptions.Converters.Add(
                    new CustomerService.Api.Json.UtcDateTimeJsonConverter());
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "Customer Service AI Dashboard API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new()
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "Enter 'Bearer {token}'",
            });
            c.AddSecurityRequirement(new()
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" },
                    },
                    Array.Empty<string>()
                },
            });
        });

        builder.Services.AddCors(options =>
        {
            var corsOrigins = config["Cors:AllowedOrigins"]
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? new[] { "http://localhost:4200" };
            options.AddPolicy("AllowAngularDev", policy =>
                policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
            // AllowCredentials() is REQUIRED now that auth uses cookies. Origins are
            // explicit (never "*"), which is what makes this combination valid + safe.
        });

        // Rate limiting on anonymous auth endpoints (brute-force protection).
        // Built-in .NET 8 limiter - no extra dependency. Keyed by client IP.
        // DEV GOTCHA (this was a real bug): in Development every browser
        // request reaches the API from 127.0.0.1 (the Angular dev proxy), so
        // ALL clients shared ONE fixed-window bucket. A tight limit blocked
        // legitimate logins after a few attempts - correct credentials came
        // back 429. Worse, IPv4 (127.0.0.1) and IPv6 ([::1]) localhost are
        // DIFFERENT buckets, so probing one path silently saturated the other.
        // Fix: loopback clients are NOT rate-limited in Development (brute-force
        // protection is meaningless for localhost). Real (non-loopback) clients
        // always get the real-IP-keyed limiter - so the protection still exists
        // for genuine traffic.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Real client IP: prefer X-Forwarded-For (first hop) behind a
            // reverse proxy, else the direct connection address.
            static string ClientIp(HttpContext context)
            {
                var xff = context.Request.Headers["X-Forwarded-For"].ToString();
                if (!string.IsNullOrWhiteSpace(xff))
                {
                    var first = xff.Split(',')[0].Trim();
                    if (first.Length > 0) return first;
                }
                return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            }

            static bool IsLoopback(HttpContext context)
            {
                var ip = context.Connection.RemoteIpAddress;
                return ip is not null && IPAddress.IsLoopback(ip);
            }

            options.AddPolicy("auth", context =>
            {
                // Loopback in Development: no limiter (don't block local testing).
                if (builder.Environment.IsDevelopment() && IsLoopback(context))
                {
                    return RateLimitPartition.GetNoLimiter<string>(ClientIp(context));
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientIp(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    });
            });
        });

        return builder;
    }

    /// <summary>Applies pending migrations and seeds demo data.</summary>
    /// <param name="app">The running app.</param>
    private static void SeedDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();
        // EnsureCreated() does not alter existing tables, so when a new column
        // is added to a model that already has a database (e.g. Notification.Type),
        // we add it explicitly here. Idempotent + provider-aware. Swap for EF
        // migrations in production.
        EnsureNotificationsTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureNotificationTypeColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCustomerAccountTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseCommentsTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseResolvedAtColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseFollowUpDueUtcColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseLastOverdueNotifiedUtcColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureConversationReadStatesTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureUserResetTokenColumns(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseDisplayIdColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseAssignedAtUtcColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureAccountActivatedAtColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCustomerUpdatedAtUtcColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCustomerSoftDeleteColumns(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCaseSoftDeleteColumns(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCustomerActivitiesTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureCustomerActivityCaseIdColumn(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureViewEventsTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureRefreshTokensTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        SeedDataInitializer.Initialize(ctx);
        // Backfill any customer missing a display ID (e.g. rows created by the
        // self-signup path before this sequence existed) and seed the singleton
        // generator so subsequent creates continue above the highest existing
        // value. Idempotent: only NULL display IDs are filled.
        var displayIdGenerator = scope.ServiceProvider.GetRequiredService<ICustomerDisplayIdGenerator>();
        EnsureCustomerDisplayIds(ctx, displayIdGenerator, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates the <c>RefreshTokens</c> table if it is missing. Needed because
    /// the project uses <c>EnsureCreated()</c> (no migrations), which will not
    /// create a table for a model added after the database already exists.
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureRefreshTokensTable(AppDbContext ctx, string? provider)
    {
        const string table = "RefreshTokens";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [Token] TEXT NOT NULL,
                        [SubjectId] TEXT NOT NULL,
                        [SubjectType] TEXT NOT NULL,
                        [Role] TEXT NOT NULL,
                        [CreatedUtc] TEXT NOT NULL,
                        [ExpiresUtc] TEXT NOT NULL,
                        [RevokedUtc] TEXT,
                        [ReplacedByToken] TEXT
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idxToken = conn.CreateCommand();
                    idxToken.CommandText = $"CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [{table}] ([Token]);";
                    await idxToken.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [Token] nvarchar(128) NOT NULL, " +
                    $"[SubjectId] nvarchar(100) NOT NULL, [SubjectType] nvarchar(20) NOT NULL, " +
                    $"[Role] nvarchar(50) NOT NULL, [CreatedUtc] datetime2 NOT NULL, " +
                    $"[ExpiresUtc] datetime2 NOT NULL, [RevokedUtc] datetime2, [ReplacedByToken] nvarchar(128), " +
                    $"CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]));");
                _ = exists;
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RefreshTokens_Token') " +
                    $"CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [{table}] ([Token]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the <c>Notifications</c> table if it is missing. Needed because
    /// the project uses <c>EnsureCreated()</c> (no migrations), which will not
    /// create a table for a model added after the database already exists.
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureNotificationsTable(AppDbContext ctx, string? provider)
    {
        const string table = "Notifications";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [Title] TEXT NOT NULL,
                        [Message] TEXT NOT NULL,
                        [Channel] INTEGER NOT NULL,
                        [Status] INTEGER NOT NULL,
                        [Type] INTEGER NOT NULL DEFAULT 0,
                        [CreatedAtUtc] TEXT NOT NULL,
                        [Link] TEXT,
                        [CaseId] INTEGER,
                        [Recipient] TEXT
                    );";
                    await create.ExecuteNonQueryAsync();
                }
            }
            else
            {
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [Title] nvarchar(200) NOT NULL, [Message] nvarchar(1000) NOT NULL, " +
                    $"[Channel] int NOT NULL, [Status] int NOT NULL, [Type] int NOT NULL DEFAULT 0, " +
                    $"[CreatedAtUtc] datetime2 NOT NULL, [Link] nvarchar(200), [CaseId] int, [Recipient] nvarchar(200), " +
                    $"CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]));");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Notifications.Type</c> column if it is missing. Needed because
    /// the project uses <c>EnsureCreated()</c> (no migrations), which will not
    /// alter an existing table when the model gains a column.
    /// </summary>
    private static async Task EnsureNotificationTypeColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Notifications";
        const string column = "Type";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] int NOT NULL DEFAULT 0;");
                // ExecuteSqlRaw returns -1 for DDL; the IF guards re-runs.
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: if the column already exists or the provider differs,
            // the app should still start. Log and continue.
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the <c>CustomerAccounts</c> table if it is missing. Needed
    /// because the project uses <c>EnsureCreated()</c> (no migrations), which
    /// will not create a table for a model added after the database already
    /// exists. Idempotent + provider-aware. Swap for EF migrations in
    /// production.
    /// </summary>
    private static async Task EnsureCustomerAccountTable(AppDbContext ctx, string? provider)
    {
        const string table = "CustomerAccounts";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [CustomerId] INTEGER NOT NULL,
                        [PasswordHash] TEXT,
                        [InviteToken] TEXT,
                        [InviteTokenExpiresAt] TEXT,
                        [InviteTokenUsed] INTEGER NOT NULL,
                        [IsActive] INTEGER NOT NULL,
                        [CreatedAtUtc] TEXT NOT NULL,
                        CONSTRAINT [FK_CustomerAccounts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idxToken = conn.CreateCommand();
                    idxToken.CommandText = $"CREATE UNIQUE INDEX [IX_CustomerAccounts_InviteToken] ON [{table}] ([InviteToken]);";
                    await idxToken.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [CustomerId] int NOT NULL, [PasswordHash] nvarchar(200), " +
                    $"[InviteToken] nvarchar(128), [InviteTokenExpiresAt] datetime2, " +
                    $"[InviteTokenUsed] bit NOT NULL, [IsActive] bit NOT NULL, [CreatedAtUtc] datetime2 NOT NULL, " +
                    $"CONSTRAINT [PK_CustomerAccounts] PRIMARY KEY ([Id]), " +
                    $"CONSTRAINT [FK_CustomerAccounts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE);");
                _ = exists;
                // Add the unique index on InviteToken separately (IF NOT EXISTS guard).
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CustomerAccounts_InviteToken') " +
                    $"CREATE UNIQUE INDEX [IX_CustomerAccounts_InviteToken] ON [{table}] ([InviteToken]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the <c>CaseComments</c> table if it is missing. Needed because
    /// the project uses <c>EnsureCreated()</c> (no migrations), which will not
    /// create a table for a model added after the database already exists.
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCaseCommentsTable(AppDbContext ctx, string? provider)
    {
        const string table = "CaseComments";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [CaseId] INTEGER NOT NULL,
                        [AuthorUserId] TEXT,
                        [AuthorCustomerId] INTEGER,
                        [Body] TEXT NOT NULL,
                        [CreatedAtUtc] TEXT NOT NULL,
                        CONSTRAINT [FK_CaseComments_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_CaseComments_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL,
                        CONSTRAINT [FK_CaseComments_Customers_AuthorCustomerId] FOREIGN KEY ([AuthorCustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idx = conn.CreateCommand();
                    idx.CommandText = $"CREATE INDEX [IX_CaseComments_CaseId] ON [{table}] ([CaseId]);";
                    await idx.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [CaseId] int NOT NULL, [AuthorUserId] nvarchar(450), " +
                    $"[AuthorCustomerId] int, [Body] nvarchar(4000) NOT NULL, [CreatedAtUtc] datetime2 NOT NULL, " +
                    $"CONSTRAINT [PK_CaseComments] PRIMARY KEY ([Id]), " +
                    $"CONSTRAINT [FK_CaseComments_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE, " +
                    $"CONSTRAINT [FK_CaseComments_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL, " +
                    $"CONSTRAINT [FK_CaseComments_Customers_AuthorCustomerId] FOREIGN KEY ([AuthorCustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION);");
                _ = exists;
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CaseComments_CaseId') " +
                    $"CREATE INDEX [IX_CaseComments_CaseId] ON [{table}] ([CaseId]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Cases.ResolvedAtUtc</c> column if it is missing. Needed
    /// because the project uses <c>EnsureCreated()</c> (no migrations), which
    /// will not alter an existing table when the model gains a column.
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCaseResolvedAtColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        const string column = "ResolvedAtUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Cases.FollowUpDueUtc</c> column if it is missing. Pre-existing
    /// live databases created before this column was added to the model lack it,
    /// which makes any query that materializes the full <c>Case</c> entity fail.
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCaseFollowUpDueUtcColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        const string column = "FollowUpDueUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Cases.LastOverdueNotifiedUtc</c> column if it is missing
    /// (Phase 44 durable overdue de-dup marker). Mirrors the other Ensure*Column
    /// helpers: idempotent + provider-aware. Swap for EF migrations in production.
    /// ponytail: reuse the established pattern instead of introducing a migration
    /// toolchain (dotnet ef not installed in this environment).
    /// </summary>
    private static async Task EnsureCaseLastOverdueNotifiedUtcColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        const string column = "LastOverdueNotifiedUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the <c>ConversationReadStates</c> table if it is missing. Needed
    /// because the project uses <c>EnsureCreated()</c> (no migrations), which
    /// will not create a table for a model added after the database already
    /// exists. Idempotent + provider-aware. Swap for EF migrations in
    /// production.
    /// </summary>
    private static async Task EnsureConversationReadStatesTable(AppDbContext ctx, string? provider)
    {
        const string table = "ConversationReadStates";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [AgentUserId] TEXT NOT NULL,
                        [CaseId] INTEGER NOT NULL,
                        [LastViewedUtc] TEXT NOT NULL
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idx = conn.CreateCommand();
                    idx.CommandText = $"CREATE INDEX [IX_ConversationReadStates_AgentCase] ON [{table}] ([AgentUserId], [CaseId]);";
                    await idx.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [AgentUserId] nvarchar(100) NOT NULL, " +
                    $"[CaseId] int NOT NULL, [LastViewedUtc] datetime2 NOT NULL, " +
                    $"CONSTRAINT [PK_ConversationReadStates] PRIMARY KEY ([Id]));");
                _ = exists;
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ConversationReadStates_AgentCase') " +
                    $"CREATE INDEX [IX_ConversationReadStates_AgentCase] ON [{table}] ([AgentUserId], [CaseId]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the staff password-reset columns (<c>ResetToken</c>,
    /// <c>ResetTokenExpiresAt</c>, <c>ResetTokenUsed</c>) to the <c>Users</c>
    /// table if they are missing. Needed because <c>EnsureCreated()</c> will not
    /// add columns to an existing table. Idempotent + provider-aware.
    /// </summary>
    private static async Task EnsureUserResetTokenColumns(AppDbContext ctx, string? provider)
    {
        var columns = new[] { "ResetToken", "ResetTokenExpiresAt", "ResetTokenUsed" };
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                foreach (var col in columns)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name='{col}';";
                    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (count == 0)
                    {
                        var type = col == "ResetTokenUsed" ? "INTEGER NOT NULL DEFAULT 0"
                                 : col == "ResetTokenExpiresAt" ? "TEXT"
                                 : "TEXT";
                        using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE Users ADD COLUMN {col} {type};";
                        await alter.ExecuteNonQueryAsync();
                    }
                }
            }
            else
            {
                // SQL Server — each ALTER is a no-op if the column already exists.
                ctx.Database.ExecuteSqlRaw(
                    "IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='ResetToken') " +
                    "ALTER TABLE Users ADD [ResetToken] nvarchar(128) NULL;");
                ctx.Database.ExecuteSqlRaw(
                    "IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='ResetTokenExpiresAt') " +
                    "ALTER TABLE Users ADD [ResetTokenExpiresAt] datetime2 NULL;");
                ctx.Database.ExecuteSqlRaw(
                    "IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='ResetTokenUsed') " +
                    "ALTER TABLE Users ADD [ResetTokenUsed] bit NOT NULL DEFAULT 0;");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure Users reset-token columns: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Cases.CaseDisplayId</c> column if it is missing. Needed because
    /// <c>EnsureCreated()</c> does not alter existing tables when the model gains
    /// a column (e.g. when <c>Case.CaseDisplayId</c> was added).
    /// Idempotent + provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCaseDisplayIdColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        const string column = "CaseDisplayId";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] nvarchar(20) NULL;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Cases.AssignedAtUtc</c> column if missing. Needed because
    /// <c>EnsureCreated()</c> does not alter existing tables. Idempotent +
    /// provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCaseAssignedAtUtcColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        const string column = "AssignedAtUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2 NULL;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>CustomerAccounts.ActivatedAtUtc</c> column if it is missing.
    /// Needed because <c>EnsureCreated()</c> does not alter existing tables when
    /// the model gains a column (e.g. when <c>CustomerAccount.ActivatedAtUtc</c>
    /// was added to record account-activation time). Idempotent + provider-aware.
    /// Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureAccountActivatedAtColumn(AppDbContext ctx, string? provider)
    {
        const string table = "CustomerAccounts";
        const string column = "ActivatedAtUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2 NULL;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the <c>Customers.UpdatedAtUtc</c> column if it is missing. Records
    /// the last account-level profile edit (name/email/phone/company/address),
    /// surfaced by the Customers sidenav badge as "info updated since I last
    /// looked". Needed because <c>EnsureCreated()</c> does not alter existing
    /// tables when the model gains a column. Idempotent + provider-aware.
    /// Seed rows stay <c>UpdatedAtUtc: null</c> (this bootstrap adds the column
    /// but does not backfill) — intentional, so first-visit badges aren't
    /// polluted by stale data. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCustomerUpdatedAtUtcColumn(AppDbContext ctx, string? provider)
    {
        const string table = "Customers";
        const string column = "UpdatedAtUtc";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] datetime2 NULL;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the soft-delete / purge columns exist on <c>Customers</c>. Needed
    /// because <c>EnsureCreated()</c> does not alter existing tables when the
    /// model gains columns (here: IsDeleted, DeletedAtUtc, DeletedById, Purged,
    /// PurgedAtUtc, RestoredById, RestoredAtUtc). Idempotent + provider-aware.
    /// Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCustomerSoftDeleteColumns(AppDbContext ctx, string? provider)
    {
        const string table = "Customers";
        // (column name, sqlite type, sqlserver type)
        var columns = new (string Name, string Sqlite, string SqlServer)[]
        {
            ("IsDeleted", "INTEGER NOT NULL DEFAULT 0", "bit NOT NULL DEFAULT 0"),
            ("DeletedAtUtc", "TEXT NULL", "datetime2 NULL"),
            ("DeletedById", "TEXT NULL", "nvarchar(450) NULL"),
            ("Purged", "INTEGER NOT NULL DEFAULT 0", "bit NOT NULL DEFAULT 0"),
            ("PurgedAtUtc", "TEXT NULL", "datetime2 NULL"),
            ("RestoredById", "TEXT NULL", "nvarchar(450) NULL"),
            ("RestoredAtUtc", "TEXT NULL", "datetime2 NULL"),
        };
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                foreach (var col in columns)
                {
                    using var check = conn.CreateCommand();
                    check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{col.Name}';";
                    var count = Convert.ToInt32(await check.ExecuteScalarAsync());
                    if (count == 0)
                    {
                        using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {col.Name} {col.Sqlite};";
                        await alter.ExecuteNonQueryAsync();
                    }
                }
            }
            else
            {
                foreach (var col in columns)
                {
                    var exists = ctx.Database.ExecuteSqlRaw(
                        $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{col.Name}') " +
                        $"ALTER TABLE {table} ADD [{col.Name}] {col.SqlServer};");
                    _ = exists;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} soft-delete columns: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the soft-delete / purge columns exist on <c>Cases</c>. Needed
    /// because <c>EnsureCreated()</c> does not alter existing tables when the
    /// model gains columns (here: IsDeleted, DeletedAtUtc, DeletedById, Purged,
    /// PurgedAtUtc). Idempotent + provider-aware. Swap for EF migrations in
    /// production.
    /// </summary>
    private static async Task EnsureCaseSoftDeleteColumns(AppDbContext ctx, string? provider)
    {
        const string table = "Cases";
        // (column name, sqlite type, sqlserver type)
        var columns = new (string Name, string Sqlite, string SqlServer)[]
        {
            ("IsDeleted", "INTEGER NOT NULL DEFAULT 0", "bit NOT NULL DEFAULT 0"),
            ("DeletedAtUtc", "TEXT NULL", "datetime2 NULL"),
            ("DeletedById", "TEXT NULL", "nvarchar(450) NULL"),
            ("Purged", "INTEGER NOT NULL DEFAULT 0", "bit NOT NULL DEFAULT 0"),
            ("PurgedAtUtc", "TEXT NULL", "datetime2 NULL"),
        };
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                foreach (var col in columns)
                {
                    using var check = conn.CreateCommand();
                    check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{col.Name}';";
                    var count = Convert.ToInt32(await check.ExecuteScalarAsync());
                    if (count == 0)
                    {
                        using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {col.Name} {col.Sqlite};";
                        await alter.ExecuteNonQueryAsync();
                    }
                }
            }
            else
            {
                foreach (var col in columns)
                {
                    var exists = ctx.Database.ExecuteSqlRaw(
                        $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{col.Name}') " +
                        $"ALTER TABLE {table} ADD [{col.Name}] {col.SqlServer};");
                    _ = exists;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} soft-delete columns: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the <c>CustomerActivities</c> audit table if it is missing.
    /// Holds explicit audit rows for customer-account activity that is NOT
    /// derivable from the case graph or Notification table (today: profile/
    /// account field edits by staff or the customer themselves). Needed because
    /// <c>EnsureCreated()</c> will not create a table for a model added after
    /// the database already exists. Idempotent + provider-aware. Swap for EF
    /// migrations in production.
    /// </summary>
    private static async Task EnsureCustomerActivitiesTable(AppDbContext ctx, string? provider)
    {
        const string table = "CustomerActivities";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [CustomerId] INTEGER NOT NULL,
                        [CaseId] INTEGER NULL,
                        [Kind] TEXT NOT NULL,
                        [Label] TEXT NOT NULL,
                        [Detail] TEXT NULL,
                        [AtUtc] TEXT NOT NULL,
                        [ActorUserId] TEXT NULL,
                        [ActorRole] TEXT NULL
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idx = conn.CreateCommand();
                    idx.CommandText = $"CREATE INDEX [IX_CustomerActivities_CustomerId] ON [{table}] ([CustomerId]);";
                    await idx.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [CustomerId] int NOT NULL, [CaseId] int NULL, " +
                    $"[Kind] nvarchar(50) NOT NULL, [Label] nvarchar(100) NOT NULL, " +
                    $"[Detail] nvarchar(500) NULL, [AtUtc] datetime2 NOT NULL, " +
                    $"[ActorUserId] nvarchar(100) NULL, [ActorRole] nvarchar(50) NULL, " +
                    $"CONSTRAINT [PK_CustomerActivities] PRIMARY KEY ([Id]));");
                _ = exists;
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CustomerActivities_CustomerId') " +
                    $"CREATE INDEX [IX_CustomerActivities_CustomerId] ON [{table}] ([CustomerId]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }
    /// <summary>
    /// Ensures the optional <c>CaseId</c> column exists on <c>CustomerActivities</c>.
    /// Added so case-level lifecycle events (case_deleted / case_restored) can be
    /// stored in the same unified audit table as account events. Idempotent +
    /// provider-aware. Swap for EF migrations in production.
    /// </summary>
    private static async Task EnsureCustomerActivityCaseIdColumn(AppDbContext ctx, string? provider)
    {
        const string table = "CustomerActivities";
        const string column = "CaseId";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                using var check = conn.CreateCommand();
                check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
                var count = Convert.ToInt32(await check.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NULL;";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{column}') " +
                    $"ALTER TABLE {table} ADD [{column}] int NULL;");
                _ = exists;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table}.{column} column: {ex.Message}");
        }
    }


    /// <summary>
    /// Creates the <c>ViewEvents</c> table if it is missing (read/view audit
    /// rows for Case + Customer detail pages). Mirrors <see cref="EnsureCustomerActivitiesTable"/>:
    /// idempotent raw SQL so a model added after the database exists is created
    /// without EF migrations. No FK to Case/Customer — the log must survive
    /// target deletion.
    /// </summary>
    private static async Task EnsureViewEventsTable(AppDbContext ctx, string? provider)
    {
        const string table = "ViewEvents";
        try
        {
            if (provider != null && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var create = conn.CreateCommand();
                    create.CommandText = $@"CREATE TABLE [{table}] (
                        [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
                        [TargetType] TEXT NOT NULL,
                        [TargetId] INTEGER NOT NULL,
                        [ViewerUserId] TEXT NULL,
                        [ViewerName] TEXT NOT NULL,
                        [ViewerRole] TEXT NULL,
                        [AtUtc] TEXT NOT NULL
                    );";
                    await create.ExecuteNonQueryAsync();
                    using var idx = conn.CreateCommand();
                    idx.CommandText = $"CREATE INDEX [IX_ViewEvents_Target] ON [{table}] ([TargetType], [TargetId]);";
                    await idx.ExecuteNonQueryAsync();
                }
            }
            else
            {
                var exists = ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='{table}') " +
                    $"CREATE TABLE [{table}] (" +
                    $"[Id] int IDENTITY(1,1) NOT NULL, [TargetType] nvarchar(20) NOT NULL, " +
                    $"[TargetId] int NOT NULL, [ViewerUserId] nvarchar(100) NULL, " +
                    $"[ViewerName] nvarchar(200) NOT NULL, [ViewerRole] nvarchar(50) NULL, " +
                    $"[AtUtc] datetime2 NOT NULL, " +
                    $"CONSTRAINT [PK_ViewEvents] PRIMARY KEY ([Id]));");
                _ = exists;
                ctx.Database.ExecuteSqlRaw(
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ViewEvents_Target') " +
                    $"CREATE INDEX [IX_ViewEvents_Target] ON [{table}] ([TargetType], [TargetId]);");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not ensure {table} table: {ex.Message}");
        }
    }

    /// <summary>
    /// Backfills any <c>Customer</c> row with a missing (NULL/empty) display ID
    /// using the shared monotonic sequence, and seeds that sequence from the
    /// highest existing suffix so future creates continue above it. This covers
    /// rows created before the display-ID sequence existed (e.g. the self-signup
    /// path) without clobbering rows that already have a value. Idempotent: only
    /// NULL display IDs are assigned, and they are assigned in Id order so the
    /// result is deterministic across runs.
    /// </summary>
    private static async Task EnsureCustomerDisplayIds(
        AppDbContext ctx, ICustomerDisplayIdGenerator displayIdGenerator, string? provider)
    {
        try
        {
            var customers = await ctx.Customers
                .OrderBy(c => c.Id)
                .ToListAsync();

            // Seed the sequence from ALL rows (including ones we won't touch) so
            // the next emitted value sits above the highest existing suffix and
            // never collides with or reuses an existing display ID.
            displayIdGenerator.SeedFrom(customers.Select(c => c.CustomerDisplayId));

            var missing = customers
                .Where(c => string.IsNullOrWhiteSpace(c.CustomerDisplayId))
                .ToList();
            if (missing.Count == 0)
            {
                return;
            }

            foreach (var customer in missing)
            {
                customer.CustomerDisplayId = displayIdGenerator.Next();
            }
            await ctx.SaveChangesAsync();
            Console.WriteLine($"INFO: backfilled {missing.Count} customer display ID(s).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN: could not backfill customer display IDs: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the ONNX model path so it is found regardless of the process
    /// working directory. Tries, in order: the configured path as-is, relative
    /// to the content root, and relative to the solution/repo root (the model
    /// lives at &lt;repo&gt;/ml/models/priority_model.onnx). Returns the first
    /// existing path, or the content-root-relative path when none exist (so the
    /// caller can log a clear "not found" message).
    /// </summary>
    private static string? ResolveModelPath(string? configuredPath, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var candidates = new List<string> { configuredPath! };
        if (!Path.IsPathRooted(configuredPath))
        {
            candidates.Add(Path.Combine(contentRoot, configuredPath));
            // Walk up from the content root looking for an "ml/models" folder
            // (content root is typically <repo>/backend/src/CustomerService.Api).
            var dir = new DirectoryInfo(contentRoot);
            while (dir != null)
            {
                var repoCandidate = Path.Combine(dir.FullName, configuredPath);
                if (!candidates.Contains(repoCandidate))
                {
                    candidates.Add(repoCandidate);
                }
                if (Directory.Exists(Path.Combine(dir.FullName, "ml")))
                {
                    break;
                }
                dir = dir.Parent;
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
