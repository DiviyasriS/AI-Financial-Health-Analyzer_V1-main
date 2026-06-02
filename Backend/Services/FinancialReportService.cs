using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Backend.Models.ML;

public class FinancialReportService : IReportService
{
    private readonly ITransactionService _transactionService;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRiskPredictionRepository _riskRepository;
    private readonly RiskPredictionService _riskPredictionService;
    private readonly InsightsService _insightsService;
    private readonly ILogger<FinancialReportService> _logger;

    public FinancialReportService(
        ITransactionService transactionService,
        ITransactionRepository transactionRepository,
        IRiskPredictionRepository riskRepository,
        IInsightRepository insightRepository,
        RiskPredictionService riskPredictionService,
        InsightsService insightsService,
        ILogger<FinancialReportService> logger)
    {
        _transactionService = transactionService;
        _transactionRepository = transactionRepository;
        _riskRepository = riskRepository;
        _riskPredictionService = riskPredictionService;
        _insightsService = insightsService;
        _logger = logger;
    }

    public async Task<byte[]> GenerateFinancialReportPdfAsync(int userId)
    {
        try
        {
            SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);
            List<Transaction> transactions = await _transactionRepository.GetByUserIdAsync(userId);

            if (transactions.Count == 0)
            {
                _logger.LogInformation("Empty PDF report generated for UserId={UserId}", userId);
                return BuildPdf(
                    summary,
                    null,
                    new RiskAssessmentResult
                    {
                        RiskLevel = "Unknown",
                        RiskScorePercent = 0,
                        Summary = "No transactions found. Upload a statement to generate a financial health report.",
                        RiskFactors = new List<string> { "No transaction data is available for analysis." },
                        PositiveSignals = new List<string>()
                    },
                    new List<Insight>(),
                    new List<Transaction>());
            }

            UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);
            RiskAssessmentResult assessment = RiskLabelGenerator.GenerateAssessment(features);
            (string riskLevel, float riskScore) = _riskPredictionService.Predict(features);

            RiskPrediction risk = new()
            {
                UserId = userId,
                RiskLevel = riskLevel,
                RiskScore = riskScore,
                MonthlyAvgSpend = summary.AverageMonthlySpend,
                TotalTransactions = summary.TotalTransactions,
                CategoryCount = summary.CategoryBreakdown.Count,
                TopCategory = features.TopCategory,
                TopCategoryPercentage = (decimal)features.TopCategoryPercentage,
                FoodSpendPercentage = (decimal)features.FoodSpendPercentage,
                EntertainmentSpendPercentage = (decimal)features.EntertainmentSpendPercentage,
                MoMSpendChangePercentage = (decimal)features.MoMSpendChangePercentage,
                PredictedAt = DateTime.UtcNow
            };

            await _riskRepository.SaveAsync(risk);

            List<Insight> insights = await _insightsService.GenerateAndSaveAsync(
                userId,
                features,
                summary,
                riskLevel);

            List<Transaction> topSpendingTransactions = transactions
                .Where(TransactionFilters.IsSpendingAnalytics)
                .OrderByDescending(t => Math.Abs(t.Amount))
                .Take(10)
                .ToList();

            byte[] pdfBytes = BuildPdf(summary, risk, assessment, insights, topSpendingTransactions);

            _logger.LogInformation(
                "PDF report generated for UserId={UserId}. RiskLevel={RiskLevel}, RiskScore={RiskScore:P1}, Transactions={TransactionCount}",
                userId,
                risk.RiskLevel,
                risk.RiskScore,
                transactions.Count);

            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PDF report for UserId={UserId}", userId);
            throw;
        }
    }

    private static byte[] BuildPdf(
        SpendingSummaryDto summary,
        RiskPrediction? risk,
        RiskAssessmentResult assessment,
        List<Insight> insights,
        List<Transaction> topSpendingTransactions)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("AI Financial Health Analyzer Report")
                        .FontSize(20)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);

                    col.Item().Text($"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Element(InfoBox).Text(
                        "Report definitions: Total Spent = all debit/outgoing transactions. Total Received = all credit/incoming transactions. Category analysis, risk score, insights, and top spending exclude credits and transfer/self-transfer transactions so the financial health story is not distorted.");

                    col.Item().Element(SectionTitle).Text("1. Executive Summary").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        AddMetric(table, "Total Spent", FormatCurrency(summary.TotalSpent));
                        AddMetric(table, "Total Received", FormatCurrency(summary.TotalReceived));
                        AddMetric(table, "Transaction Volume", FormatCurrency(summary.TotalTransactionVolume));
                        AddMetric(table, "Total Transactions", summary.TotalTransactions.ToString());
                        AddMetric(table, "Average Expense", FormatCurrency(summary.AverageExpenseAmount));
                        AddMetric(table, "Avg Monthly Spend", FormatCurrency(summary.AverageMonthlySpend));
                    });

                    col.Item().Element(SectionTitle).Text("2. Risk Score and Explanation").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                        });

                        AddHeader(table, "Risk Level", "Risk Score", "Meaning");
                        table.Cell().Element(Cell).Text(risk?.RiskLevel ?? "Unknown").Bold();
                        table.Cell().Element(Cell).Text(risk is null ? "0%" : $"{Math.Clamp(risk.RiskScore, 0f, 1f) * 100:0}%").Bold();
                        table.Cell().Element(Cell).Text(assessment.Summary);
                    });

                    AddBullets(col, "Risk Factors", assessment.RiskFactors);
                    AddBullets(col, "Positive Signals", assessment.PositiveSignals);

                    col.Item().Element(SectionTitle).Text("3. Spending Category Breakdown").Bold();
                    if (summary.CategoryBreakdown.Count == 0)
                    {
                        col.Item().Text("No category spending data is available after excluding credits and transfers.");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            AddHeader(table, "Category", "Spent", "Txns", "% of Spending");

                            foreach (CategorySummaryDto item in summary.CategoryBreakdown.Take(12))
                            {
                                table.Cell().Element(Cell).Text(item.Category);
                                table.Cell().Element(Cell).Text(FormatCurrency(item.Total));
                                table.Cell().Element(Cell).Text(item.TransactionCount.ToString());
                                table.Cell().Element(Cell).Text($"{item.PercentageOfTotal:0.##}%");
                            }
                        });
                    }

                    col.Item().Element(SectionTitle).Text("4. Monthly Expense Breakdown").Bold();
                    if (summary.MonthlyBreakdown.Count == 0)
                    {
                        col.Item().Text("No debit transactions are available for monthly expense analysis.");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            AddHeader(table, "Month", "Total Spent", "Txns", "Change");

                            foreach (MonthlySummaryDto item in summary.MonthlyBreakdown)
                            {
                                table.Cell().Element(Cell).Text(item.MonthName);
                                table.Cell().Element(Cell).Text(FormatCurrency(item.Total));
                                table.Cell().Element(Cell).Text(item.TransactionCount.ToString());
                                table.Cell().Element(Cell).Text(FormatChange(item.ChangeFromPreviousMonth, item.PercentageChangeFromPreviousMonth));
                            }
                        });
                    }

                    col.Item().Element(SectionTitle).Text("5. AI Insights").Bold();
                    if (insights.Count == 0)
                    {
                        col.Item().Text("No insights available yet.");
                    }
                    else
                    {
                        foreach (Insight insight in insights.OrderByDescending(i => i.Priority).Take(8))
                        {
                            col.Item().Element(InsightBox).Column(box =>
                            {
                                box.Item().Text($"{insight.Title}  ·  Priority {insight.Priority}").Bold();
                                box.Item().Text(insight.Message);
                            });
                        }
                    }

                    col.Item().Element(SectionTitle).Text("6. Top Spending Transactions").Bold();
                    if (topSpendingTransactions.Count == 0)
                    {
                        col.Item().Text("No spending transactions found after excluding credits and transfers.");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(5);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });

                            AddHeader(table, "Date", "Description", "Category", "Amount");

                            foreach (Transaction tx in topSpendingTransactions)
                            {
                                table.Cell().Element(Cell).Text(tx.Date.ToString("dd MMM yyyy"));
                                table.Cell().Element(Cell).Text(tx.Description);
                                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(tx.Category) ? "Uncategorized" : tx.Category);
                                table.Cell().Element(Cell).Text(FormatCurrency(Math.Abs(tx.Amount)));
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void AddBullets(ColumnDescriptor col, string title, List<string> items)
    {
        if (items.Count == 0)
            return;

        col.Item().Text(title).Bold();
        foreach (string item in items.Take(5))
        {
            col.Item().PaddingLeft(8).Text($"• {item}");
        }
    }

    private static IContainer SectionTitle(IContainer container)
    {
        return container
            .PaddingTop(4)
            .PaddingBottom(4)
            .BorderBottom(1)
            .BorderColor(Colors.Blue.Lighten2);
    }

    private static IContainer Cell(IContainer container)
    {
        return container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten3)
            .PaddingVertical(4)
            .PaddingHorizontal(3);
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(5)
            .PaddingHorizontal(3);
    }

    private static IContainer MetricCell(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(6);
    }

    private static IContainer InfoBox(IContainer container)
    {
        return container
            .Background(Colors.Blue.Lighten5)
            .Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Padding(8);
    }

    private static IContainer InsightBox(IContainer container)
    {
        return container
            .Background(Colors.Grey.Lighten5)
            .BorderLeft(3)
            .BorderColor(Colors.Blue.Darken2)
            .Padding(7);
    }

    private static void AddHeader(TableDescriptor table, params string[] headers)
    {
        foreach (string header in headers)
        {
            table.Cell().Element(HeaderCell).Text(header).Bold();
        }
    }

    private static void AddMetric(TableDescriptor table, string key, string value)
    {
        table.Cell().Element(MetricCell).Column(col =>
        {
            col.Item().Text(key).FontColor(Colors.Grey.Darken2);
            col.Item().Text(value).FontSize(12).Bold();
        });
    }

    private static string FormatCurrency(decimal value)
    {
        return $"₹{Math.Abs(value):N2}";
    }

    private static string FormatChange(decimal? value, decimal? percentage)
    {
        if (value is null)
            return "—";

        string amountPrefix = value > 0 ? "+" : value < 0 ? "-" : string.Empty;
        string amountText = $"{amountPrefix}{FormatCurrency(Math.Abs(value.Value))}";

        if (percentage is null)
            return amountText;

        string pctPrefix = percentage > 0 ? "+" : string.Empty;
        return $"{amountText} ({pctPrefix}{percentage:0.#}%)";
    }
}
