using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Search.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace OmniSharp.FastCodeNavPlugin
{
    internal class AzureDevOpsCodeSearchService : ICodeSearchService
    {
        private readonly OmniSharpWorkspace _workspace;
        private readonly ILogger _logger;
        
        private SearchHttpClient _searchClient;

        public AzureDevOpsCodeSearchService(
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory,
            RepoInfo repoInfo)
        {
            _workspace = workspace;
            _logger = loggerFactory.CreateLogger<AzureDevOpsCodeSearchService>();
            InitializeSearchClientAsync(repoInfo).FireAndForget(_logger);
        }

        private async Task InitializeSearchClientAsync(RepoInfo repoInfo)
        {
            if (_searchClient != null)
            {
                return;
            }

            try
            {
                _logger.LogDebug($"Initializing AzureDevOps Code Search Client for {repoInfo.ProjectUri}");
                var creds = new VssClientCredentials();
                var connection = new VssConnection(repoInfo.ProjectUri, creds);
                _searchClient = await connection.GetClientAsync<SearchHttpClient>();
                _logger.LogDebug($"Successfully initialized AzureDevOps Code Search Client for {repoInfo.ProjectUri}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to initialize AzureDevOps Code Search Client for {repoInfo.ProjectUri}");
            }
        }

        public Task Foo()
        {
            return Task.CompletedTask;
        }
    }
}
