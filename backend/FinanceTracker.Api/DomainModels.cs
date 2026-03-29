namespace FinanceTracker.Api;

public enum EntryType
{
    Income,
    Expense,
    Transfer
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Category
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
    public string Icon { get; set; } = "tag";
    public bool IsArchived { get; set; }
}

public sealed class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class FinanceTransaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? DestinationAccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public EntryType Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Budget
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public int AlertThresholdPercent { get; set; } = 80;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }
}

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record AuthResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record RefreshTokenIssue(string Token, DateTime ExpiresAtUtc);

public sealed record RefreshTokenExchangeResult(User User, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

public sealed record CreateAccountRequest(string Name, string Type, decimal OpeningBalance, string? InstitutionName);

public sealed record CreateBudgetRequest(Guid CategoryId, int Month, int Year, decimal Amount, int? AlertThresholdPercent);

public sealed record UpdateBudgetRequest(Guid CategoryId, int Month, int Year, decimal Amount, int? AlertThresholdPercent);

public sealed record CreateTransactionRequest(
    Guid AccountId,
    Guid? DestinationAccountId,
    Guid? CategoryId,
    string Type,
    decimal Amount,
    DateOnly Date,
    string? Merchant,
    string? Note,
    string? PaymentMethod);

public sealed record UpdateTransactionRequest(
    Guid AccountId,
    Guid? DestinationAccountId,
    Guid? CategoryId,
    string Type,
    decimal Amount,
    DateOnly Date,
    string? Merchant,
    string? Note,
    string? PaymentMethod);

public sealed record TransactionQuery(
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Type,
    Guid? AccountId,
    Guid? CategoryId,
    string? Search);

public sealed record OperationResult<T>(bool Success, T? Payload, string? Error)
{
    public static OperationResult<T> Ok(T payload) => new(true, payload, null);

    public static OperationResult<T> Fail(string error) => new(false, default, error);
}

public sealed record CategorySpendItem(string Category, decimal Amount);

public sealed record DashboardSummaryResponse(
    decimal MonthIncome,
    decimal MonthExpense,
    decimal NetBalance,
    IReadOnlyList<CategorySpendItem> CategorySpend,
    IReadOnlyList<FinanceTransaction> RecentTransactions);

public sealed record BudgetProgressItem(
    Guid BudgetId,
    Guid CategoryId,
    string CategoryName,
    int Month,
    int Year,
    decimal BudgetAmount,
    decimal ActualExpense,
    decimal UtilizationPercent,
    bool IsOverBudget,
    bool IsThresholdReached);
