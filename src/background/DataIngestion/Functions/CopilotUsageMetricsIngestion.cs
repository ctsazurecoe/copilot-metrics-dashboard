using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.CopilotDashboard.DataIngestion.Models;
using Microsoft.CopilotDashboard.DataIngestion.Services;

namespace Microsoft.CopilotDashboard.DataIngestion.Functions;

/// <summary>
/// Azure Function for ingesting Copilot usage metrics from the new GitHub Usage Metrics API.
/// This function runs daily to fetch usage metrics reports and store them in Cosmos DB.
/// 
/// The new API provides:
/// - Organization-level aggregated metrics
/// - Enterprise-level aggregated metrics
/// - User-level detailed metrics (optional)
/// 
/// Reports are available from October 10, 2025, with up to 1 year of historical data.
/// </summary>
public class CopilotUsageMetricsIngestion
{
    private readonly ILogger<CopilotUsageMetricsIngestion> _logger;
    private readonly GitHubCopilotUsageMetricsClient _usageMetricsClient;
    private readonly IOptions<GithubMetricsApiOptions> _options;

    public CopilotUsageMetricsIngestion(
        ILogger<CopilotUsageMetricsIngestion> logger,
        GitHubCopilotUsageMetricsClient usageMetricsClient,
        IOptions<GithubMetricsApiOptions> options)
    {
        _logger = logger;
        _usageMetricsClient = usageMetricsClient;
        _options = options;
    }

    /// <summary>
    /// Timer-triggered function that runs daily at 6 AM UTC to fetch usage metrics.
    /// Reports are generated daily by GitHub, so we fetch the previous day's data.
    /// </summary>
    [Function("GitHubCopilotUsageMetricsIngestion")]
    [CosmosDBOutput(
        databaseName: "platform-engineering",
        containerName: "usage_metrics_history",
        Connection = "AZURE_COSMOSDB_ENDPOINT",
        CreateIfNotExists = true)]
    public async Task<List<UsageMetricsData>> Run([TimerTrigger("0 0 6 * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation("GitHubCopilotUsageMetricsIngestion started at: {Time}", DateTime.UtcNow);

        var allMetrics = new List<UsageMetricsData>();

        try
        {
            // Fetch data for yesterday (reports need time to be generated)
            var targetDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            _logger.LogInformation("Fetching usage metrics for date: {Date}", targetDay);

            var scope = Environment.GetEnvironmentVariable("GITHUB_API_SCOPE");

            if (!string.IsNullOrWhiteSpace(scope) && scope.Equals("enterprise", StringComparison.OrdinalIgnoreCase))
            {
                var enterprise = Environment.GetEnvironmentVariable("GITHUB_ENTERPRISE");
                if (!string.IsNullOrWhiteSpace(enterprise))
                {
                    _logger.LogInformation("Fetching enterprise usage metrics for: {Enterprise}", enterprise);
                    var enterpriseMetrics = await FetchEnterpriseUsageMetricsAsync(enterprise, targetDay);
                    allMetrics.AddRange(enterpriseMetrics);
                }
            }
            else
            {
                var organization = Environment.GetEnvironmentVariable("GITHUB_ORGANIZATION");
                if (!string.IsNullOrWhiteSpace(organization))
                {
                    _logger.LogInformation("Fetching organization usage metrics for: {Organization}", organization);
                    var orgMetrics = await FetchOrganizationUsageMetricsAsync(organization, targetDay);
                    allMetrics.AddRange(orgMetrics);
                }
            }

            _logger.LogInformation("Total usage metrics records to store: {Count}", allMetrics.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during usage metrics ingestion");
        }

        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation("Next usage metrics ingestion scheduled at: {Next}", myTimer.ScheduleStatus.Next);
        }

        return allMetrics;
    }

    /// <summary>
    /// Manual trigger function to backfill historical usage metrics data.
    /// Can be called via HTTP to fetch metrics for a specific date range.
    /// </summary>
    [Function("GitHubCopilotUsageMetricsBackfill")]
    [CosmosDBOutput(
        databaseName: "platform-engineering",
        containerName: "usage_metrics_history",
        Connection = "AZURE_COSMOSDB_ENDPOINT",
        CreateIfNotExists = true)]
    public async Task<List<UsageMetricsData>> RunBackfill(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "usage-metrics/backfill")] HttpRequestData req)
    {
        _logger.LogInformation("Usage metrics backfill triggered");

        var allMetrics = new List<UsageMetricsData>();

        try
        {
            // Parse the request body for date range
            var requestBody = await req.ReadAsStringAsync();
            var backfillRequest = System.Text.Json.JsonSerializer.Deserialize<BackfillRequest>(requestBody ?? "{}");

            var startDate = backfillRequest?.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            var endDate = backfillRequest?.EndDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

            _logger.LogInformation("Backfilling usage metrics from {Start} to {End}", startDate, endDate);

            var scope = Environment.GetEnvironmentVariable("GITHUB_API_SCOPE");
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                _logger.LogInformation("Backfilling data for: {Date}", currentDate);

                if (!string.IsNullOrWhiteSpace(scope) && scope.Equals("enterprise", StringComparison.OrdinalIgnoreCase))
                {
                    var enterprise = Environment.GetEnvironmentVariable("GITHUB_ENTERPRISE");
                    if (!string.IsNullOrWhiteSpace(enterprise))
                    {
                        var metrics = await FetchEnterpriseUsageMetricsAsync(enterprise, currentDate);
                        allMetrics.AddRange(metrics);
                    }
                }
                else
                {
                    var organization = Environment.GetEnvironmentVariable("GITHUB_ORGANIZATION");
                    if (!string.IsNullOrWhiteSpace(organization))
                    {
                        var metrics = await FetchOrganizationUsageMetricsAsync(organization, currentDate);
                        allMetrics.AddRange(metrics);
                    }
                }

                currentDate = currentDate.AddDays(1);

                // Add a small delay to avoid rate limiting
                await Task.Delay(500);
            }

            _logger.LogInformation("Backfill complete. Total records: {Count}", allMetrics.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during usage metrics backfill");
        }

        return allMetrics;
    }

    private async Task<List<UsageMetricsData>> FetchOrganizationUsageMetricsAsync(string organization, DateOnly day)
    {
        var metrics = new List<UsageMetricsData>();

        try
        {
            var reportResponse = await _usageMetricsClient.GetOrgUsageMetricsForDayAsync(organization, day);

            if (reportResponse?.DownloadLinks?.Length > 0)
            {
                var downloadedMetrics = await _usageMetricsClient.DownloadUsageMetricsDataAsync(
                    reportResponse.DownloadLinks,
                    organization: organization);

                metrics.AddRange(downloadedMetrics);
                _logger.LogInformation("Fetched {Count} organization usage metrics for {Org} on {Date}",
                    downloadedMetrics.Count, organization, day);
            }
            else
            {
                _logger.LogWarning("No usage metrics report available for {Org} on {Date}", organization, day);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching organization usage metrics for {Org} on {Date}", organization, day);
        }

        return metrics;
    }

    private async Task<List<UsageMetricsData>> FetchEnterpriseUsageMetricsAsync(string enterprise, DateOnly day)
    {
        var metrics = new List<UsageMetricsData>();

        try
        {
            var reportResponse = await _usageMetricsClient.GetEnterpriseUsageMetricsForDayAsync(enterprise, day);

            if (reportResponse?.DownloadLinks?.Length > 0)
            {
                var downloadedMetrics = await _usageMetricsClient.DownloadUsageMetricsDataAsync(
                    reportResponse.DownloadLinks,
                    enterprise: enterprise);

                metrics.AddRange(downloadedMetrics);
                _logger.LogInformation("Fetched {Count} enterprise usage metrics for {Enterprise} on {Date}",
                    downloadedMetrics.Count, enterprise, day);
            }
            else
            {
                _logger.LogWarning("No usage metrics report available for {Enterprise} on {Date}", enterprise, day);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching enterprise usage metrics for {Enterprise} on {Date}", enterprise, day);
        }

        return metrics;
    }
}

/// <summary>
/// Request model for the backfill endpoint.
/// </summary>
public class BackfillRequest
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
