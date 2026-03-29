export type EntryType = 'income' | 'expense' | 'transfer';

export type AuthResponse = {
  userId: string;
  email: string;
  displayName: string;
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
};

export type Account = {
  id: string;
  userId: string;
  name: string;
  type: string;
  openingBalance: number;
  currentBalance: number;
  institutionName: string;
  lastUpdatedAt: string;
};

export type Category = {
  id: string;
  userId: string;
  name: string;
  type: 'income' | 'expense';
  color: string;
  icon: string;
  isArchived: boolean;
};

export type FinanceTransaction = {
  id: string;
  userId: string;
  accountId: string;
  destinationAccountId?: string | null;
  categoryId?: string | null;
  type: 'income' | 'expense' | 'transfer';
  amount: number;
  date: string;
  merchant: string;
  note: string;
  paymentMethod: string;
  createdAt: string;
  updatedAt: string;
};

export type CategorySpendItem = {
  category: string;
  amount: number;
};

export type Budget = {
  id: string;
  userId: string;
  categoryId: string;
  month: number;
  year: number;
  amount: number;
  alertThresholdPercent: number;
  createdAt: string;
  updatedAt: string;
};

export type BudgetProgressItem = {
  budgetId: string;
  categoryId: string;
  categoryName: string;
  month: number;
  year: number;
  budgetAmount: number;
  actualExpense: number;
  utilizationPercent: number;
  isOverBudget: boolean;
  isThresholdReached: boolean;
};

export type DashboardSummary = {
  monthIncome: number;
  monthExpense: number;
  netBalance: number;
  categorySpend: CategorySpendItem[];
  recentTransactions: FinanceTransaction[];
};
