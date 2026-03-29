# Personal Finance Tracker - Hackathon Build Plan

## Project Goal
Build a full-stack personal finance tracker that supports authentication, accounts, transaction tracking, budgets, goals, recurring payments, and reports with a responsive web UI.

## Chosen Stack
- Frontend: React + TypeScript + Vite
- Backend: ASP.NET Core Minimal API (.NET 10)
- Database (target): PostgreSQL
- Current state: EF Core + PostgreSQL local persistence with DB safety guard

## Hackathon Constraints (From Channel Guidance)
- Use `amiti.in` Codex credentials for hackathon work.
- Do not mix OPENLANE and Amiti Codex sessions; switch accounts based on workstream.
- Check in code to Amiti GitHub (invitations were sent by organizers).
- Deployment expectation is Azure.
- Podman is recommended for reusable cloud deployment workflow; not compulsory in early local development.
- Azure deployment instructions shared by organizer:
  - `https://docs.google.com/document/d/1bRP7KQl0sV4NJllfx77zEVqsHzcdUkaLWbMlWIa086E/edit?usp=sharing`

## Delivery Phases

## Phase 0 - Foundation and Environment (Completed)
- Extract and analyze product specification.
- Scaffold backend and frontend projects.
- Wire frontend-to-backend API communication.
- Implement local-only workflow and project structure.

Deliverables:
- `backend/FinanceTracker.Api`
- `frontend`
- Working app shell and API health path

## Phase 1 - Core Vertical Slice (In Progress)
- Authentication: register/login with password strength checks.
- Accounts: create and list accounts.
- Transactions: create/list/update/delete transactions.
- Dashboard summary: income, expense, net balance, category spend, recent items.
- Responsive starter UI with:
  - auth flow
  - add account
  - add transaction
  - account balances
  - recent transactions table

Acceptance checks:
- User can create account and sign in.
- User can add a transaction and see balances/summary update.
- Data visible in dashboard after refresh when session token remains.
- JWT access tokens and refresh-token rotation are active.

## Phase 2 - PostgreSQL Persistence (In Progress)
- Replace in-memory store with PostgreSQL. (Done)
- Add EF Core entities and DB context. (Done)
- Seed default categories on user registration. (Done)
- Add EF Core migrations baseline and schema versioning. (Done)
- Create repository/services layer boundaries. (Pending)

Acceptance checks:
- Data persists across backend restarts.
- User-scoped queries enforced for all entities.

## Phase 3 - Budgets and Alerts
- Budgets CRUD by month/category.
- Budget vs actual progress bars.
- Threshold alerts (80%, 100%, 120%).

Acceptance checks:
- Budget overrun appears in dashboard and budget screen.

## Phase 4 - Goals and Contributions
- Goals CRUD with target tracking and status updates.
- Contribution and withdraw flows.
- Optional account-linked goal constraints.

Acceptance checks:
- Goal progress updates immediately after contribution/withdrawal.

## Phase 5 - Recurring Transactions
- Recurring item CRUD.
- Next due calculation by frequency.
- Scheduled transaction generation job.

Acceptance checks:
- Upcoming recurring payments list is accurate.

## Phase 6 - Reports and Export
- Category breakdown and income-vs-expense trends.
- Date/account/category filters.
- CSV export endpoint and UI.

Acceptance checks:
- Filtered report values match transaction dataset.

## Phase 7 - Hardening and Demo Readiness
- Validation and error handling pass.
- Security basics: JWT, refresh tokens, rate limiting.
- Accessibility and responsive polish.
- Smoke tests and final demo script.

Acceptance checks:
- End-to-end demo path completes without manual fixes.

## Phase 8 - Deployment and Submission
- Containerize backend/frontend using Podman-compatible setup.
- Provision Azure resources and deploy app + PostgreSQL.
- Configure production environment variables and CORS.
- Run post-deploy smoke checks (login, CRUD, budget summary).
- Submit deployed URL, repo link, and progress form artifacts.

Acceptance checks:
- Deployed app is reachable and login works.
- Leaderboard criteria met for deployed app + verified repo.

## Current Next Steps
- Complete remaining V1 modules: Goals, Recurring, Reports/Export.
- Prepare Podman-friendly deployment assets and start Azure deployment.
- Push code to Amiti GitHub and connect Azure deployment pipelines.
