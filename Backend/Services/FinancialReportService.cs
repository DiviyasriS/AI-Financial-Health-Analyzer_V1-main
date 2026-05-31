using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Backend.Models.ML;

public class FinancialReportService : IReportService
{
    private readonly ITransactionService _transactionService;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRiskPredictionRepository _riskRepository;
    private readonly IInsightRepository _insightRepository;
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
        _insightRepository = insightRepository;
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

            RiskPrediction? risk = await _riskRepository.GetLatestByUserIdAsync(userId);

            if (risk is null && transactions.Count > 0)
            {
                UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);
                (string riskLevel, float riskScore) = _riskPredictionService.Predict(features);

                risk = new RiskPrediction
                {
                    UserId = userId,
                    RiskLevel = riskLevel,
                    RiskScore = riskScore,
                    MonthlyAvgSpend = summary.AverageMonthlySpend,
                    TotalTransactions = summary.TotalTransactions,
                    CategoryCount = summary.CategoryBreakdown.Count,
                    PredictedAt = DateTime.UtcNow
                };

                await _riskRepository.SaveAsync(risk);
            }

            List<Insight> insights = await _insightRepository.GetByUserIdAsync(userId);

            if (insights.Count == 0 && transactions.Count > 0)
            {
                UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);

                insights = await _insightsService.GenerateAndSaveAsync(
                    userId,
                    features,
                    summary,
                    risk?.RiskLevel ?? "Low");
            }

            List<Transaction> topTransactions = transactions
                .Where(t => !t.IsCredit)
                .OrderByDescending(t => Math.Abs(t.Amount))
                .Take(10)
                .ToList();

            byte[] pdfBytes = BuildPdf(summary, risk, insights, topTransactions);

            _logger.LogInformation("PDF report generated for UserId={UserId}", userId);
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
        List<Insight> insights,
        List<Transaction> topTransactions)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("AI Financial Health Analyzer Report")
                        .FontSize(20)
                        .Bold();

                    col.Item().Text($"Generated Date: {DateTime.Now:dd MMM yyyy hh:mm tt}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Element(SectionTitle).Text("Summary").Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        AddKeyValue(table, "Total Spent", FormatCurrency(summary.TotalSpent));
                        AddKeyValue(table, "Total Received", FormatCurrency(summary.TotalReceived));
                        AddKeyValue(table, "Total Transaction Volume", FormatCurrency(summary.TotalTransactionVolume));
                        AddKeyValue(table, "Total Transactions", summary.TotalTransactions.ToString());
                        AddKeyValue(table, "Average Expense Transaction", FormatCurrency(summary.AverageTransactionAmount));
                        AddKeyValue(table, "Average Monthly Spend", FormatCurrency(summary.AverageMonthlySpend));
                        AddKeyValue(table, "Highest Spending Category", summary.HighestSpendingCategory);
                    });

                    col.Item().Element(SectionTitle).Text("Risk Score").Bold();

                    col.Item().Text(risk is null
                        ? "Risk Score: Not available"
                        : $"Risk Level: {risk.RiskLevel} | Risk Score: {(risk.RiskScore * 100):0}% | Predicted At: {risk.PredictedAt:dd MMM yyyy}");

                    col.Item().Element(SectionTitle).Text("Category Breakdown").Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        AddHeader(
                            table,
                            "Category",
                            "Total Spent",
                            "Transactions",
                            "Percentage"
                        );

                        foreach (CategorySummaryDto item in summary.CategoryBreakdown)
                        {
                            table.Cell().Element(Cell).Text(item.Category);
                            table.Cell().Element(Cell).Text(FormatCurrency(item.Total));
                            table.Cell().Element(Cell).Text(item.TransactionCount.ToString());
                            table.Cell().Element(Cell).Text($"{item.PercentageOfTotal:0.##}%");
                        }
                    });

                    col.Item().Element(SectionTitle).Text("Monthly Breakdown").Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        AddHeader(
                            table,
                            "Month",
                            "Total Spent",
                            "Transactions"
                        );

                        foreach (MonthlySummaryDto item in summary.MonthlyBreakdown)
                        {
                            table.Cell().Element(Cell).Text(item.MonthName);
                            table.Cell().Element(Cell).Text(FormatCurrency(item.Total));
                            table.Cell().Element(Cell).Text(item.TransactionCount.ToString());
                        }
                    });

                    col.Item().Element(SectionTitle).Text("AI Insights").Bold();

                    if (insights.Count == 0)
                    {
                        col.Item().Text("No insights available.");
                    }
                    else
                    {
                        foreach (Insight insight in insights.OrderBy(i => i.Priority))
                        {
                            col.Item().Text($"{insight.Title}: {insight.Message}");
                        }
                    }

                    col.Item().Element(SectionTitle).Text("Top Transactions").Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(3);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        AddHeader(table, "Date", "Description", "Category", "Amount");

                        foreach (Transaction tx in topTransactions)
                        {
                            table.Cell().Element(Cell).Text(tx.Date.ToString("dd MMM yyyy"));
                            table.Cell().Element(Cell).Text(tx.Description);
                            table.Cell().Element(Cell).Text(tx.Category);
                            table.Cell().Element(Cell).Text(FormatCurrency(Math.Abs(tx.Amount)));
                        }
                    });
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

    private static IContainer SectionTitle(IContainer container)
    {
        return container
            .PaddingTop(5)
            .PaddingBottom(4)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten1);
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

    private static void AddHeader(TableDescriptor table, params string[] headers)
    {
        foreach (string header in headers)
        {
            table.Cell().Element(HeaderCell).Text(header).Bold();
        }
    }

    private static void AddKeyValue(TableDescriptor table, string key, string value)
    {
        table.Cell().Element(Cell).Text(key).Bold();
        table.Cell().Element(Cell).Text(value);
    }

    private static string FormatCurrency(decimal value)
    {
        return $"₹{Math.Abs(value):N2}";
    }
}