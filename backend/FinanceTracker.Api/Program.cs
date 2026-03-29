using System.Security.Claims;
using System.Text;
using FinanceTracker.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Services.AddOpenApi();
    var dbSafetyOptions = builder.Configuration.GetSection(DatabaseSafetyOptions.SectionName).Get<DatabaseSafetyOptions>() ?? new();
    DatabaseSafetyGuard.Validate(builder.Configuration, builder.Environment, dbSafetyOptions);
    var connectionString = builder.Configuration.GetConnectionString(dbSafetyOptions.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{dbSafetyOptions.ConnectionStringName}' is missing.");

    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddSingleton<AppStore>();
    var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?.Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    builder.Services.AddSingleton(jwtOptions);
    builder.Services.AddSingleton<JwtTokenService>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("frontend", policy =>
        {
            if (configuredCorsOrigins.Length > 0)
            {
                policy.WithOrigins(configuredCorsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
                return;
            }

            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1"))
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(20)
            };
        });

    builder.Services.AddAuthorization();

    var app = builder.Build();
    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = dbFactory.CreateDbContext();
        db.Database.Migrate();
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseCors("frontend");
    app.UseAuthentication();
    app.UseAuthorization();

    var appUrl = builder.Configuration["APP_URL"];
    if (!string.IsNullOrWhiteSpace(appUrl))
    {
        app.Urls.Add(appUrl);
    }
    else if (app.Environment.IsDevelopment())
    {
        app.Urls.Add("http://localhost:5052");
    }

    var api = app.MapGroup("/api");
    api.MapGet("/health", () => Results.Ok(new { status = "ok", service = "finance-tracker-api" }));

    api.MapPost("/auth/register", (RegisterRequest request, AppStore store, JwtTokenService jwt) =>
    {
        var result = store.Register(request);
        if (!result.Success || result.Payload is null)
        {
            return Results.BadRequest(new { message = result.Error });
        }

        return BuildAuthSuccess(result.Payload, store, jwt, jwtOptions);
    });

    api.MapPost("/auth/login", (LoginRequest request, AppStore store, JwtTokenService jwt) =>
    {
        var result = store.Login(request);
        if (!result.Success || result.Payload is null)
        {
            return Results.BadRequest(new { message = result.Error });
        }

        return BuildAuthSuccess(result.Payload, store, jwt, jwtOptions);
    });

    api.MapPost("/auth/refresh", (RefreshTokenRequest request, AppStore store, JwtTokenService jwt) =>
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { message = "Refresh token is required." });
        }

        var exchange = store.RotateRefreshToken(request.RefreshToken.Trim(), jwtOptions.RefreshTokenTtl);
        if (!exchange.Success || exchange.Payload is null)
        {
            return Results.Unauthorized();
        }

        var access = jwt.CreateAccessToken(exchange.Payload.User);
        var response = new AuthResponse(
            exchange.Payload.User.Id,
            exchange.Payload.User.Email,
            exchange.Payload.User.DisplayName,
            access.Token,
            exchange.Payload.RefreshToken,
            access.ExpiresAtUtc,
            exchange.Payload.RefreshTokenExpiresAtUtc);

        return Results.Ok(response);
    });

    var secured = api.MapGroup(string.Empty).RequireAuthorization();

    secured.MapGet("/categories", (ClaimsPrincipal principal, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        return Results.Ok(store.GetCategories(userId));
    });

    secured.MapGet("/accounts", (ClaimsPrincipal principal, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        return Results.Ok(store.GetAccounts(userId));
    });

    secured.MapGet("/budgets", (ClaimsPrincipal principal, AppStore store, int? month, int? year) =>
    {
        var userId = GetRequiredUserId(principal);
        return Results.Ok(store.GetBudgets(userId, month, year));
    });

    secured.MapPost("/budgets", (ClaimsPrincipal principal, CreateBudgetRequest payload, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.UpsertBudget(
            userId,
            budgetId: null,
            payload.CategoryId,
            payload.Month,
            payload.Year,
            payload.Amount,
            payload.AlertThresholdPercent ?? 80);

        return result.Success
            ? Results.Ok(result.Payload)
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapPut("/budgets/{id:guid}", (ClaimsPrincipal principal, Guid id, UpdateBudgetRequest payload, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.UpsertBudget(
            userId,
            budgetId: id,
            payload.CategoryId,
            payload.Month,
            payload.Year,
            payload.Amount,
            payload.AlertThresholdPercent ?? 80);

        return result.Success
            ? Results.Ok(result.Payload)
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapDelete("/budgets/{id:guid}", (ClaimsPrincipal principal, Guid id, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.DeleteBudget(userId, id);
        return result.Success
            ? Results.Ok(new { deleted = true, id })
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapGet("/budgets/progress", (ClaimsPrincipal principal, AppStore store, int month, int year) =>
    {
        var userId = GetRequiredUserId(principal);
        if (month is < 1 or > 12 || year is < 2000 or > 3000)
        {
            return Results.BadRequest(new { message = "Invalid month or year." });
        }

        return Results.Ok(store.GetBudgetProgress(userId, month, year));
    });

    secured.MapPost("/accounts", (ClaimsPrincipal principal, CreateAccountRequest payload, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.CreateAccount(userId, payload);
        return result.Success
            ? Results.Ok(result.Payload)
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapGet(
        "/transactions",
        (ClaimsPrincipal principal, AppStore store, DateOnly? startDate, DateOnly? endDate, string? type, Guid? accountId, Guid? categoryId, string? search) =>
        {
            var userId = GetRequiredUserId(principal);
            var query = new TransactionQuery(startDate, endDate, type, accountId, categoryId, search);
            return Results.Ok(store.GetTransactions(userId, query));
        });

    secured.MapPost("/transactions", (ClaimsPrincipal principal, CreateTransactionRequest payload, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.CreateTransaction(userId, payload);
        return result.Success
            ? Results.Ok(result.Payload)
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapPut("/transactions/{id:guid}", (ClaimsPrincipal principal, Guid id, UpdateTransactionRequest payload, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.UpdateTransaction(userId, id, payload);
        return result.Success
            ? Results.Ok(result.Payload)
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapDelete("/transactions/{id:guid}", (ClaimsPrincipal principal, Guid id, AppStore store) =>
    {
        var userId = GetRequiredUserId(principal);
        var result = store.DeleteTransaction(userId, id);
        return result.Success
            ? Results.Ok(new { deleted = true, id })
            : Results.BadRequest(new { message = result.Error });
    });

    secured.MapGet("/dashboard/summary", (ClaimsPrincipal principal, AppStore store, int? month, int? year) =>
    {
        var userId = GetRequiredUserId(principal);
        var now = DateTime.UtcNow;
        var summary = store.GetDashboardSummary(userId, month ?? now.Month, year ?? now.Year);
        return Results.Ok(summary);
    });

    app.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine("FinanceTracker API startup failed.");
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ex.ToString());
    Environment.Exit(1);
}

static IResult BuildAuthSuccess(User user, AppStore store, JwtTokenService jwt, JwtOptions options)
{
    var refreshIssue = store.IssueRefreshToken(user.Id, options.RefreshTokenTtl);
    if (!refreshIssue.Success || refreshIssue.Payload is null)
    {
        return Results.BadRequest(new { message = refreshIssue.Error ?? "Unable to issue refresh token." });
    }

    var access = jwt.CreateAccessToken(user);
    var response = new AuthResponse(
        user.Id,
        user.Email,
        user.DisplayName,
        access.Token,
        refreshIssue.Payload.Token,
        access.ExpiresAtUtc,
        refreshIssue.Payload.ExpiresAtUtc);

    return Results.Ok(response);
}

static Guid GetRequiredUserId(ClaimsPrincipal principal)
{
    var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (value is null || !Guid.TryParse(value, out var userId))
    {
        throw new UnauthorizedAccessException("Invalid token subject.");
    }

    return userId;
}
