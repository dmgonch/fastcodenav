using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using FastCodeNavPlugin.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Search.Shared.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Search.WebApi;
using Microsoft.VisualStudio.Services.Search.WebApi.Contracts.Code;
using Microsoft.VisualStudio.Services.WebApi;
using OmniSharp.Models;
using OmniSharp.Models.V2;

namespace OmniSharp.FastCodeNavPlugin
{
    internal class AzureDevOpsCodeSearchService : ICodeSearchService
    {
        private readonly RepoInfo _repoInfo;
        private readonly OmniSharpWorkspace _workspace;
        private readonly ILogger _logger;
        private readonly IDictionary<string, IEnumerable<string>> _searchFilters;
        private readonly IMemoryCache _queryResultsCache = new MemoryCache(new MemoryCacheOptions());
        private SearchHttpClient _searchClient;

        public AzureDevOpsCodeSearchService(
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory,
            RepoInfo repoInfo)
        {
            _workspace = workspace;
            _logger = loggerFactory.CreateLogger<AzureDevOpsCodeSearchService>();
            _repoInfo = repoInfo;

            _searchFilters = new Dictionary<string, IEnumerable<string>>
            {
                { "Project", new[] { repoInfo.ProjectName } },
                { "Repository", new[] { repoInfo.RepoName } },
                { "Branch", new[] { "master" } }
            };

            InitializeSearchClientAsync(repoInfo).FireAndForget(_logger);
        }

        public async Task InitializeSearchClientAsync(RepoInfo repoInfo)
        {
            if (_searchClient != null)
            {
                return;
            }

            try
            {
                _logger.LogDebug($"Initializing Azure DevOps Code Search Client for {repoInfo.ProjectUri}");
                var creds = new VssClientCredentials();
                var connection = new VssConnection(repoInfo.ProjectUri, creds);
                _searchClient = await connection.GetClientAsync<SearchHttpClient>();
                _logger.LogDebug($"Successfully initialized Azure DevOps Code Search Client for {repoInfo.ProjectUri}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to initialize Azure DevOps Code Search Client for {repoInfo.ProjectUri}");
            }
        }

        public async Task<List<QuickFix>> QueryAsync(string filter, int maxResults, TimeSpan timeout, bool exactMatch, CodeSearchQueryType searchType)
        {
            if (_searchClient == null)
            {
                return new List<QuickFix>();
            }

            List<QuickFix> result = null;
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

            var request = new CodeSearchRequest
            {
                SearchText = $"ext:cs {searchTypeString}{(exactMatch ? string.Empty : "*")}",
                Skip = 0,
                Top = maxResults,
                Filters = _searchFilters,
                IncludeFacets = false
            };

            if (_queryResultsCache.TryGetValue(request.SearchText, out object cachedValue))
            {
                var cachedResults = (List<QuickFix>)cachedValue;
                _logger.LogDebug($"Reusing cached value for Azure DevOps Code Search filter '{request.SearchText}' that contains {cachedResults.Count()} result(s)");
                return cachedResults;
            }

            try
            {
                _logger.LogDebug($"Querying Azure DevOps Code Search with filter '{request.SearchText}'");
                using (var ct = new CancellationTokenSource(timeout))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    CodeSearchResponse response = await _searchClient.FetchCodeSearchResultsAsync(request, ct);
                    _logger.LogDebug($"Response from Azure DevOps Code Search for filter '{request.SearchText}' completed in {sw.Elapsed.TotalSeconds} seconds and contains {response.Results.Count()} result(s)");

                    if (response != null)
                    {
                        result = await GetQuickFixesFromCodeResultsAsync(response.Results, searchType, filter, exactMatch);
                        _queryResultsCache.Set(request.SearchText, result, TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to query Azure DevOps Code Search for filter '{request.SearchText}'");
            }

            return result ?? new List<QuickFix>();
        }
        
        private async Task<List<QuickFix>> GetQuickFixesFromCodeResultsAsync(IEnumerable<CodeResult> codeResults,
            CodeSearchQueryType searchType, string searchFilter, bool exactMatch)
        {
            var transform = new TransformBlock<CodeResult, List<QuickFix>>(codeResult =>
            {
                string filePath = Path.Combine(_repoInfo.RootDir, codeResult.Path.TrimStart('/').Replace('/', '\\'));
                if (!File.Exists(filePath))
                {
                    _logger.LogDebug($"File {filePath} from CodeResult doesn't exist");
                    return null;
                }

                return GetQuickFixesFromCodeResult(codeResult, filePath, searchType, searchFilter, exactMatch);
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount - 1,
                BoundedCapacity = DataflowBlockOptions.Unbounded
            });

            var buffer = new BufferBlock<List<QuickFix>>();
            transform.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });

            foreach (CodeResult codeResult in codeResults)
            {
                await transform.SendAsync(codeResult);
            }
            transform.Complete();

            var allFoundSymbols = new List<QuickFix>();
            while (await buffer.OutputAvailableAsync().ConfigureAwait(false))
            {
                foreach (List<QuickFix> symbols in buffer.ReceiveAll().Where(item => item != null))
                {
                    allFoundSymbols.AddRange(symbols);
                }
            }

            // Propagate an exception if it occurred
            await buffer.Completion;
            return allFoundSymbols;
        }

        private static List<QuickFix> GetQuickFixesFromCodeResult(CodeResult codeResult, string filePath, CodeSearchQueryType searchType,
            string searchFilter, bool exactMatch)
        {
            var foundSymbols = new List<QuickFix>();
            if (!codeResult.Matches.TryGetValue("content", out IEnumerable<Hit> contentHitsEnum))
            {
                return foundSymbols;
            }

            List<Hit> contentHits = contentHitsEnum.ToList();
            int lineOffsetWin = 0;
            int lineOffsetUnix = 0;
            int lineNumber = 0;

            foreach (string line in File.ReadLines(filePath))
            {
                foreach (Hit hit in contentHits.OrderBy(h => h.CharOffset))
                {
                    // Hit.CharOffset is calculated based on end-of-lines of the file stored by the service which can be different from local file end-of-lines. 
                    // Try guess what end-of-lines were used by the service and use the first match.
                    if (hit.CharOffset >= lineOffsetUnix && hit.CharOffset < lineOffsetUnix + line.Length)
                    {
                        if (ConsiderMatchCandidate(filePath, searchType, searchFilter, exactMatch, hit, lineNumber, lineOffsetUnix, line, foundSymbols))
                        {
                            // Keep only a single match per line per hit - it is already clone enough
                            continue;
                        }
                    }

                    if (hit.CharOffset >= lineOffsetWin && hit.CharOffset < lineOffsetWin + line.Length)
                    {
                        ConsiderMatchCandidate(filePath, searchType, searchFilter, exactMatch, hit, lineNumber, lineOffsetWin, line, foundSymbols);
                    }
                }

                if (foundSymbols.Count >= contentHits.Count)
                {
                    // Found matching symbols for all hits - can stop searching the file
                    break;
                }

                lineOffsetWin += line.Length + 2;
                lineOffsetUnix += line.Length + 1;
                lineNumber++;
            }

            return foundSymbols;
        }

        private static bool ConsiderMatchCandidate(string filePath, CodeSearchQueryType searchType, string searchFilter, bool exactMatch, Hit hit,
            int lineNumber, int lineOffset, string line, List<QuickFix> foundSymbols)
        {
            if (line.Trim().Length >= 2 && line.Trim().Substring(0, 2) == @"//")
            {
                // ignore obvious mismatches like comments
                return false;
            }

            var symbolLocation = new SymbolLocation
            {
                Kind = SymbolKinds.Unknown,
                FileName = filePath,
                Line = lineNumber,
                EndLine = lineNumber,
                Column = hit.CharOffset - lineOffset,
                EndColumn = hit.CharOffset - lineOffset + hit.Length,
                Projects = new List<string>()
            };

            string symbolText = line.Substring(symbolLocation.Column, Math.Min(hit.Length, line.Length - symbolLocation.Column));

            // Azure DevOps Code Search is case-insensitive. Require complete case match when looking for references or the definition
            if (exactMatch && !symbolText.Equals(searchFilter, StringComparison.Ordinal))
            {
                return false;
            }

            // Detect the case when local enlistment and Azure DevOps Code Search index is out of sync and skip such hits.
            // Or the case when the end of lines are different b/w what was used when calculating hit offset of the server vs end of lines in the local file
            if (!exactMatch && symbolText.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            if (searchType == CodeSearchQueryType.FindReferences)
            {
                symbolLocation.Text = line.Trim();
            }
            else
            {
                symbolLocation.Text = symbolText;
            }

            foundSymbols.Add(symbolLocation);
            return true;
        }
    }
}
