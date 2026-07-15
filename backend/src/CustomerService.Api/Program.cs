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
        builder.Services.AddScoped<ICallLogService, CallLogService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IDashboardService, DashboardService>();

        builder.Services.AddSingleton<IPriorityPredictor>(_ =>
        {
            var modelPath = config["ML:ModelPath"];
            return new OnnxPriorityPredictor(modelPath);
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
        SeedDataInitializer.Initialize(ctx);
    }
}
