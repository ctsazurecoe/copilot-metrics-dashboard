import { formatResponseError, unknownResponseError } from "@/features/common/response-error";
import { CopilotMetrics, CopilotUsageOutput, UsageMetricsReportResponse, UsageMetricsData } from "@/features/common/models";
import { ServerActionResponse } from "@/features/common/server-action-response";
import { SqlQuerySpec } from "@azure/cosmos";
import { format } from "date-fns";
import { cosmosClient, cosmosConfiguration } from "./cosmos-db-service";
import { ensureGitHubEnvConfig } from "./env-service";
import { stringIsNullOrEmpty, applyTimeFrameLabel } from "../utils/helpers";
import { sampleData } from "./sample-data";

export interface IFilter {
  startDate?: Date;
  endDate?: Date;
  enterprise: string;
  organization: string;
  team: string[];
}

export const getCopilotMetrics = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();
  const isCosmosConfig = cosmosConfiguration();

  if (env.status !== "OK") {
    return env;
  }

  const { enterprise, organization } = env.response;

  try {
    switch (process.env.GITHUB_API_SCOPE) {
      case "enterprise":
        if (stringIsNullOrEmpty(filter.enterprise)) {
          filter.enterprise = enterprise;
        }
        break;
      default:
        if (stringIsNullOrEmpty(filter.organization)) {
          filter.organization = organization;
        }
        break;
    }    if (isCosmosConfig) {
      return getCopilotMetricsFromDatabase(filter);
    }
    
    // If teams are specified, use the teams-specific API function
    if (filter.team && filter.team.length > 0) {
      return getCopilotTeamsMetricsFromApi(filter);
    }
    
    return getCopilotMetricsFromApi(filter);
  } catch (e) {
    return unknownResponseError(e);
  }
};

const fetchCopilotMetrics = async (
  url: string,
  token: string,
  version: string,
  entityName: string
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const response = await fetch(url, {
    cache: "no-store",
    headers: {
      Accept: `application/vnd.github+json`,
      Authorization: `Bearer ${token}`,
      "X-GitHub-Api-Version": version,
    },
  });

  if (!response.ok) {
    return formatResponseError(entityName, response);
  }

  const data = await response.json();
  const dataWithTimeFrame = applyTimeFrameLabel(data);
  return {
    status: "OK",
    response: dataWithTimeFrame,
  };
};

export const getCopilotMetricsFromApi = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();

  if (env.status !== "OK") {
    return env;
  }
  
  if (filter.team && filter.team.length > 0) {
    return getCopilotTeamsMetricsFromApi(filter);
  }

  const { token, version } = env.response;

  try {
    const queryParams = new URLSearchParams();

    if (filter.startDate) {
      queryParams.append("since", format(filter.startDate, "yyyy-MM-dd"));
    }
    if (filter.endDate) {
      queryParams.append("until", format(filter.endDate, "yyyy-MM-dd"));
    }

    const queryString = queryParams.toString()
      ? `?${queryParams.toString()}`
      : "";

    if (filter.enterprise) {
      const url = `https://api.github.com/enterprises/${filter.enterprise}/copilot/metrics${queryString}`;
      return fetchCopilotMetrics(url, token, version, filter.enterprise);
    } else {
      const url = `https://api.github.com/orgs/${filter.organization}/copilot/metrics${queryString}`;
      return fetchCopilotMetrics(url, token, version, filter.organization);
    }
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Fetches Copilot metrics for specific teams from the GitHub API
 * @param filter - Filter containing team names and date range
 * @returns Promise with combined metrics for all specified teams
 */
export const getCopilotTeamsMetricsFromApi = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();

  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  try {
    // If no teams specified, return empty array
    if (!filter.team || filter.team.length === 0) {
      return {
        status: "OK",
        response: [],
      };
    }

    const queryParams = new URLSearchParams();

    if (filter.startDate) {
      queryParams.append("since", format(filter.startDate, "yyyy-MM-dd"));
    }
    if (filter.endDate) {
      queryParams.append("until", format(filter.endDate, "yyyy-MM-dd"));
    }

    const queryString = queryParams.toString()
      ? `?${queryParams.toString()}`
      : "";

    // Fetch metrics for each team and combine results
    const teamMetricsPromises = filter.team.map(async (teamSlug) => {
      let url: string;
      let entityName: string;

      if (filter.enterprise) {
        // For enterprise-level team metrics
        url = `https://api.github.com/enterprises/${filter.enterprise}/team/${teamSlug}/copilot/metrics${queryString}`;
        entityName = `${filter.enterprise}/team/${teamSlug}`;
      } else {
        // For organization-level team metrics
        url = `https://api.github.com/orgs/${filter.organization}/team/${teamSlug}/copilot/metrics${queryString}`;
        entityName = `${filter.organization}/team/${teamSlug}`;
      }

      return fetchCopilotMetrics(url, token, version, entityName);
    });

    const teamMetricsResults = await Promise.all(teamMetricsPromises);

    // Check if any requests failed
    const failedResults = teamMetricsResults.filter(result => result.status !== "OK");
    if (failedResults.length > 0) {
      // Return the first error encountered
      return failedResults[0];
    }

    // Combine all successful results
    const allMetrics: CopilotUsageOutput[] = [];
    teamMetricsResults.forEach(result => {
      if (result.status === "OK") {
        allMetrics.push(...result.response);
      }    });

    // Sort by day to maintain consistency
    allMetrics.sort((a, b) => new Date(a.day).getTime() - new Date(b.day).getTime());

    return {
      status: "OK",
      response: allMetrics,
    };
  } catch (e) {
    return unknownResponseError(e);
  }
};

export const getCopilotMetricsFromDatabase = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const client = cosmosClient();
  const database = client.database("platform-engineering");
  const container = database.container("metrics_history");

  let start = "";
  let end = "";
  const maxDays = 365 * 2; // maximum 2 years of data
  const maximumDays = 31;

  if (filter.startDate && filter.endDate) {
    start = format(filter.startDate, "yyyy-MM-dd");
    end = format(filter.endDate, "yyyy-MM-dd");
  } else {
    // set the start date to today and the end date to 31 days ago
    const todayDate = new Date();
    const startDate = new Date(todayDate);
    startDate.setDate(todayDate.getDate() - maximumDays);

    start = format(startDate, "yyyy-MM-dd");
    end = format(todayDate, "yyyy-MM-dd");
  }

  let querySpec: SqlQuerySpec = {
    query: `SELECT * FROM c WHERE c.date >= @start AND c.date <= @end`,
    parameters: [
      { name: "@start", value: start },
      { name: "@end", value: end },
    ],
  };

  if (filter.enterprise) {
    querySpec.query += ` AND c.enterprise = @enterprise`;
    querySpec.parameters?.push({
      name: "@enterprise",
      value: filter.enterprise,
    });
  }

  if (filter.organization) {
    querySpec.query += ` AND c.organization = @organization`;
    querySpec.parameters?.push({
      name: "@organization",
      value: filter.organization,
    });
  }
  if (filter.team && filter.team.length > 0) {
    if (filter.team.length === 1) {
      querySpec.query += ` AND c.team = @team`;
      querySpec.parameters?.push({ name: "@team", value: filter.team[0] });
    } else {
      const teamConditions = filter.team
        .map((_, index) => `c.team = @team${index}`)
        .join(" OR ");
      querySpec.query += ` AND (${teamConditions})`;
      filter.team.forEach((team, index) => {
        querySpec.parameters?.push({ name: `@team${index}`, value: team });
      });
    }
  }else {
    querySpec.query += ` AND c.team = null`;
  }

  const { resources } = await container.items
    .query<CopilotMetrics>(querySpec, {
      maxItemCount: maxDays,
    })
    .fetchAll();

  const dataWithTimeFrame = applyTimeFrameLabel(resources);
  return {
    status: "OK",
    response: dataWithTimeFrame,
  };
};

export const _getCopilotMetrics = (): Promise<CopilotUsageOutput[]> => {
  const promise = new Promise<CopilotUsageOutput[]>((resolve) => {
    setTimeout(() => {
      const weekly = applyTimeFrameLabel(sampleData);
      resolve(weekly);
    }, 1000);
  });

  return promise;
};

// ============================================
// New Usage Metrics API Functions (Oct 2025+)
// ============================================

/**
 * Fetch organization usage metrics for a specific day from the new Usage Metrics API.
 * This API returns download links to report files.
 */
export const getOrgUsageMetricsForDay = async (
  org: string,
  day: string
): Promise<ServerActionResponse<UsageMetricsReportResponse>> => {
  const env = ensureGitHubEnvConfig();
  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  const url = `https://api.github.com/orgs/${org}/copilot/metrics/reports/organization-1-day?day=${day}`;

  try {
    const response = await fetch(url, {
      cache: "no-store",
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${token}`,
        "X-GitHub-Api-Version": version,
      },
    });

    if (!response.ok) {
      return formatResponseError(org, response);
    }

    return { status: "OK", response: await response.json() };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Fetch the latest 28-day organization usage metrics from the new Usage Metrics API.
 */
export const getOrgUsageMetricsLatest28Day = async (
  org: string
): Promise<ServerActionResponse<UsageMetricsReportResponse>> => {
  const env = ensureGitHubEnvConfig();
  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  const url = `https://api.github.com/orgs/${org}/copilot/metrics/reports/organization-28-day/latest`;

  try {
    const response = await fetch(url, {
      cache: "no-store",
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${token}`,
        "X-GitHub-Api-Version": version,
      },
    });

    if (!response.ok) {
      return formatResponseError(org, response);
    }

    return { status: "OK", response: await response.json() };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Fetch enterprise usage metrics for a specific day from the new Usage Metrics API.
 */
export const getEnterpriseUsageMetricsForDay = async (
  enterprise: string,
  day: string
): Promise<ServerActionResponse<UsageMetricsReportResponse>> => {
  const env = ensureGitHubEnvConfig();
  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  const url = `https://api.github.com/enterprises/${enterprise}/copilot/metrics/reports/enterprise-1-day?day=${day}`;

  try {
    const response = await fetch(url, {
      cache: "no-store",
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${token}`,
        "X-GitHub-Api-Version": version,
      },
    });

    if (!response.ok) {
      return formatResponseError(enterprise, response);
    }

    return { status: "OK", response: await response.json() };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Fetch the latest 28-day enterprise usage metrics from the new Usage Metrics API.
 */
export const getEnterpriseUsageMetricsLatest28Day = async (
  enterprise: string
): Promise<ServerActionResponse<UsageMetricsReportResponse>> => {
  const env = ensureGitHubEnvConfig();
  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  const url = `https://api.github.com/enterprises/${enterprise}/copilot/metrics/reports/enterprise-28-day/latest`;

  try {
    const response = await fetch(url, {
      cache: "no-store",
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${token}`,
        "X-GitHub-Api-Version": version,
      },
    });

    if (!response.ok) {
      return formatResponseError(enterprise, response);
    }

    return { status: "OK", response: await response.json() };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Download and parse usage metrics data from the provided download links.
 * These are signed URLs that don't require authentication.
 */
export const downloadUsageMetricsReports = async <T = UsageMetricsData>(
  downloadLinks: string[]
): Promise<ServerActionResponse<T[]>> => {
  try {
    const allData: T[] = [];

    for (const link of downloadLinks) {
      // Signed URLs - no auth headers needed
      const response = await fetch(link, { cache: "no-store" });

      if (!response.ok) {
        console.warn(`Failed to download report from ${link}: ${response.status}`);
        continue;
      }

      const data = await response.json();
      if (Array.isArray(data)) {
        allData.push(...data);
      } else {
        allData.push(data);
      }
    }

    return { status: "OK", response: allData };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Get usage metrics from the new API with automatic download of report data.
 * This is a convenience function that combines fetching the report links and downloading the data.
 */
export const getCopilotUsageMetricsFromApi = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();
  if (env.status !== "OK") {
    return env;
  }

  try {
    let reportResponse: ServerActionResponse<UsageMetricsReportResponse>;

    // Determine if we're fetching enterprise or organization metrics
    if (filter.enterprise) {
      reportResponse = await getEnterpriseUsageMetricsLatest28Day(filter.enterprise);
    } else if (filter.organization) {
      reportResponse = await getOrgUsageMetricsLatest28Day(filter.organization);
    } else {
      return { status: "ERROR", errors: [{ message: "No organization or enterprise specified" }] };
    }

    if (reportResponse.status !== "OK") {
      return reportResponse;
    }

    // Download the actual metrics data from the report links
    if (!reportResponse.response.download_links?.length) {
      return { status: "OK", response: [] };
    }

    const downloadResult = await downloadUsageMetricsReports<CopilotMetrics>(
      reportResponse.response.download_links
    );

    if (downloadResult.status !== "OK") {
      return downloadResult;
    }

    // Apply time frame labels to match existing data format
    const dataWithTimeFrame = applyTimeFrameLabel(downloadResult.response);

    return { status: "OK", response: dataWithTimeFrame };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Get usage metrics from the Cosmos DB (usage_metrics_history container).
 * This reads data that was ingested by the new CopilotUsageMetricsIngestion function.
 */
export const getCopilotUsageMetricsFromDatabase = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const client = cosmosClient();
  const database = client.database("platform-engineering");
  const container = database.container("usage_metrics_history");

  let start = "";
  let end = "";
  const maxDays = 365 * 2; // maximum 2 years of data
  const maximumDays = 31;

  if (filter.startDate && filter.endDate) {
    start = format(filter.startDate, "yyyy-MM-dd");
    end = format(filter.endDate, "yyyy-MM-dd");
  } else {
    const todayDate = new Date();
    const startDate = new Date(todayDate);
    startDate.setDate(todayDate.getDate() - maximumDays);

    start = format(startDate, "yyyy-MM-dd");
    end = format(todayDate, "yyyy-MM-dd");
  }

  let querySpec: SqlQuerySpec = {
    query: `SELECT * FROM c WHERE c.date >= @start AND c.date <= @end AND c.report_type = 'usage_metrics'`,
    parameters: [
      { name: "@start", value: start },
      { name: "@end", value: end },
    ],
  };

  if (filter.enterprise) {
    querySpec.query += ` AND c.enterprise = @enterprise`;
    querySpec.parameters?.push({
      name: "@enterprise",
      value: filter.enterprise,
    });
  }

  if (filter.organization) {
    querySpec.query += ` AND c.organization = @organization`;
    querySpec.parameters?.push({
      name: "@organization",
      value: filter.organization,
    });
  }

  if (filter.team && filter.team.length > 0) {
    if (filter.team.length === 1) {
      querySpec.query += ` AND c.team = @team`;
      querySpec.parameters?.push({ name: "@team", value: filter.team[0] });
    } else {
      const teamConditions = filter.team
        .map((_, index) => `c.team = @team${index}`)
        .join(" OR ");
      querySpec.query += ` AND (${teamConditions})`;
      filter.team.forEach((team, index) => {
        querySpec.parameters?.push({ name: `@team${index}`, value: team });
      });
    }
  } else {
    querySpec.query += ` AND c.team = null`;
  }

  const { resources } = await container.items
    .query<CopilotMetrics>(querySpec, {
      maxItemCount: maxDays,
    })
    .fetchAll();

  const dataWithTimeFrame = applyTimeFrameLabel(resources);
  return {
    status: "OK",
    response: dataWithTimeFrame,
  };
};
