using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FastCodeNavPlugin.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OmniSharp.Models;
using StreamJsonRpc;

namespace OmniSharp.FastCodeNavPlugin
{
    internal class AzureDevOpsCodeSearch : ICodeSearch
    {
        private readonly RepoInfo _repoInfo;
        private readonly OmniSharpWorkspace _workspace;
        private readonly ILogger _logger;
        private readonly IMemoryCache _queryResultsCache = new MemoryCache(new MemoryCacheOptions());

        private Process _clientProcess;
        private JsonRpc _jsonRpc;

        public AzureDevOpsCodeSearch(
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory,
            RepoInfo repoInfo)
        {
            _workspace = workspace;
            _logger = loggerFactory.CreateLogger<AzureDevOpsCodeSearch>();
            _repoInfo = repoInfo;
        }

        public async Task InitializeCodeSearchServiceAsync()
        {
            if (_jsonRpc != null)
            {
                return;
            }

            string clientPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "AzDevOpsInteractiveClient.exe");

            string pipeName = $"AzDevOpsClientPipe-{Process.GetCurrentProcess().Id}";

            _clientProcess = new Process();
            _clientProcess.StartInfo = new ProcessStartInfo(clientPath)
            {
                Arguments = 
                    $@"--RootDir ""{_repoInfo.RootDir}"" " +
                    $@"--ProjectUri {_repoInfo.ProjectUri} " +
                    $@"--ProjectName ""{_repoInfo.ProjectName}"" " +
                    $@"--RepoName ""{_repoInfo.RepoName}"" " +
                    $@"--RpcPipeName ""{pipeName}"" ",
                UseShellExecute = true,
                CreateNoWindow = false,
                // RedirectStandardInput = true,
                // RedirectStandardOutput = true
            };

            _logger.LogDebug($"FastCodeNav: Launching {clientPath} with arguments '{_clientProcess.StartInfo.Arguments}'");
            if (!_clientProcess.Start())
            {
                _logger.LogError($"FastCodeNav: Failed to launch {clientPath}");
                return;
            }

            _logger.LogDebug($"FastCodeNav: Connecting to search service client with PID {_clientProcess.Id}");
            var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync();
            _logger.LogDebug($"FastCodeNav: Connected to search service client.");

            var jsonRpc = new JsonRpc(stream);
            var jsonRpcProxy = jsonRpc.Attach<ICodeSearchService>();
            jsonRpc.StartListening();
            _jsonRpc = jsonRpc;

            _logger.LogDebug($"FastCodeNav: Issuing a warmup RPC request");
            _jsonRpc.InvokeAsync("WarmUpAsync").FireAndForget(_logger);
        }

        public async Task<List<QuickFix>> QueryAsync(string filter, int maxResults, TimeSpan timeout, bool exactMatch, CodeSearchQueryType searchType)
        {
            if (_jsonRpc == null)
            {
                return new List<QuickFix>();
            }

            List<QuickFix> results = null;
            string searchTypeString;
            switch (searchType)
            {
                case CodeSearchQueryType.FindDefinitions:
                    searchTypeString = $"def:{filter}";
                    break;
                case CodeSearchQueryType.FindReferences:
                    searchTypeString = $"{filter}"; // Do not use ref: prefix because it misses too many things
                    break;
                default:
                    throw new InvalidOperationException($"Unknown search type {searchType}");
            }

            string cacheKey = $"{searchTypeString}{(exactMatch ? string.Empty : "*")}";
            if (_queryResultsCache.TryGetValue(cacheKey, out object cachedValue))
            {
                var cachedResults = (List<QuickFix>)cachedValue;
                _logger.LogDebug($"FastCodeNav: Reusing cached value for '{cacheKey}' that contains {cachedResults.Count()} result(s)");
                return cachedResults;
            }

            try
            {
                _logger.LogDebug($"FastCodeNav: Searching for '{cacheKey}'");
                using (var ct = new CancellationTokenSource(timeout))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    SearchResults response = await _jsonRpc.InvokeWithCancellationAsync<SearchResults>("SearchCodeAsync",
                        new[] { new SearchRequest
                        {
                            Filter = filter, 
                            MaxResults = maxResults, 
                            Timeout = timeout, 
                            ExactMatch = exactMatch, 
                            FindReferences = searchType == CodeSearchQueryType.FindReferences
                        }}, ct.Token);

                    _logger.LogDebug($"FastCodeNav: search for '{cacheKey}' completed in {sw.Elapsed.TotalSeconds:F2} seconds and contains {response.Results.Count()} result(s)");

                    if (response != null)
                    {
                        results = new List<QuickFix>();
                        foreach (SearchResult searchResult in response.Results)
                        {
                            results.Add(new QuickFix
                            {
                                FileName = searchResult.FileName, 
                                Text = searchResult.Text, 
                                Line = searchResult.Line, 
                                Column = searchResult.Column,
                                EndLine = searchResult.EndLine,
                                EndColumn = searchResult.EndColumn,
                            });
                        }

                        _queryResultsCache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to search for '{cacheKey}'");
            }

            return results ?? new List<QuickFix>();
        }
    }
}
