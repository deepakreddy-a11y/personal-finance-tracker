using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api;

public sealed class AppStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public OperationResult<User> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var displayName = request.DisplayName.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            return OperationResult<User>.Fail("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return OperationResult<User>.Fail("Display name is required.");
        }

        if (!IsStrongPassword(request.Password))
        {
            return OperationResult<User>.Fail("Password must be 8+ chars with upper, lower, and number.");
        }

        using var db = dbFactory.CreateDbContext();
        if (db.Users.Any(x => x.Email == email))
        {
            return OperationResult<User>.Fail("Email already exists.");
        }

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = email,
            DisplayName = displayName,
            PasswordHash = HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Categories.AddRange(BuildDefaultCategories(userId));
        db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Primary Wallet",
            Type = "cash",
            OpeningBalance = 0m,
            CurrentBalance = 0m,
            InstitutionName = string.Empty,
            LastUpdatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
        return OperationResult<User>.Ok(CloneUser(user));
    }

    public OperationResult<User> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        using var db = dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(x => x.Email == email);
        if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            return OperationResult<User>.Fail("Invalid email or password.");
        }

        return OperationResult<User>.Ok(CloneUser(user));
    }

    public OperationResult<User> GetUserById(Guid userId)
    {
        using var db = dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(x => x.Id == userId);
        return user is null
            ? OperationResult<User>.Fail("User not found.")
            : OperationResult<User>.Ok(CloneUser(user));
    }

    public OperationResult<RefreshTokenIssue> IssueRefreshToken(Guid userId, TimeSpan ttl)
    {
        using var db = dbFactory.CreateDbContext();
        if (!db.Users.Any(x => x.Id == userId))
        {
            return OperationResult<RefreshTokenIssue>.Fail("User not found.");
        }

        var token = CreateRefreshTokenValue();
        var expiresAt = DateTime.UtcNow.Add(ttl);
        db.RefreshTokens.Add(new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            Token = token,
            UserId = userId,
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        return OperationResult<RefreshTokenIssue>.Ok(new RefreshTokenIssue(token, expiresAt));
    }

    public OperationResult<RefreshTokenExchangeResult> RotateRefreshToken(string refreshToken, TimeSpan ttl)
    {
        using var db = dbFactory.CreateDbContext();
        var current = db.RefreshTokens.FirstOrDefault(x => x.Token == refreshToken);
        if (current is null || current.RevokedAtUtc is not null || current.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return OperationResult<RefreshTokenExchangeResult>.Fail("Refresh token is expired or revoked.");
        }

        var user = db.Users.FirstOrDefault(x => x.Id == current.UserId);
        if (user is null)
        {
            return OperationResult<RefreshTokenExchangeResult>.Fail("User not found.");
        }

        var nextToken = CreateRefreshTokenValue();
        var nextExpiresAt = DateTime.UtcNow.Add(ttl);
        current.RevokedAtUtc = DateTime.UtcNow;
        current.ReplacedByToken = nextToken;

        db.RefreshTokens.Add(new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            Token = nextToken,
            UserId = user.Id,
            ExpiresAtUtc = nextExpiresAt,
            CreatedAtUtc = DateTime.UtcNow
        });

        db.SaveChanges();
        return OperationResult<RefreshTokenExchangeResult>.Ok(
            new RefreshTokenExchangeResult(CloneUser(user), nextToken, nextExpiresAt));
    }

    public IReadOnlyList<Category> GetCategories(Guid userId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Categories
            .Where(x => x.UserId == userId)
            .AsNoTracking()
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(CloneCategory)
            .ToList();
    }

    public IReadOnlyList<Account> GetAccounts(Guid userId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Accounts
            .Where(x => x.UserId == userId)
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(CloneAccount)
            .ToList();
    }

    public IReadOnlyList<Budget> GetBudgets(Guid userId, int? month, int? year)
    {
        using var db = dbFactory.CreateDbContext();
        IQueryable<Budget> query = db.Budgets.Where(x => x.UserId == userId);
        if (month.HasValue)
        {
            query = query.Where(x => x.Month == month.Value);
        }

        if (year.HasValue)
        {
            query = query.Where(x => x.Year == year.Value);
        }

        return query
            .AsNoTracking()
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ThenBy(x => x.CreatedAt)
            .Select(CloneBudget)
            .ToList();
    }

    public OperationResult<Account> CreateAccount(Guid userId, CreateAccountRequest payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            return OperationResult<Account>.Fail("Account name is required.");
        }

        if (string.IsNullOrWhiteSpace(payload.Type))
        {
            return OperationResult<Account>.Fail("Account type is required.");
        }

        using var db = dbFactory.CreateDbContext();
        if (!db.Users.Any(x => x.Id == userId))
        {
            return OperationResult<Account>.Fail("User not found.");
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = payload.Name.Trim(),
            Type = payload.Type.Trim().ToLowerInvariant(),
            OpeningBalance = payload.OpeningBalance,
            CurrentBalance = payload.OpeningBalance,
            InstitutionName = payload.InstitutionName?.Trim() ?? string.Empty,
            LastUpdatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(account);
        db.SaveChanges();
        return OperationResult<Account>.Ok(CloneAccount(account));
    }

    public OperationResult<Budget> UpsertBudget(Guid userId, Guid? budgetId, Guid categoryId, int month, int year, decimal amount, int alertThresholdPercent)
    {
        if (amount <= 0)
        {
            return OperationResult<Budget>.Fail("Budget amount must be greater than 0.");
        }

        if (month is < 1 or > 12)
        {
            return OperationResult<Budget>.Fail("Month must be between 1 and 12.");
        }

        if (year is < 2000 or > 3000)
        {
            return OperationResult<Budget>.Fail("Year is out of supported range.");
        }

        if (alertThresholdPercent is < 1 or > 200)
        {
            return OperationResult<Budget>.Fail("Alert threshold percent must be between 1 and 200.");
        }

        using var db = dbFactory.CreateDbContext();
        var hasCategory = db.Categories.Any(x => x.UserId == userId && x.Id == categoryId && x.Type == "expense" && !x.IsArchived);
        if (!hasCategory)
        {
            return OperationResult<Budget>.Fail("Selected category is invalid.");
        }

        var duplicate = db.Budgets.FirstOrDefault(x =>
            x.UserId == userId &&
            x.CategoryId == categoryId &&
            x.Month == month &&
            x.Year == year &&
            (!budgetId.HasValue || x.Id != budgetId.Value));
        if (duplicate is not null)
        {
            return OperationResult<Budget>.Fail("Budget already exists for this category and month.");
        }

        if (budgetId.HasValue)
        {
            var existing = db.Budgets.FirstOrDefault(x => x.UserId == userId && x.Id == budgetId.Value);
            if (existing is null)
            {
                return OperationResult<Budget>.Fail("Budget not found.");
            }

            existing.CategoryId = categoryId;
            existing.Month = month;
            existing.Year = year;
            existing.Amount = amount;
            existing.AlertThresholdPercent = alertThresholdPercent;
            existing.UpdatedAt = DateTime.UtcNow;
            db.SaveChanges();
            return OperationResult<Budget>.Ok(CloneBudget(existing));
        }

        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CategoryId = categoryId,
            Month = month,
            Year = year,
            Amount = amount,
            AlertThresholdPercent = alertThresholdPercent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Budgets.Add(budget);
        db.SaveChanges();
        return OperationResult<Budget>.Ok(CloneBudget(budget));
    }

    public OperationResult<bool> DeleteBudget(Guid userId, Guid budgetId)
    {
        using var db = dbFactory.CreateDbContext();
        var existing = db.Budgets.FirstOrDefault(x => x.UserId == userId && x.Id == budgetId);
        if (existing is null)
        {
            return OperationResult<bool>.Fail("Budget not found.");
        }

        db.Budgets.Remove(existing);
        db.SaveChanges();
        return OperationResult<bool>.Ok(true);
    }

    public IReadOnlyList<BudgetProgressItem> GetBudgetProgress(Guid userId, int month, int year)
    {
        using var db = dbFactory.CreateDbContext();
        var budgets = db.Budgets.Where(x => x.UserId == userId && x.Month == month && x.Year == year).ToList();
        var categories = db.Categories.Where(x => x.UserId == userId).ToDictionary(x => x.Id, x => x.Name);
        var transactions = db.Transactions
            .Where(x => x.UserId == userId && x.Type == EntryType.Expense && x.Date.Month == month && x.Date.Year == year)
            .ToList();

        return budgets
            .Select(budget =>
            {
                var actual = transactions.Where(x => x.CategoryId == budget.CategoryId).Sum(x => x.Amount);
                var utilization = budget.Amount == 0 ? 0 : Math.Round((actual / budget.Amount) * 100m, 2);
                return new BudgetProgressItem(
                    budget.Id,
                    budget.CategoryId,
                    categories.TryGetValue(budget.CategoryId, out var name) ? name : "Unknown",
                    budget.Month,
                    budget.Year,
                    budget.Amount,
                    actual,
                    utilization,
                    utilization >= 100m,
                    utilization >= budget.AlertThresholdPercent);
            })
            .OrderByDescending(x => x.UtilizationPercent)
            .ToList();
    }

    public IReadOnlyList<FinanceTransaction> GetTransactions(Guid userId, TransactionQuery query)
    {
        using var db = dbFactory.CreateDbContext();
        IQueryable<FinanceTransaction> filtered = db.Transactions.Where(x => x.UserId == userId);

        if (query.StartDate.HasValue)
        {
            filtered = filtered.Where(x => x.Date >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            filtered = filtered.Where(x => x.Date <= query.EndDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Type) && TryParseEntryType(query.Type, out var type))
        {
            filtered = filtered.Where(x => x.Type == type);
        }

        if (query.AccountId.HasValue)
        {
            filtered = filtered.Where(x => x.AccountId == query.AccountId || x.DestinationAccountId == query.AccountId);
        }

        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(x => x.CategoryId == query.CategoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var needle = query.Search.Trim();
            filtered = filtered.Where(x =>
                x.Merchant.Contains(needle) ||
                x.Note.Contains(needle));
        }

        return filtered
            .AsNoTracking()
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Select(CloneTransaction)
            .ToList();
    }

    public OperationResult<FinanceTransaction> CreateTransaction(Guid userId, CreateTransactionRequest payload)
    {
        using var db = dbFactory.CreateDbContext();
        var validation = ValidateAndNormalizeTransactionPayload(db, userId, payload.AccountId, payload.DestinationAccountId, payload.CategoryId, payload.Type, payload.Amount);
        if (!validation.Success)
        {
            return OperationResult<FinanceTransaction>.Fail(validation.Error!);
        }

        var tx = new FinanceTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = payload.AccountId,
            DestinationAccountId = payload.DestinationAccountId,
            CategoryId = payload.CategoryId,
            Type = validation.Payload!,
            Amount = payload.Amount,
            Date = payload.Date,
            Merchant = payload.Merchant?.Trim() ?? string.Empty,
            Note = payload.Note?.Trim() ?? string.Empty,
            PaymentMethod = payload.PaymentMethod?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        using var dbTransaction = db.Database.BeginTransaction();
        ApplyTransactionImpact(db, userId, tx, +1);
        db.Transactions.Add(tx);
        db.SaveChanges();
        dbTransaction.Commit();
        return OperationResult<FinanceTransaction>.Ok(CloneTransaction(tx));
    }

    public OperationResult<FinanceTransaction> UpdateTransaction(Guid userId, Guid txId, UpdateTransactionRequest payload)
    {
        using var db = dbFactory.CreateDbContext();
        var existing = db.Transactions.FirstOrDefault(x => x.UserId == userId && x.Id == txId);
        if (existing is null)
        {
            return OperationResult<FinanceTransaction>.Fail("Transaction not found.");
        }

        var validation = ValidateAndNormalizeTransactionPayload(db, userId, payload.AccountId, payload.DestinationAccountId, payload.CategoryId, payload.Type, payload.Amount);
        if (!validation.Success)
        {
            return OperationResult<FinanceTransaction>.Fail(validation.Error!);
        }

        using var dbTransaction = db.Database.BeginTransaction();
        ApplyTransactionImpact(db, userId, existing, -1);

        existing.AccountId = payload.AccountId;
        existing.DestinationAccountId = payload.DestinationAccountId;
        existing.CategoryId = payload.CategoryId;
        existing.Type = validation.Payload!;
        existing.Amount = payload.Amount;
        existing.Date = payload.Date;
        existing.Merchant = payload.Merchant?.Trim() ?? string.Empty;
        existing.Note = payload.Note?.Trim() ?? string.Empty;
        existing.PaymentMethod = payload.PaymentMethod?.Trim() ?? string.Empty;
        existing.UpdatedAt = DateTime.UtcNow;

        ApplyTransactionImpact(db, userId, existing, +1);
        db.SaveChanges();
        dbTransaction.Commit();
        return OperationResult<FinanceTransaction>.Ok(CloneTransaction(existing));
    }

    public OperationResult<bool> DeleteTransaction(Guid userId, Guid txId)
    {
        using var db = dbFactory.CreateDbContext();
        var existing = db.Transactions.FirstOrDefault(x => x.UserId == userId && x.Id == txId);
        if (existing is null)
        {
            return OperationResult<bool>.Fail("Transaction not found.");
        }

        using var dbTransaction = db.Database.BeginTransaction();
        ApplyTransactionImpact(db, userId, existing, -1);
        db.Transactions.Remove(existing);
        db.SaveChanges();
        dbTransaction.Commit();
        return OperationResult<bool>.Ok(true);
    }

    public DashboardSummaryResponse GetDashboardSummary(Guid userId, int month, int year)
    {
        using var db = dbFactory.CreateDbContext();
        var categoryMap = db.Categories
            .Where(x => x.UserId == userId)
            .AsNoTracking()
            .ToDictionary(x => x.Id, x => x.Name);

        var transactions = db.Transactions
            .Where(x => x.UserId == userId)
            .AsNoTracking()
            .ToList();

        var inMonth = transactions.Where(x => x.Date.Month == month && x.Date.Year == year).ToList();
        var income = inMonth.Where(x => x.Type == EntryType.Income).Sum(x => x.Amount);
        var expense = inMonth.Where(x => x.Type == EntryType.Expense).Sum(x => x.Amount);

        var categorySpend = inMonth
            .Where(x => x.Type == EntryType.Expense && x.CategoryId.HasValue)
            .GroupBy(x => x.CategoryId!.Value)
            .Select(group =>
            {
                var name = categoryMap.TryGetValue(group.Key, out var value) ? value : "Other";
                return new CategorySpendItem(name, group.Sum(x => x.Amount));
            })
            .OrderByDescending(x => x.Amount)
            .Take(6)
            .ToList();

        var recent = transactions
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Take(8)
            .Select(CloneTransaction)
            .ToList();

        return new DashboardSummaryResponse(income, expense, income - expense, categorySpend, recent);
    }

    private OperationResult<EntryType> ValidateAndNormalizeTransactionPayload(
        AppDbContext db,
        Guid userId,
        Guid accountId,
        Guid? destinationAccountId,
        Guid? categoryId,
        string type,
        decimal amount)
    {
        if (amount <= 0)
        {
            return OperationResult<EntryType>.Fail("Amount must be greater than 0.");
        }

        if (!db.Users.Any(x => x.Id == userId))
        {
            return OperationResult<EntryType>.Fail("User not found.");
        }

        var sourceExists = db.Accounts.Any(x => x.UserId == userId && x.Id == accountId);
        if (!sourceExists)
        {
            return OperationResult<EntryType>.Fail("Source account not found.");
        }

        if (!TryParseEntryType(type, out var entryType))
        {
            return OperationResult<EntryType>.Fail("Type must be income, expense, or transfer.");
        }

        if (entryType == EntryType.Transfer)
        {
            if (!destinationAccountId.HasValue)
            {
                return OperationResult<EntryType>.Fail("Transfer requires destination account.");
            }

            if (destinationAccountId.Value == accountId)
            {
                return OperationResult<EntryType>.Fail("Source and destination account must differ.");
            }

            var destinationExists = db.Accounts.Any(x => x.UserId == userId && x.Id == destinationAccountId.Value);
            if (!destinationExists)
            {
                return OperationResult<EntryType>.Fail("Destination account not found.");
            }
        }
        else
        {
            if (!categoryId.HasValue)
            {
                return OperationResult<EntryType>.Fail("Category is required for income and expense.");
            }

            var categoryExists = db.Categories.Any(x => x.UserId == userId && x.Id == categoryId.Value && !x.IsArchived);
            if (!categoryExists)
            {
                return OperationResult<EntryType>.Fail("Selected category not found.");
            }
        }

        return OperationResult<EntryType>.Ok(entryType);
    }

    private static void ApplyTransactionImpact(AppDbContext db, Guid userId, FinanceTransaction transaction, int direction)
    {
        var accounts = db.Accounts.Where(x => x.UserId == userId).ToList();
        var source = accounts.First(x => x.Id == transaction.AccountId);

        switch (transaction.Type)
        {
            case EntryType.Income:
                source.CurrentBalance += direction * transaction.Amount;
                source.LastUpdatedAt = DateTime.UtcNow;
                break;
            case EntryType.Expense:
                source.CurrentBalance -= direction * transaction.Amount;
                source.LastUpdatedAt = DateTime.UtcNow;
                break;
            case EntryType.Transfer:
                if (!transaction.DestinationAccountId.HasValue)
                {
                    throw new InvalidOperationException("Transfer missing destination account.");
                }

                var destination = accounts.First(x => x.Id == transaction.DestinationAccountId.Value);
                source.CurrentBalance -= direction * transaction.Amount;
                destination.CurrentBalance += direction * transaction.Amount;
                source.LastUpdatedAt = DateTime.UtcNow;
                destination.LastUpdatedAt = DateTime.UtcNow;
                break;
        }
    }

    private static bool TryParseEntryType(string input, out EntryType type) =>
        Enum.TryParse(input.Trim(), true, out type);

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string savedHash)
    {
        var parts = savedHash.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static bool IsStrongPassword(string password)
    {
        if (password.Length < 8)
        {
            return false;
        }

        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        return hasUpper && hasLower && hasDigit;
    }

    private static string CreateRefreshTokenValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(64));

    private static List<Category> BuildDefaultCategories(Guid userId)
    {
        var items = new List<(string Name, string Type)>
        {
            ("Food", "expense"),
            ("Rent", "expense"),
            ("Utilities", "expense"),
            ("Transport", "expense"),
            ("Entertainment", "expense"),
            ("Shopping", "expense"),
            ("Health", "expense"),
            ("Education", "expense"),
            ("Travel", "expense"),
            ("Subscriptions", "expense"),
            ("Miscellaneous", "expense"),
            ("Salary", "income"),
            ("Freelance", "income"),
            ("Bonus", "income"),
            ("Gift", "income"),
            ("Refund", "income"),
            ("Other", "income")
        };

        return items.Select(item => new Category
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = item.Name,
            Type = item.Type,
            Color = item.Type == "income" ? "#0EA5A8" : "#2F4F4F",
            Icon = "circle",
            IsArchived = false
        }).ToList();
    }

    private static User CloneUser(User user) =>
        new()
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            PasswordHash = user.PasswordHash,
            CreatedAt = user.CreatedAt
        };

    private static Account CloneAccount(Account account) =>
        new()
        {
            Id = account.Id,
            UserId = account.UserId,
            Name = account.Name,
            Type = account.Type,
            OpeningBalance = account.OpeningBalance,
            CurrentBalance = account.CurrentBalance,
            InstitutionName = account.InstitutionName,
            LastUpdatedAt = account.LastUpdatedAt
        };

    private static Category CloneCategory(Category category) =>
        new()
        {
            Id = category.Id,
            UserId = category.UserId,
            Name = category.Name,
            Type = category.Type,
            Color = category.Color,
            Icon = category.Icon,
            IsArchived = category.IsArchived
        };

    private static FinanceTransaction CloneTransaction(FinanceTransaction tx) =>
        new()
        {
            Id = tx.Id,
            UserId = tx.UserId,
            AccountId = tx.AccountId,
            DestinationAccountId = tx.DestinationAccountId,
            CategoryId = tx.CategoryId,
            Type = tx.Type,
            Amount = tx.Amount,
            Date = tx.Date,
            Merchant = tx.Merchant,
            Note = tx.Note,
            PaymentMethod = tx.PaymentMethod,
            CreatedAt = tx.CreatedAt,
            UpdatedAt = tx.UpdatedAt
        };

    private static Budget CloneBudget(Budget budget) =>
        new()
        {
            Id = budget.Id,
            UserId = budget.UserId,
            CategoryId = budget.CategoryId,
            Month = budget.Month,
            Year = budget.Year,
            Amount = budget.Amount,
            AlertThresholdPercent = budget.AlertThresholdPercent,
            CreatedAt = budget.CreatedAt,
            UpdatedAt = budget.UpdatedAt
        };
}
