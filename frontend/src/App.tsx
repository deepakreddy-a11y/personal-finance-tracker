import { useEffect, useMemo, useState } from 'react';
import { ApiError, api } from './api';
import type {
  Account,
  AuthResponse,
  Budget,
  BudgetProgressItem,
  Category,
  DashboardSummary,
  EntryType,
  FinanceTransaction,
} from './types';

const today = new Date().toISOString().slice(0, 10);
const SESSION_STORAGE_KEY = 'finance_session';

type Session = {
  accessToken: string;
  refreshToken: string;
  displayName: string;
  email: string;
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
};

function getSavedSession(): Session | null {
  const raw = localStorage.getItem(SESSION_STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Session;
    return parsed.accessToken && parsed.refreshToken ? parsed : null;
  } catch {
    return null;
  }
}

function mapAuthToSession(auth: AuthResponse): Session {
  return {
    accessToken: auth.accessToken,
    refreshToken: auth.refreshToken,
    displayName: auth.displayName,
    email: auth.email,
    accessTokenExpiresAtUtc: auth.accessTokenExpiresAtUtc,
    refreshTokenExpiresAtUtc: auth.refreshTokenExpiresAtUtc,
  };
}

export default function App() {
  const [session, setSession] = useState<Session | null>(getSavedSession());
  const [mode, setMode] = useState<'login' | 'register'>('register');
  const [authEmail, setAuthEmail] = useState('');
  const [authPassword, setAuthPassword] = useState('');
  const [authName, setAuthName] = useState('');
  const [authError, setAuthError] = useState('');
  const [isAuthLoading, setIsAuthLoading] = useState(false);

  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [budgets, setBudgets] = useState<Budget[]>([]);
  const [budgetProgress, setBudgetProgress] = useState<BudgetProgressItem[]>([]);
  const [transactions, setTransactions] = useState<FinanceTransaction[]>([]);
  const [dataError, setDataError] = useState('');
  const [isDataLoading, setIsDataLoading] = useState(false);

  const [accountName, setAccountName] = useState('');
  const [accountType, setAccountType] = useState('bank');
  const [accountOpeningBalance, setAccountOpeningBalance] = useState('0');
  const [budgetCategoryId, setBudgetCategoryId] = useState('');
  const [budgetAmount, setBudgetAmount] = useState('');
  const [budgetMonth, setBudgetMonth] = useState(new Date().getMonth() + 1);
  const [budgetYear, setBudgetYear] = useState(new Date().getFullYear());
  const [budgetViewMonth, setBudgetViewMonth] = useState(new Date().getMonth() + 1);
  const [budgetViewYear, setBudgetViewYear] = useState(new Date().getFullYear());
  const [budgetViewMonthInput, setBudgetViewMonthInput] = useState(new Date().getMonth() + 1);
  const [budgetViewYearInput, setBudgetViewYearInput] = useState(new Date().getFullYear());
  const [budgetFilterStatus, setBudgetFilterStatus] = useState('');

  const [txType, setTxType] = useState<EntryType>('expense');
  const [txAmount, setTxAmount] = useState('');
  const [txDate, setTxDate] = useState(today);
  const [txAccountId, setTxAccountId] = useState('');
  const [txDestinationId, setTxDestinationId] = useState('');
  const [txCategoryId, setTxCategoryId] = useState('');
  const [txMerchant, setTxMerchant] = useState('');
  const [txNote, setTxNote] = useState('');

  const expenseCategories = useMemo(() => categories.filter((x) => x.type === 'expense'), [categories]);
  const incomeCategories = useMemo(() => categories.filter((x) => x.type === 'income'), [categories]);
  const categoryOptions = txType === 'income' ? incomeCategories : expenseCategories;

  useEffect(() => {
    if (!session?.accessToken) {
      return;
    }
    void loadData();
  }, [session?.accessToken]);

  useEffect(() => {
    if (!txAccountId && accounts[0]) {
      setTxAccountId(accounts[0].id);
    }
    if (!txDestinationId && accounts[1]) {
      setTxDestinationId(accounts[1].id);
    }
  }, [accounts, txAccountId, txDestinationId]);

  useEffect(() => {
    if (!txCategoryId && categoryOptions[0]) {
      setTxCategoryId(categoryOptions[0].id);
    }
  }, [categoryOptions, txCategoryId]);

  useEffect(() => {
    const defaultBudgetCategory = expenseCategories[0];
    if (!budgetCategoryId && defaultBudgetCategory) {
      setBudgetCategoryId(defaultBudgetCategory.id);
    }
  }, [expenseCategories, budgetCategoryId]);

  const handleAuth = async (event: React.FormEvent) => {
    event.preventDefault();
    setAuthError('');
    setIsAuthLoading(true);

    try {
      const response =
        mode === 'register'
          ? await api.register(authEmail, authPassword, authName)
          : await api.login(authEmail, authPassword);

      const nextSession = mapAuthToSession(response);
      localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(nextSession));
      setSession(nextSession);
      setAuthPassword('');
      setAuthName('');
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Authentication failed.');
    } finally {
      setIsAuthLoading(false);
    }
  };

  const clearSession = () => {
    localStorage.removeItem(SESSION_STORAGE_KEY);
    setSession(null);
    setSummary(null);
    setAccounts([]);
    setCategories([]);
    setBudgets([]);
    setBudgetProgress([]);
    setTransactions([]);
  };

  const runWithAuth = async <T,>(operation: (accessToken: string) => Promise<T>): Promise<T> => {
    if (!session?.accessToken) {
      throw new Error('Session is not available.');
    }

    try {
      return await operation(session.accessToken);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && session.refreshToken) {
        try {
          const refreshed = await api.refresh(session.refreshToken);
          const refreshedSession = mapAuthToSession(refreshed);
          localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(refreshedSession));
          setSession(refreshedSession);
          return await operation(refreshedSession.accessToken);
        } catch {
          clearSession();
          throw new Error('Session expired. Please sign in again.');
        }
      }
      throw error;
    }
  };

  const loadData = async (targetBudgetMonth = budgetViewMonth, targetBudgetYear = budgetViewYear) => {
    setIsDataLoading(true);
    setDataError('');
    try {
      const now = new Date();
      const [accountsData, categoriesData, budgetsData, budgetProgressData, transactionsData, summaryData] = await runWithAuth((accessToken) =>
        Promise.all([
          api.getAccounts(accessToken),
          api.getCategories(accessToken),
          api.getBudgets(accessToken, targetBudgetMonth, targetBudgetYear),
          api.getBudgetProgress(accessToken, targetBudgetMonth, targetBudgetYear),
          api.getTransactions(accessToken),
          api.getDashboardSummary(accessToken, now.getMonth() + 1, now.getFullYear()),
        ]),
      );
      setAccounts(accountsData);
      setCategories(categoriesData);
      setBudgets(budgetsData);
      setBudgetProgress(budgetProgressData);
      setTransactions(transactionsData);
      setSummary(summaryData);
      return true;
    } catch (error) {
      setDataError(error instanceof Error ? error.message : 'Failed to load dashboard data.');
      return false;
    } finally {
      setIsDataLoading(false);
    }
  };

  const handleLogout = () => {
    clearSession();
  };

  const handleCreateAccount = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!session?.accessToken) return;

    setDataError('');
    try {
      await runWithAuth((accessToken) =>
        api.createAccount(accessToken, {
          name: accountName,
          type: accountType,
          openingBalance: Number(accountOpeningBalance),
        }),
      );
      setAccountName('');
      setAccountType('bank');
      setAccountOpeningBalance('0');
      await loadData();
    } catch (error) {
      setDataError(error instanceof Error ? error.message : 'Could not create account.');
    }
  };

  const handleCreateBudget = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!session?.accessToken) return;

    setDataError('');
    try {
      await runWithAuth((accessToken) =>
        api.createBudget(accessToken, {
          categoryId: budgetCategoryId,
          month: budgetMonth,
          year: budgetYear,
          amount: Number(budgetAmount),
          alertThresholdPercent: 80,
        }),
      );
      setBudgetAmount('');
      setBudgetViewMonth(budgetMonth);
      setBudgetViewYear(budgetYear);
      setBudgetViewMonthInput(budgetMonth);
      setBudgetViewYearInput(budgetYear);
      const ok = await loadData(budgetMonth, budgetYear);
      if (ok) {
        setBudgetFilterStatus(`Showing budgets for ${budgetMonth}/${budgetYear}.`);
      }
    } catch (error) {
      setDataError(error instanceof Error ? error.message : 'Could not save budget.');
    }
  };

  const handleApplyBudgetView = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!session?.accessToken) return;

    if (budgetViewMonthInput < 1 || budgetViewMonthInput > 12) {
      setDataError('View month must be between 1 and 12.');
      return;
    }

    if (budgetViewYearInput < 2000 || budgetViewYearInput > 3000) {
      setDataError('View year must be between 2000 and 3000.');
      return;
    }

    setDataError('');
    setBudgetFilterStatus(`Applying filter ${budgetViewMonthInput}/${budgetViewYearInput}...`);
    setBudgetViewMonth(budgetViewMonthInput);
    setBudgetViewYear(budgetViewYearInput);
    setBudgetMonth(budgetViewMonthInput);
    setBudgetYear(budgetViewYearInput);
    const ok = await loadData(budgetViewMonthInput, budgetViewYearInput);
    if (ok) {
      setBudgetFilterStatus(`Showing budgets for ${budgetViewMonthInput}/${budgetViewYearInput}.`);
    }
  };

  const handleCreateTransaction = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!session?.accessToken) return;

    setDataError('');
    try {
      await runWithAuth((accessToken) =>
        api.createTransaction(accessToken, {
          accountId: txAccountId,
          destinationAccountId: txType === 'transfer' ? txDestinationId : null,
          categoryId: txType === 'transfer' ? null : txCategoryId,
          type: txType,
          amount: Number(txAmount),
          date: txDate,
          merchant: txMerchant,
          note: txNote,
          paymentMethod: 'manual',
        }),
      );
      setTxAmount('');
      setTxMerchant('');
      setTxNote('');
      await loadData();
    } catch (error) {
      setDataError(error instanceof Error ? error.message : 'Could not save transaction.');
    }
  };

  const handleDeleteTransaction = async (id: string) => {
    if (!session?.accessToken) return;
    setDataError('');
    try {
      await runWithAuth((accessToken) => api.deleteTransaction(accessToken, id));
      await loadData();
    } catch (error) {
      setDataError(error instanceof Error ? error.message : 'Could not delete transaction.');
    }
  };

  const categoryNameById = useMemo(() => {
    return new Map(categories.map((x) => [x.id, x.name]));
  }, [categories]);

  const accountNameById = useMemo(() => {
    return new Map(accounts.map((x) => [x.id, x.name]));
  }, [accounts]);

  if (!session?.accessToken) {
    return (
      <main className="auth-shell">
        <section className="auth-card">
          <h1>Personal Finance Tracker</h1>
          <p className="lede">Hackathon starter with React + ASP.NET Core API.</p>
          <div className="auth-toggle">
            <button onClick={() => setMode('register')} className={mode === 'register' ? 'active' : ''}>
              Register
            </button>
            <button onClick={() => setMode('login')} className={mode === 'login' ? 'active' : ''}>
              Login
            </button>
          </div>
          <form onSubmit={handleAuth} className="form-grid">
            {mode === 'register' && (
              <label>
                Display Name
                <input value={authName} onChange={(e) => setAuthName(e.target.value)} required />
              </label>
            )}
            <label>
              Email
              <input type="email" value={authEmail} onChange={(e) => setAuthEmail(e.target.value)} required />
            </label>
            <label>
              Password
              <input type="password" value={authPassword} onChange={(e) => setAuthPassword(e.target.value)} required />
            </label>
            {authError && <p className="error-text">{authError}</p>}
            <button type="submit" className="primary" disabled={isAuthLoading}>
              {isAuthLoading ? 'Please wait...' : mode === 'register' ? 'Create Account' : 'Sign In'}
            </button>
          </form>
        </section>
      </main>
    );
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">Finance Tracker</p>
          <h1>Welcome, {session.displayName}</h1>
        </div>
        <button onClick={handleLogout}>Log out</button>
      </header>

      {dataError && <p className="error-text">{dataError}</p>}

      <section className="cards">
        <article className="card">
          <p>Month Income</p>
          <strong>₹ {summary?.monthIncome.toFixed(2) ?? '0.00'}</strong>
        </article>
        <article className="card">
          <p>Month Expense</p>
          <strong>₹ {summary?.monthExpense.toFixed(2) ?? '0.00'}</strong>
        </article>
        <article className="card">
          <p>Net Balance</p>
          <strong>₹ {summary?.netBalance.toFixed(2) ?? '0.00'}</strong>
        </article>
        <article className="card">
          <p>Accounts</p>
          <strong>{accounts.length}</strong>
        </article>
      </section>

      <section className="layout-grid">
        <article className="panel">
          <h2>Add Transaction</h2>
          <form onSubmit={handleCreateTransaction} className="form-grid">
            <label>
              Type
              <select value={txType} onChange={(e) => setTxType(e.target.value as EntryType)}>
                <option value="expense">Expense</option>
                <option value="income">Income</option>
                <option value="transfer">Transfer</option>
              </select>
            </label>
            <label>
              Amount
              <input type="number" min="0.01" step="0.01" value={txAmount} onChange={(e) => setTxAmount(e.target.value)} required />
            </label>
            <label>
              Date
              <input type="date" value={txDate} onChange={(e) => setTxDate(e.target.value)} required />
            </label>
            <label>
              Account
              <select value={txAccountId} onChange={(e) => setTxAccountId(e.target.value)} required>
                {accounts.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.name}
                  </option>
                ))}
              </select>
            </label>
            {txType === 'transfer' ? (
              <label>
                Destination
                <select value={txDestinationId} onChange={(e) => setTxDestinationId(e.target.value)} required>
                  {accounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.name}
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <label>
                Category
                <select value={txCategoryId} onChange={(e) => setTxCategoryId(e.target.value)} required>
                  {categoryOptions.map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <label>
              Merchant
              <input value={txMerchant} onChange={(e) => setTxMerchant(e.target.value)} />
            </label>
            <label>
              Note
              <input value={txNote} onChange={(e) => setTxNote(e.target.value)} />
            </label>
            <button type="submit" className="primary">
              Save Transaction
            </button>
          </form>
        </article>

        <article className="panel">
          <h2>Create Account</h2>
          <form onSubmit={handleCreateAccount} className="form-grid">
            <label>
              Name
              <input value={accountName} onChange={(e) => setAccountName(e.target.value)} required />
            </label>
            <label>
              Type
              <select value={accountType} onChange={(e) => setAccountType(e.target.value)}>
                <option value="bank">Bank</option>
                <option value="credit-card">Credit Card</option>
                <option value="cash">Cash</option>
                <option value="savings">Savings</option>
              </select>
            </label>
            <label>
              Opening Balance
              <input
                type="number"
                step="0.01"
                value={accountOpeningBalance}
                onChange={(e) => setAccountOpeningBalance(e.target.value)}
                required
              />
            </label>
            <button type="submit">Add Account</button>
          </form>

          <h3>Account Balances</h3>
          <ul className="clean-list">
            {accounts.map((account) => (
              <li key={account.id}>
                <span>{account.name}</span>
                <strong>₹ {account.currentBalance.toFixed(2)}</strong>
              </li>
            ))}
          </ul>
        </article>

        <article className="panel">
          <h2>Monthly Budgets</h2>
          <p className="eyebrow">
            {budgets.length} budget(s) configured for {budgetViewMonth}/{budgetViewYear}.
          </p>
          <form onSubmit={handleApplyBudgetView} className="form-grid">
            <label>
              View Month
              <input type="number" min="1" max="12" value={budgetViewMonthInput} onChange={(e) => setBudgetViewMonthInput(Number(e.target.value))} required />
            </label>
            <label>
              View Year
              <input type="number" min="2000" max="3000" value={budgetViewYearInput} onChange={(e) => setBudgetViewYearInput(Number(e.target.value))} required />
            </label>
            <button type="submit">Apply Filter</button>
          </form>
          {isDataLoading && <p className="filter-status">Loading budgets...</p>}
          {budgetFilterStatus && <p className="filter-status">{budgetFilterStatus}</p>}
          <h3>Create Budget</h3>
          <form onSubmit={handleCreateBudget} className="form-grid">
            <label>
              Category
              <select value={budgetCategoryId} onChange={(e) => setBudgetCategoryId(e.target.value)} required>
                {expenseCategories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Month
              <input type="number" min="1" max="12" value={budgetMonth} onChange={(e) => setBudgetMonth(Number(e.target.value))} required />
            </label>
            <label>
              Year
              <input type="number" min="2020" max="2100" value={budgetYear} onChange={(e) => setBudgetYear(Number(e.target.value))} required />
            </label>
            <label>
              Amount
              <input type="number" min="1" step="0.01" value={budgetAmount} onChange={(e) => setBudgetAmount(e.target.value)} required />
            </label>
            <button type="submit">Set Budget</button>
          </form>

          <h3>Budget Progress ({budgetViewMonth}/{budgetViewYear})</h3>
          <ul className="clean-list">
            {budgetProgress.length > 0 ? (
              budgetProgress.map((item) => (
                <li key={item.budgetId}>
                  <div>
                    <div>{item.categoryName}</div>
                    <small>
                      ₹ {item.actualExpense.toFixed(2)} / ₹ {item.budgetAmount.toFixed(2)} ({item.utilizationPercent.toFixed(0)}%)
                    </small>
                  </div>
                  <strong className={item.isOverBudget ? 'amt-negative' : item.isThresholdReached ? 'amt-positive' : ''}>
                    {item.isOverBudget ? 'Over' : item.isThresholdReached ? 'Alert' : 'OK'}
                  </strong>
                </li>
              ))
            ) : (
              <li>No budgets for this month yet.</li>
            )}
          </ul>
        </article>

        <article className="panel panel-wide">
          <h2>Recent Transactions</h2>
          {isDataLoading && <p>Loading data...</p>}
          {!isDataLoading && transactions.length === 0 && <p>No transactions yet.</p>}
          {!isDataLoading && transactions.length > 0 && (
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Type</th>
                  <th>Account</th>
                  <th>Category</th>
                  <th>Amount</th>
                  <th>Merchant</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {transactions.map((tx) => (
                  <tr key={tx.id}>
                    <td>{tx.date}</td>
                    <td>{tx.type}</td>
                    <td>{accountNameById.get(tx.accountId) ?? 'Unknown'}</td>
                    <td>{tx.categoryId ? categoryNameById.get(tx.categoryId) ?? 'Other' : '-'}</td>
                    <td className={tx.type === 'expense' ? 'amt-negative' : 'amt-positive'}>
                      {tx.type === 'expense' ? '-' : '+'}₹ {tx.amount.toFixed(2)}
                    </td>
                    <td>{tx.merchant || '-'}</td>
                    <td>
                      <button className="danger" onClick={() => handleDeleteTransaction(tx.id)}>
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </article>

        <article className="panel">
          <h2>Category Spend (This Month)</h2>
          <ul className="clean-list">
            {summary?.categorySpend.length ? (
              summary.categorySpend.map((item) => (
                <li key={item.category}>
                  <span>{item.category}</span>
                  <strong>₹ {item.amount.toFixed(2)}</strong>
                </li>
              ))
            ) : (
              <li>No spending data yet.</li>
            )}
          </ul>
        </article>
      </section>
    </main>
  );
}
