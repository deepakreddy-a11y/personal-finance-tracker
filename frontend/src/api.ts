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

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5052/api';

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, options: RequestInit = {}, token?: string): Promise<T> {
  const headers = new Headers(options.headers ?? {});
  headers.set('Content-Type', 'application/json');
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${API_BASE}${path}`, { ...options, headers });
  if (!response.ok) {
    let message = 'Request failed';
    try {
      const body = (await response.json()) as { message?: string };
      if (body.message) {
        message = body.message;
      }
    } catch {
      // Keep fallback message.
    }
    throw new ApiError(message, response.status);
  }
  return (await response.json()) as T;
}

export const api = {
  register: (email: string, password: string, displayName: string) =>
    request<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, password, displayName }),
    }),

  login: (email: string, password: string) =>
    request<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  refresh: (refreshToken: string) =>
    request<AuthResponse>('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),

  getAccounts: (token: string) => request<Account[]>('/accounts', {}, token),

  createAccount: (token: string, payload: { name: string; type: string; openingBalance: number; institutionName?: string }) =>
    request<Account>('/accounts', { method: 'POST', body: JSON.stringify(payload) }, token),

  getBudgets: (token: string, month: number, year: number) =>
    request<Budget[]>(`/budgets?month=${month}&year=${year}`, {}, token),

  createBudget: (
    token: string,
    payload: { categoryId: string; month: number; year: number; amount: number; alertThresholdPercent?: number },
  ) => request<Budget>('/budgets', { method: 'POST', body: JSON.stringify(payload) }, token),

  getBudgetProgress: (token: string, month: number, year: number) =>
    request<BudgetProgressItem[]>(`/budgets/progress?month=${month}&year=${year}`, {}, token),

  getCategories: (token: string) => request<Category[]>('/categories', {}, token),

  getTransactions: (token: string) => request<FinanceTransaction[]>('/transactions', {}, token),

  createTransaction: (
    token: string,
    payload: {
      accountId: string;
      destinationAccountId?: string | null;
      categoryId?: string | null;
      type: EntryType;
      amount: number;
      date: string;
      merchant?: string;
      note?: string;
      paymentMethod?: string;
    },
  ) => request<FinanceTransaction>('/transactions', { method: 'POST', body: JSON.stringify(payload) }, token),

  deleteTransaction: (token: string, id: string) =>
    request<{ deleted: boolean; id: string }>(`/transactions/${id}`, { method: 'DELETE' }, token),

  getDashboardSummary: (token: string, month: number, year: number) =>
    request<DashboardSummary>(`/dashboard/summary?month=${month}&year=${year}`, {}, token),
};
