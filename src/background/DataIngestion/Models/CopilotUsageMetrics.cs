using System.Text.Json.Serialization;

namespace Microsoft.CopilotDashboard.DataIngestion.Models;

/// <summary>
/// Response from the GitHub Copilot Usage Metrics API endpoints.
/// Contains download links to report files and date range information.
/// </summary>
public class UsageMetricsReportResponse
{
    [JsonPropertyName("download_links")]
    public string[] DownloadLinks { get; set; } = [];

    /// <summary>
    /// The date of the report (for single-day reports).
    /// Format: YYYY-MM-DD
    /// </summary>
    [JsonPropertyName("report_day")]
    public string? ReportDay { get; set; }

    /// <summary>
    /// The start date of the report (for multi-day reports like 28-day).
    /// Format: YYYY-MM-DD
    /// </summary>
    [JsonPropertyName("report_start_day")]
    public string? ReportStartDay { get; set; }

    /// <summary>
    /// The end date of the report (for multi-day reports like 28-day).
    /// Format: YYYY-MM-DD
    /// </summary>
    [JsonPropertyName("report_end_day")]
    public string? ReportEndDay { get; set; }
}

/// <summary>
/// Usage metrics data downloaded from the report files.
/// This represents the actual metrics data contained in the downloaded reports.
/// </summary>
public class UsageMetricsData
{
    [JsonPropertyName("id")]
    public string Id => GetId();

    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("total_active_users")]
    public int TotalActiveUsers { get; set; }

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("copilot_ide_code_completions")]
    public UsageIdeCodeCompletions? CopilotIdeCodeCompletions { get; set; }

    [JsonPropertyName("copilot_ide_chat")]
    public UsageIdeChat? CopilotIdeChat { get; set; }

    [JsonPropertyName("copilot_dotcom_chat")]
    public UsageDotComChat? CopilotDotcomChat { get; set; }

    [JsonPropertyName("copilot_dotcom_pull_requests")]
    public UsageDotComPullRequests? CopilotDotcomPullRequests { get; set; }

    [JsonPropertyName("enterprise")]
    public string? Enterprise { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("report_type")]
    public string ReportType { get; set; } = "usage_metrics";

    [JsonPropertyName("last_update")]
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    private string GetId()
    {
        var prefix = ReportType == "user_usage_metrics" ? "USR" : "ORG";
        if (!string.IsNullOrWhiteSpace(Organization))
        {
            return $"{Date:yyyy-MM-dd}-{prefix}-{Organization}{(string.IsNullOrWhiteSpace(Team) ? "" : $"-{Team}")}";
        }
        else if (!string.IsNullOrWhiteSpace(Enterprise))
        {
            return $"{Date:yyyy-MM-dd}-{prefix}-ENT-{Enterprise}{(string.IsNullOrWhiteSpace(Team) ? "" : $"-{Team}")}";
        }
        return $"{Date:yyyy-MM-dd}-{prefix}-XXX";
    }
}

public class UsageIdeCodeCompletions
{
    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("languages")]
    public UsageLanguage[]? Languages { get; set; }

    [JsonPropertyName("editors")]
    public UsageEditor[]? Editors { get; set; }
}

public class UsageLanguage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("total_code_suggestions")]
    public int TotalCodeSuggestions { get; set; }

    [JsonPropertyName("total_code_acceptances")]
    public int TotalCodeAcceptances { get; set; }

    [JsonPropertyName("total_code_lines_suggested")]
    public int TotalCodeLinesSuggested { get; set; }

    [JsonPropertyName("total_code_lines_accepted")]
    public int TotalCodeLinesAccepted { get; set; }
}

public class UsageEditor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("models")]
    public UsageModel[]? Models { get; set; }
}

public class UsageModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_custom_model")]
    public bool IsCustomModel { get; set; }

    [JsonPropertyName("custom_model_training_date")]
    public DateOnly? CustomModelTrainingDate { get; set; }

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("languages")]
    public UsageLanguage[]? Languages { get; set; }

    [JsonPropertyName("total_chats")]
    public int? TotalChats { get; set; }

    [JsonPropertyName("total_chat_insertion_events")]
    public int? TotalChatInsertionEvents { get; set; }

    [JsonPropertyName("total_chat_copy_events")]
    public int? TotalChatCopyEvents { get; set; }

    [JsonPropertyName("total_pr_summaries_created")]
    public int? TotalPrSummariesCreated { get; set; }
}

public class UsageIdeChat
{
    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("editors")]
    public UsageChatEditor[]? Editors { get; set; }
}

public class UsageChatEditor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("models")]
    public UsageModel[]? Models { get; set; }
}

public class UsageDotComChat
{
    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("models")]
    public UsageModel[]? Models { get; set; }
}

public class UsageDotComPullRequests
{
    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("repositories")]
    public UsageRepository[]? Repositories { get; set; }
}

public class UsageRepository
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("total_engaged_users")]
    public int TotalEngagedUsers { get; set; }

    [JsonPropertyName("models")]
    public UsageModel[]? Models { get; set; }
}

/// <summary>
/// User-level usage metrics data for granular user engagement analysis.
/// </summary>
public class UserUsageMetricsData
{
    [JsonPropertyName("id")]
    public string Id => GetId();

    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("user_login")]
    public string? UserLogin { get; set; }

    [JsonPropertyName("user_id")]
    public long? UserId { get; set; }

    [JsonPropertyName("total_active_days")]
    public int TotalActiveDays { get; set; }

    [JsonPropertyName("total_engaged_days")]
    public int TotalEngagedDays { get; set; }

    [JsonPropertyName("copilot_ide_code_completions")]
    public UsageIdeCodeCompletions? CopilotIdeCodeCompletions { get; set; }

    [JsonPropertyName("copilot_ide_chat")]
    public UsageIdeChat? CopilotIdeChat { get; set; }

    [JsonPropertyName("copilot_dotcom_chat")]
    public UsageDotComChat? CopilotDotcomChat { get; set; }

    [JsonPropertyName("copilot_dotcom_pull_requests")]
    public UsageDotComPullRequests? CopilotDotcomPullRequests { get; set; }

    [JsonPropertyName("enterprise")]
    public string? Enterprise { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("report_type")]
    public string ReportType { get; set; } = "user_usage_metrics";

    [JsonPropertyName("last_update")]
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    private string GetId()
    {
        var userPart = UserLogin ?? UserId?.ToString() ?? "unknown";
        if (!string.IsNullOrWhiteSpace(Organization))
        {
            return $"{Date:yyyy-MM-dd}-USR-{Organization}-{userPart}";
        }
        else if (!string.IsNullOrWhiteSpace(Enterprise))
        {
            return $"{Date:yyyy-MM-dd}-USR-ENT-{Enterprise}-{userPart}";
        }
        return $"{Date:yyyy-MM-dd}-USR-XXX-{userPart}";
    }
}
