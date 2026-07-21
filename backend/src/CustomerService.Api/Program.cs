using System.Data;
using System.IO;
using System.Text;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain.Interfaces;
using CustomerService.Infrastructure.Data;
using CustomerService.Infrastructure.Repositories;
using CustomerService.ML;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

        app.UseCors("AllowAngularDev");        app.UseMiddleware<CustomerService.Api.Middleware.ApiExceptionMiddleware>();        app.UseAuthentication();
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

        var provider = config["Database:Provider"] ?? "SqlServer";
        builder.Services.AddDbContext<AppDbContext>(options =>
        {
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
        builder.Services.AddScoped<ICallLogService, CallLogService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<ICustomerAuthService, CustomerAuthService>();
        builder.Services.AddScoped<IDashboardService, DashboardService>();

        builder.Services.AddScoped<InAppNotificationSender>();
        builder.Services.AddScoped<EmailNotificationSender>();
        builder.Services.AddScoped<SmsNotificationSender>();
        builder.Services.AddScoped<INotificationSender>(sp => sp.GetRequiredService<CompositeNotificationSender>());
        // CompositeNotificationSender routes each notification to the sender
        // that handles its channel; the app consumes only this single
        // INotificationSender. See docs/DIY.md §7.
        builder.Services.AddScoped<CompositeNotificationSender>();
        builder.Services.Configure<CustomerService.Application.Options.NotificationOptions>(
            builder.Configuration.GetSection("Notifications"));
        // Register the resolved options as a concrete service so the Email/SMS
        // senders can take NotificationOptions directly (not just IOptions<>).
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerService.Application.Options.NotificationOptions>>().Value);
        builder.Services.Configure<CustomerService.Application.Options.EmailOptions>(
            builder.Configuration.GetSection("Email"));
        // Register the resolved EmailOptions as a concrete service so the email
        // sender can take it directly (not just IOptions<>).
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerService.Application.Options.EmailOptions>>().Value);
        builder.Services.AddScoped<INotificationService, NotificationService>();
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

        var jwtKey = config["Jwt:Key"] ?? "dev-insecure-key-change-me-1234567890";
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
            });

        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()));
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
            options.AddPolicy("AllowAngularDev", policy =>
                policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod());
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
        EnsureConversationReadStatesTable(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        EnsureUserResetTokenColumns(ctx, app.Configuration["Database:Provider"]).GetAwaiter().GetResult();
        SeedDataInitializer.Initialize(ctx);
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
