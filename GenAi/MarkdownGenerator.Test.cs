using XiansAi.Server.GenAi;
using DotNetEnv;
using Xunit;

namespace XiansAi.Server.GenAi.Tests;
public class MarkdownGeneratorTests
{
    private readonly MarkdownGenerator _markdownGenerator;
    private readonly IOpenAIClientService _openAIClientService;
    private readonly ILogger<MarkdownGenerator> _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MarkdownGenerator>();
    public MarkdownGeneratorTests()
    {
        Env.Load();
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY Environment variable is not set");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4";
        
        var config = new OpenAIConfig
        {
            Model = model,
            ApiKey = apiKey
        };
        _openAIClientService = new OpenAIClientService(config);
        _markdownGenerator = new MarkdownGenerator(_openAIClientService, _logger);
    }


    /*
    dotnet test --filter "FullyQualifiedName~XiansAi.Server.GenAi.Tests.MarkdownGeneratorTests" -v d
    */
    [Fact]
    public async Task GenerateMarkdown_WithValidDefinition_ReturnsMarkdown()
    {
        var definition = new FlowDefinition {
            TypeName = "TestFlow",
            ClassName = "TestFlow",
            Hash = "1234567890",
            Activities = new List<ActivityDefinition>(),
            Parameters = new List<ParameterDefinition>(),
            Source = GetTestDefinition()
        };
        var markdown = await _markdownGenerator.GenerateMarkdown(definition.Source);

        _logger.LogInformation("Markdown: {markdown}", markdown);
    }

    string GetTestDefinition()
    {
        return @"
        using Temporalio.Workflows;
        using XiansAi.Flow;
        using NineNineX.Prospecting.Activities;

        namespace NineNineX.Prospecting;

        public class ProspectCompany
        {
            public string? CompanyName { get; set; }
            public string? CompanyUrl { get; set; }
            public Dictionary<string, string>? ArticleData { get; set; }
        }

        [Workflow]
        public class ProspectingFlow: FlowBase
        {

            [WorkflowRun]
            public async Task<List<ProspectCompany>> RunAsync(string sourceLink)
            {
                // List of companies that are ISVs
                var isvCompanies = new List<ProspectCompany>();

                // Scrape news links
                var allLinks = await RunActivityAsync(
                    (ScrapeNewsLinksActivity a) => a.ScrapeNewsLinks(sourceLink));

                foreach (var link in allLinks)
                {
                    var article = await RunActivityAsync(
                        (ScrapeNewsDetailsActivity a) => a.ScrapeNewsDetails(link));

                    if (article == null || !article.TryGetValue(\""company\"", out var companyName) || string.IsNullOrEmpty(companyName))
                        continue;

                    var companyUrl = await RunActivityAsync(
                        (SearchForWebsiteActivity a) => a.SearchForWebsite(companyName));

                    if (companyUrl == null) continue;

                    var isISVCompany = await RunActivityAsync(
                        (IsProductCompanyActivity a) => a.IsProductCompany(companyUrl, companyName));

                    if (isISVCompany == true)
                    {
                        isvCompanies.Add(new ProspectCompany
                        {
                            CompanyName = companyName,
                            CompanyUrl = companyUrl,
                            ArticleData = article
                        });
                    }
                }

                return isvCompanies;
            }
        }

        ";
    }
}