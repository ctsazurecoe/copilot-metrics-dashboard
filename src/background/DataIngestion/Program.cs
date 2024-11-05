using System.Net.Http.Headers;
using Microsoft.CopilotDashboard.DataIngestion.Functions;
using Microsoft.CopilotDashboard.DataIngestion.Functions.CoPilotMetricsIngestion;
using Microsoft.CopilotDashboard.DataIngestion.Functions.CoPilotUsageIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<GithubMetricsApiOptions>(ctx.Configuration.GetSection("GITHUB_METRICS"));
        services.AddHttpClient<GitHubCopilotMetricsClient>(ConfigureClient);
        services.AddHttpClient<GitHubCopilotUsageClient>(ConfigureClient);
    })
    .Build();

host.Run();

void ConfigureClient(HttpClient httpClient)
{
    var apiVersion = Environment.GetEnvironmentVariable("GITHUB_API_VERSION");
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    httpClient.BaseAddress = new Uri("https://api.github.com/");
    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", apiVersion);
    httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubCopilotDataIngestion");
}