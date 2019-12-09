using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models.WorkspaceInformation;
using OmniSharp.Services;

// Prefix namespace with 'OmniSharp' for logging messages to appear in VSCode Output window, 'OmniSharp Log' channel
namespace OmniSharp.FastCodeNavPlugin
{
    // Export a 'fake' project system to hook up into OmniSharp's initialization
    [ExportProjectSystem("FastCodeNavPlugin"), Shared]
    public class PluginProjectSystem : IProjectSystem
    {
        private readonly IOmniSharpEnvironment _environment;
        private readonly OmniSharpWorkspace _workspace;

        public string Key { get; } = "FastCodeNavPlugin";
        public string Language { get; } = "FastCodeNavPlugin";
        public IEnumerable<string> Extensions { get; } = Array.Empty<string>();
        public bool EnabledByDefault { get; } = true;
        public bool Initialized { get; private set; }

        private readonly ILogger _logger;

        [ImportingConstructor]
        public PluginProjectSystem(
            IOmniSharpEnvironment environment,
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _workspace = workspace;
            _logger = loggerFactory.CreateLogger<PluginProjectSystem>();
        }

        public Task<object> GetWorkspaceModelAsync(WorkspaceInformationRequest request)
        {
            return Task.FromResult((object)string.Empty);
        }

        public Task<object> GetProjectModelAsync(string filePath)
        {
            return Task.FromResult((object)string.Empty);
        }

        public void Initalize(IConfiguration configuration)
        {
            if (string.IsNullOrEmpty(_environment.TargetDirectory))
            {
                _logger.LogDebug($"FastCodeNav plugin cannot be initialized because OmniSharp environment's target directory is empty");
                return;
            }

            if (!RepoInfo.TryDetectRepoInfo(_environment.TargetDirectory, out RepoInfo repoInfo))
            {
                _logger.LogDebug($"FastCodeNav plugin could not detect Azure DevOps repo in directory {_environment.TargetDirectory}");
                return;
            }

            if (repoInfo.SearchProviderType != RepoSearchProviderType.AzureDevOps)
            {
                _logger.LogDebug($"FastCodeNav plugin does not support search provider for {repoInfo.ProjectUri}");
                return;
            }

            _logger.LogDebug($"FastCodeNav plugin has been initialized for project {repoInfo.ProjectUri}, repo {repoInfo.RepoName}.");
            Initialized = true;
        }
    }
}
