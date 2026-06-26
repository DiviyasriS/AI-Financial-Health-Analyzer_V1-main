# Data Flow

This document traces the complete, end-to-end data flow through the application, from user login through statement upload, parsing, validation, duplicate detection, database storage, feature extraction, ML risk prediction, insights generation, the dashboard, email alerts, and report generation. Every step described below is backed by a corresponding class/method in the codebase, referenced inline.

## 1. End-to-End Overview

```mermaid
flowchart TD
    A[User Login] --> B[Upload Statement]
    B --> C[Parsing]
    C --> D[Validation]
    D --> E[Duplicate Detection]
    E --> F[Database Storage]
    F --> G[Feature Extraction]
    G --> H[ML Risk Prediction]
    H --> I[Insights Generation]
    I --> J[Dashboard]
    H --> K[Email Alert]
    J --> L[Report Generation]
```

## 2. User Login

Three login methods all converge on the same JWT issuance step.

```mermaid
sequenceDiagram
    participant U as User (Angular)
    participant LC as LoginComponent
    participant AS as AuthService (Angular)
    participant API as AuthController
    participant SVC as AuthService (Backend)
    participant DB as MySQL (Users)

    U->>LC: Enter email + password
    LC->>AS: login(dto)
    AS->>API: POST /api/auth/login
    API->>SVC: LoginAsync(dto)
    SVC->>DB: GetByEmailAsync(normalizedEmail)
    DB-->>SVC: User row (or null)
    SVC->>SVC: BCrypt.Verify(password, hash)
    SVC->>SVC: GenerateJwtToken(user)
    SVC-->>API: AuthResponseDto { token }
    API-->>AS: 200 OK / ApiResponse<AuthResponseDto>
    AS->>AS: localStorage.setItem('auth_token', token)
    AS-->>LC: success
    LC->>LC: navigate to /upload or /dashboard
```

For Google Sign-In, the same flow applies except `GoogleLoginAsync` validates the credential via `GoogleJsonWebSignature.ValidateAsync` and links/creates the user via `AuthProviders`. For mobile OTP, `SendMobileOtpAsync` generates and stores a hashed OTP, and `VerifyMobileOtpAsync` validates it before issuing the same JWT.

Once a token exists, every subsequent request from the Angular app passes through `authInterceptor`, which attaches `Authorization: Bearer <token>`. The backend's `[Authorize]` filter and JWT bearer middleware validate the token before any controller action runs; `authGuard` on the frontend additionally blocks navigation to `/upload`, `/dashboard`, `/profile` without a token.

## 3. Upload Statement → Parsing → Validation → Duplicate Detection → Database Storage

```mermaid
sequenceDiagram
    participant U as User
    participant UC as UploadComponent
    participant TS as TransactionService (Angular)
    participant API as TransactionController
    participant SVC as TransactionService (Backend)
    participant PARSER as CsvService / XlsxService / PdfService
    participant CAT as CategoryPredictionService
    participant REPO as TransactionRepository
    participant DB as MySQL (Transactions)
    participant RISK as Risk + Alert pipeline

    U->>UC: Select file (CSV/XLSX/XLS/PDF)
    UC->>TS: uploadFile(file)
    TS->>API: POST /api/transaction/upload (multipart/form-data)
    API->>API: Validate: file present, <=10MB, allowed extension
    alt validation fails
        API-->>TS: 400 Bad Request
    else validation passes
        API->>SVC: ProcessAndSaveAsync(stream, fileName, userId)
        SVC->>PARSER: ParseAsync(stream, userId) [by extension]
        PARSER->>CAT: Predict(description) [if category blank]
        PARSER-->>SVC: ParsedFileResult { Transactions, TotalRowsFound, SkippedRows }
        SVC->>REPO: GetByUserIdAndDateRangeAsync(userId, minDate, maxDate)
        REPO->>DB: SELECT existing transactions in range
        DB-->>REPO: existing rows
        SVC->>SVC: Build composite keys (date|description|amount), filter duplicates
        SVC->>REPO: AddRangeAsync(nonDuplicates)
        REPO->>DB: INSERT new transactions
        SVC-->>API: FileProcessingResultDto
        API->>RISK: TryGenerateRiskAndSendAlertAsync(userId) [best-effort, see Section 6]
        API-->>TS: 200 OK / FileProcessingResultDto
    end
```

### 3.1 Parsing

Parsing is dispatched by file extension in `TransactionService.ProcessAndSaveAsync`:

| Extension | Parser | Library |
|---|---|---|
| `.csv` | `CsvService.ParseAsync` | Hand-written RFC-4180-aware splitter |
| `.xlsx` / `.xls` | `XlsxService.ParseAsync` | EPPlus |
| `.pdf` | `PdfService.ParseAsync` | PdfPig (Paytm statement format only) |

Each parser independently determines transaction direction (credit/debit) using the same three-tier strategy (CSV/XLSX) or the embedded `+`/`-` sign in the statement text (PDF):

```mermaid
flowchart TD
    Start[Row parsed] --> T1{Explicit Type column present and recognized?}
    T1 -- Yes --> UseType[Use type column value]
    T1 -- No --> T2{Any negative amount detected in file?}
    T2 -- Yes --> Signed[Negative = Debit, Positive = Credit]
    T2 -- No --> Unsigned[Default: IsCredit = false / Debit]
```

If a category is blank or `"Uncategorized"`, `CategoryPredictionService.Predict(description)` assigns one of: `Food`, `Transport`, `Shopping`, `Utilities`, `Income`, `Entertainment`, `UPI`, or `Others` based on keyword matching. Credits left as `Uncategorized`/`Others` after prediction are reclassified as `Income`.

### 3.2 Validation

Performed by `TransactionController.UploadFile` before any parsing occurs:

1. File must be present and non-empty (`400` otherwise).
2. File size must be ≤ 10 MB (`400` otherwise).
3. File extension must be one of `.csv`, `.xlsx`, `.xls`, `.pdf` (`400` otherwise).

Row-level validation happens inside each parser: rows with fewer than 3 columns, unparsable dates, unparsable/zero amounts, or empty descriptions are skipped and counted in `SkippedRows`.

### 3.3 Duplicate Detection

Performed in `TransactionService.ProcessAndSaveAsync`:

1. Compute `minDate`/`maxDate` spanning the newly parsed rows.
2. Fetch existing transactions for the user within that date range (`GetByUserIdAndDateRangeAsync`).
3. Build a normalized composite key for each existing and new row: `{date:yyyy-MM-dd}|{description, trimmed/lowercased/whitespace-collapsed}|{Math.Abs(amount) as InvariantCulture string}`.
4. Any new row whose key matches an existing key is counted as a duplicate and not persisted.
5. If all non-duplicate new rows fall within a single calendar month that already has existing transactions, a `MonthWarning` message is included in the response.

### 3.4 Database Storage

Non-duplicate `Transaction` entities are persisted via `TransactionRepository.AddRangeAsync`, which calls `AppDbContext.Transactions.AddRangeAsync` followed by `SaveChangesAsync`. The same `AppDbContext` (MySQL via Pomelo, or EF Core InMemory under `ASPNETCORE_ENVIRONMENT=Testing`) backs all transaction reads/writes.

## 4. Feature Extraction

Triggered whenever a risk assessment is needed (after upload, on `GET /api/dashboard/risk`, on `GET /api/dashboard/insights`, or during PDF report generation).

```mermaid
flowchart TD
    A[All transactions for user] --> B["TransactionFilters.IsSpendingAnalytics filter\n(excludes credits and self-transfers)"]
    B --> C[Group by Year/Month]
    C --> D[Compute monthly totals, avg, std dev]
    B --> E[Group by normalized category]
    E --> F[Top category %, category count]
    B --> G[Match Essential / Food / Entertainment keyword sets]
    G --> H[Composition percentages]
    D --> I[Month-over-month % change]
    D --> J[3-month linear trend, normalized]
    D & F & H & I & J --> K[UserRiskFeatures]
```

`FinancialFeatureExtractor.Extract(transactions)` is a pure, static function that produces the 11-feature `UserRiskFeatures` vector: `MonthlyAvgSpend`, `MonthlySpendStdDev`, `TransactionFrequency`, `LargeTransactionFrequency`, `TopCategoryPercentage`, `CategoryCount`, `EssentialSpendPercentage`, `FoodSpendPercentage`, `EntertainmentSpendPercentage`, `MoMSpendChangePercentage`, `SpendingTrend` — plus contextual fields (`TopCategory`, `TotalSpend`, `TotalTransactions`, `MonthCount`, `MonthlyTotals`) used for insight text, not fed into the ML model.

## 5. ML Risk Prediction

```mermaid
sequenceDiagram
    participant CALLER as Controller (Transaction/Dashboard/Reports)
    participant FE as FinancialFeatureExtractor
    participant RLG as RiskLabelGenerator
    participant RPS as RiskPredictionService
    participant MODEL as Trained ML.NET model

    CALLER->>FE: Extract(transactions)
    FE-->>CALLER: UserRiskFeatures
    CALLER->>RPS: Predict(features)
    RPS->>RLG: GenerateAssessment(features)
    RLG-->>RPS: RiskAssessmentResult (rule-based level, score%, factors, signals)
    alt model is loaded
        RPS->>MODEL: PredictionEngine.Predict(RiskInput)
        MODEL-->>RPS: RiskOutput { PredictedLabel, Score[] }
        RPS->>RPS: ReconcileRiskLevels(scorecardLevel, mlLevel)
    else model not loaded
        RPS->>RPS: Use scorecard level as-is
    end
    RPS-->>CALLER: (FinalRiskLevel, RiskScore 0.0-1.0)
```

Key rule: the ML model's predicted label is accepted only if it is **at most one severity rank** away from the rule-based scorecard's level (`RiskRank`: Low=1, Medium=2, High=3); otherwise the rule-based level wins. The numeric `RiskScore` returned to every API consumer is **always** the rule-based scorecard's severity percentage (0.0–1.0) — never the ML model's raw class probability.

The rule-based scorecard (`RiskLabelGenerator.GenerateAssessment`) accumulates points (0–12 max) across six weighted categories — spending magnitude, volatility (coefficient of variation), category concentration, discretionary spend (food/entertainment), large-transaction frequency, and month-over-month/3-month trend — then converts the total to a percentage with a 15% floor (for users with real spending data) and buckets it: `<35% = Low`, `35–70% = Medium`, `≥70% = High`. Users with zero transactions or zero total spend get `"Unknown"` with a 0% score.

The result is persisted as a new `RiskPrediction` row via `IRiskPredictionRepository.SaveAsync` — one row per prediction run (upload, dashboard risk check, or report generation), each carrying a snapshot of the feature values used (for auditability and future retraining), not an update to a single "current" row.

### 5.1 Background Model Training

```mermaid
sequenceDiagram
    participant HOST as ModelTrainingHostedService
    participant TRAINER as RiskModelTrainer
    participant REPO as TransactionRepository
    participant RPS as RiskPredictionService

    Note over HOST: On application startup
    HOST->>HOST: Check if Models/risk_model.zip exists
    alt model file exists
        HOST->>TRAINER: LoadSavedModel()
        TRAINER-->>HOST: ITransformer
        HOST->>RPS: SetModel(mlContext, model)
    end
    Note over HOST: Background task (fire-and-forget)
    HOST->>TRAINER: TrainAndSaveAsync()
    TRAINER->>REPO: GetAllUserIdsAsync()
    REPO-->>TRAINER: distinct user IDs
    loop for each user with >=3 transactions
        TRAINER->>TRAINER: FinancialFeatureExtractor.Extract + RiskLabelGenerator.GenerateLabel
    end
    TRAINER->>TRAINER: Add 36 fixed synthetic samples (12 each: Low/Medium/High)
    TRAINER->>TRAINER: 80/20 train/test split (seed 42)
    TRAINER->>TRAINER: Train SdcaMaximumEntropy, evaluate, save to risk_model.zip
    TRAINER-->>HOST: trained ITransformer
    HOST->>RPS: SetModel(mlContext, trainedModel) [hot-swap]
```

This hosted service is **not registered** when `ASPNETCORE_ENVIRONMENT=Testing`.

## 6. Insights Generation

```mermaid
flowchart TD
    A[Trigger: GET /api/dashboard/insights, or report generation] --> B[Load transactions, compute SpendingSummaryDto]
    B --> C[FinancialFeatureExtractor.Extract]
    C --> D[RiskPredictionService.Predict -> current RiskLevel]
    D --> E["InsightsService.GenerateAndSaveAsync(userId, features, summary, riskLevel)"]
    E --> F[BuildInsights: apply rule checks in priority order]
    F --> G[InsightRepository.DeleteByUserIdAsync - clear old insights]
    G --> H[InsightRepository.SaveRangeAsync - persist new insights]
    H --> I[Return List of Insight, mapped to InsightDto]
```

`InsightsService.BuildInsights` applies an ordered set of rule checks (overall risk level, food spending %, entertainment spending %, category concentration, month-over-month change, 3-month trend, spending volatility, large-transaction frequency, low category diversity, and a positive "healthy pattern" acknowledgement for Low-risk users with ≥3 categories), each producing a titled `Insight` with a `Priority` (1 = highest) and a `Type` of `"info"`, `"warning"`, or `"danger"`. Insights are fully **replaced** on every generation — old rows for the user are deleted before the new set is saved.

## 7. Dashboard

```mermaid
sequenceDiagram
    participant DC as DashboardComponent
    participant API as DashboardController

    Note over DC: On component init — forkJoin (parallel calls)
    par
        DC->>API: GET /api/dashboard/summary
        API-->>DC: DashboardSummaryDto
    and
        DC->>API: GET /api/dashboard/risk
        API-->>DC: RiskDto (or client falls back to "Unknown" placeholder on error)
    and
        DC->>API: GET /api/dashboard/insights
        API-->>DC: List<InsightDto> (or client falls back to empty list on error)
    end
    DC->>DC: ChartService renders: category donut, monthly bar, trend line (>=2 months), top-5 categories bar, risk gauge
```

The dashboard additionally surfaces: an empty state when there is no data, the highest spending category and biggest single expense, month-over-month change (currency + percentage, styled positive/negative), and a **Download PDF Report** button.

## 8. Email Alert

```mermaid
sequenceDiagram
    participant CALLER as TransactionController / DashboardController
    participant ALERT as AlertService
    participant UREPO as UserRepository
    participant SENDER as SmtpEmailSender (MailKit)

    CALLER->>ALERT: SendRiskAlertAsync(userId, riskDto)
    ALERT->>UREPO: GetByIdAsync(userId)
    UREPO-->>ALERT: User (or null)
    alt user is null or has no email
        ALERT->>ALERT: Log warning, return (no email sent)
    else user has email
        ALERT->>ALERT: Build subject + plain-text body (risk level, score, summary, factors, signals)
        ALERT->>SENDER: SendEmailAsync(toEmail, subject, body)
        SENDER->>SENDER: Connect (StartTLS) -> Authenticate -> Send -> Disconnect
    end
```

Triggers:

1. After every successful statement upload (`TransactionController.UploadFile` → `TryGenerateRiskAndSendAlertAsync`) — only if the user has at least one transaction.
2. Whenever `GET /api/dashboard/risk` is called (`DashboardController.GetRisk`).

In both cases the send is wrapped in a `try/catch`: any exception (missing email, SMTP failure, etc.) is logged via `ILogger` and swallowed — it never causes the triggering request (upload or risk check) to fail.

## 9. Report Generation

```mermaid
sequenceDiagram
    participant DC as DashboardComponent (Download button)
    participant API as ReportsController
    participant FRS as FinancialReportService
    participant REPO as TransactionRepository
    participant RPS as RiskPredictionService
    participant INS as InsightsService
    participant QPDF as QuestPDF

    DC->>API: GET /api/reports/financial/pdf
    API->>FRS: GenerateFinancialReportPdfAsync(userId)
    FRS->>REPO: GetByUserIdAsync(userId)
    REPO-->>FRS: transactions
    alt transactions.Count == 0
        FRS->>QPDF: BuildPdf(empty summary, null risk, "Unknown" assessment, [], [])
    else has transactions
        FRS->>FRS: FinancialFeatureExtractor.Extract + RiskLabelGenerator.GenerateAssessment
        FRS->>RPS: Predict(features)
        RPS-->>FRS: (riskLevel, riskScore)
        FRS->>FRS: Persist new RiskPrediction row
        FRS->>INS: GenerateAndSaveAsync(userId, features, summary, riskLevel)
        INS-->>FRS: List<Insight>
        FRS->>FRS: Select top 10 spending transactions (excl. credits/transfers)
        FRS->>QPDF: BuildPdf(summary, risk, assessment, insights, topTransactions)
    end
    QPDF-->>FRS: byte[] PDF
    FRS-->>API: byte[] PDF
    API-->>DC: 200 OK, application/pdf, financial-health-report-{timestamp}.pdf
    DC->>DC: Receive as Blob, trigger browser download
```

The generated PDF contains six sections: Executive Summary, Risk Score and Explanation, Spending Category Breakdown (top 12), Monthly Expense Breakdown, AI Insights (top 8 by the ordering applied in `FinancialReportService`), and Top Spending Transactions (top 10 by absolute amount). Generating a report is **not** a read-only operation — it persists a new `RiskPrediction` row and replaces the user's stored `Insight` rows, exactly as the dashboard `risk`/`insights` endpoints do.
