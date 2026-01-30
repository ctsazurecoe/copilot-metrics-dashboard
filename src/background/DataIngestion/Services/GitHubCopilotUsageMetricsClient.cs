using System.Net.Http.Json;
using Microsoft.CopilotDashboard.DataIngestion.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.CopilotDashboard.DataIngestion.Services;

/// <summary>
/// Client for fetching Copilot usage metrics from the new GitHub Usage Metrics API.
/// This API returns download links to report files containing comprehensive usage data.
/// </summary>
public class GitHubCopilotUsageMetricsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotUsageMetricsClient> _logger;

    public GitHubCopilotUsageMetricsClient(HttpClient httpClient, ILogger<GitHubCopilotUsageMetricsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    #region Organization Endpoints

    /// <summary>
    /// Get organization usage metrics for a specific day.
    /// Endpoint: GET /orgs/{org}/copilot/metrics/reports/organization-1-day?day={day}
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetOrgUsageMetricsForDayAsync(string organization, DateOnly day)
    {
        var requestUri = $"/orgs/{organization}/copilot/metrics/reports/organization-1-day?day={day:yyyy-MM-dd}";
        return await GetReportResponseAsync(requestUri, $"org-usage-{organization}-{day}");
    }

    /// <summary>
    /// Get the latest 28-day organization usage metrics.
    /// Endpoint: GET /orgs/{org}/copilot/metrics/reports/organization-28-day/latest
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetOrgUsageMetricsLatest28DayAsync(string organization)
    {
        var requestUri = $"/orgs/{organization}/copilot/metrics/reports/organization-28-day/latest";
        return await GetReportResponseAsync(requestUri, $"org-usage-28day-{organization}");
    }

    /// <summary>
    /// Get organization user-level usage metrics for a specific day.
    /// Endpoint: GET /orgs/{org}/copilot/metrics/reports/users-1-day?day={day}
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetOrgUserUsageMetricsForDayAsync(string organization, DateOnly day)
    {
        var requestUri = $"/orgs/{organization}/copilot/metrics/reports/users-1-day?day={day:yyyy-MM-dd}";
        return await GetReportResponseAsync(requestUri, $"org-user-usage-{organization}-{day}");
    }

    /// <summary>
    /// Get the latest 28-day organization user-level usage metrics.
    /// Endpoint: GET /orgs/{org}/copilot/metrics/reports/users-28-day/latest
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetOrgUserUsageMetricsLatest28DayAsync(string organization)
    {
        var requestUri = $"/orgs/{organization}/copilot/metrics/reports/users-28-day/latest";
        return await GetReportResponseAsync(requestUri, $"org-user-usage-28day-{organization}");
    }

    #endregion

    #region Enterprise Endpoints

    /// <summary>
    /// Get enterprise usage metrics for a specific day.
    /// Endpoint: GET /enterprises/{enterprise}/copilot/metrics/reports/enterprise-1-day?day={day}
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetEnterpriseUsageMetricsForDayAsync(string enterprise, DateOnly day)
    {
        var requestUri = $"/enterprises/{enterprise}/copilot/metrics/reports/enterprise-1-day?day={day:yyyy-MM-dd}";
        return await GetReportResponseAsync(requestUri, $"ent-usage-{enterprise}-{day}");
    }

    /// <summary>
    /// Get the latest 28-day enterprise usage metrics.
    /// Endpoint: GET /enterprises/{enterprise}/copilot/metrics/reports/enterprise-28-day/latest
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetEnterpriseUsageMetricsLatest28DayAsync(string enterprise)
    {
        var requestUri = $"/enterprises/{enterprise}/copilot/metrics/reports/enterprise-28-day/latest";
        return await GetReportResponseAsync(requestUri, $"ent-usage-28day-{enterprise}");
    }

    /// <summary>
    /// Get enterprise user-level usage metrics for a specific day.
    /// Endpoint: GET /enterprises/{enterprise}/copilot/metrics/reports/users-1-day?day={day}
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetEnterpriseUserUsageMetricsForDayAsync(string enterprise, DateOnly day)
    {
        var requestUri = $"/enterprises/{enterprise}/copilot/metrics/reports/users-1-day?day={day:yyyy-MM-dd}";
        return await GetReportResponseAsync(requestUri, $"ent-user-usage-{enterprise}-{day}");
    }

    /// <summary>
    /// Get the latest 28-day enterprise user-level usage metrics.
    /// Endpoint: GET /enterprises/{enterprise}/copilot/metrics/reports/users-28-day/latest
    /// </summary>
    public async Task<UsageMetricsReportResponse?> GetEnterpriseUserUsageMetricsLatest28DayAsync(string enterprise)
    {
        var requestUri = $"/enterprises/{enterprise}/copilot/metrics/reports/users-28-day/latest";
        return await GetReportResponseAsync(requestUri, $"ent-user-usage-28day-{enterprise}");
    }

    #endregion

    #region Download Methods

    /// <summary>
    /// Downloads and parses the usage metrics data from the provided download links.
    /// The download links are signed URLs that don't require authentication.
    /// </summary>
    public async Task<List<UsageMetricsData>> DownloadUsageMetricsDataAsync(
        string[] downloadLinks,
        string? organization = null,
        string? enterprise = null)
    {
        var allData = new List<UsageMetricsData>();

        foreach (var link in downloadLinks)
        {
            try
            {
                _logger.LogInformation("Downloading usage metrics from: {Link}", link);

                // Use a separate HttpClient without auth headers for signed URLs
                using var downloadClient = new HttpClient();
                var response = await downloadClient.GetAsync(link);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download report from {Link}: {StatusCode}", link, response.StatusCode);
                    continue;
                }

                var data = await response.Content.ReadFromJsonAsync<UsageMetricsData[]>();
                if (data != null)
                {
                    // Enrich with org/enterprise info
                    foreach (var item in data)
                    {
                        item.Organization = organization;
                        item.Enterprise = enterprise;
                        item.ReportType = "usage_metrics";
                    }
                    allData.AddRange(data);
                }

                _logger.LogInformation("Downloaded {Count} usage metrics records from report", data?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading usage metrics from {Link}", link);
            }
        }

        return allData;
    }

    /// <summary>
    /// Downloads and parses the user-level usage metrics data from the provided download links.
    /// </summary>
    public async Task<List<UserUsageMetricsData>> DownloadUserUsageMetricsDataAsync(
        string[] downloadLinks,
        string? organization = null,
        string? enterprise = null)
    {
        var allData = new List<UserUsageMetricsData>();

        foreach (var link in downloadLinks)
        {
            try
            {
                _logger.LogInformation("Downloading user usage metrics from: {Link}", link);

                using var downloadClient = new HttpClient();
                var response = await downloadClient.GetAsync(link);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download user report from {Link}: {StatusCode}", link, response.StatusCode);
                    continue;
                }

                var data = await response.Content.ReadFromJsonAsync<UserUsageMetricsData[]>();
                if (data != null)
                {
                    foreach (var item in data)
                    {
                        item.Organization = organization;
                        item.Enterprise = enterprise;
                        item.ReportType = "user_usage_metrics";
                    }
                    allData.AddRange(data);
                }

                _logger.LogInformation("Downloaded {Count} user usage metrics records from report", data?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading user usage metrics from {Link}", link);
            }
        }

        return allData;
    }

    #endregion

    #region Private Methods

    private async Task<UsageMetricsReportResponse?> GetReportResponseAsync(string requestUri, string context)
    {
        try
        {
            _logger.LogInformation("Fetching usage metrics report: {Context}", context);
            var response = await _httpClient.GetAsync(requestUri);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Usage metrics report not found: {Context}. This may be expected for recent dates.", context);
                    return null;
                }
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("Access forbidden for usage metrics: {Context}. Check if the 'Copilot usage metrics' policy is enabled.", context);
                    return null;
                }
                
                _logger.LogError("Error fetching usage metrics {Context}: {StatusCode}", context, response.StatusCode);
                return null;
            }

            var reportResponse = await response.Content.ReadFromJsonAsync<UsageMetricsReportResponse>();
            _logger.LogInformation("Retrieved usage metrics report {Context} with {Count} download links", 
                context, reportResponse?.DownloadLinks?.Length ?? 0);

            return reportResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching usage metrics: {Context}", context);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching usage metrics: {Context}", context);
            return null;
        }
    }

    #endregion
}
