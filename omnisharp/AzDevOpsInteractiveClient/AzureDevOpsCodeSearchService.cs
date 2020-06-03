using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Search.Shared.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Search.WebApi;
using Microsoft.VisualStudio.Services.Search.WebApi.Contracts.Code;
using Microsoft.VisualStudio.Services.WebApi;
using FastCodeNavPlugin.Common;

namespace AzDevOpsInteractiveClient
{
    internal class AzureDevOpsCodeSearchService : ICodeSearchOnlineService
    {
        private readonly ILogger _logger;
        private readonly IDictionary<string, IEnumerable<string>> _searchFilters;
        private readonly Uri _projectUri;
        private readonly string _repoRootDir;

        private SearchHttpClient _searchClient;

        public AzureDevOpsCodeSearchService(ILoggerFactory loggerFactory, CommandLineOptions opts)
        {
            _logger = loggerFactory.CreateLogger(nameof(AzureDevOpsCodeSearchService));
            _projectUri = new Uri(opts.ProjectUri, UriKind.Absolute);
            _repoRootDir = opts.RootDir;

            _searchFilters = new Dictionary<string, IEnumerable<string>>
            {
                { "Project", new[] { opts.ProjectName } },
                { "Repository", new[] { opts.RepoName } },
                { "Branch", new[] { "master" } }
            };
        }

        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation($"Initializing Azure DevOps Code Search Client for {_projectUri}");
                var creds = new VssClientCredentials();
                var connection = new VssConnection(_projectUri, creds);
                _searchClient = await connection.GetClientAsync<SearchHttpClient>();
                _logger.LogInformation($"Successfully initialized Azure DevOps Code Search Client for {_projectUri}");

                // Kick off a warm up query
                await SearchCodeAsync(new SearchRequest { Filter = "_FastCodeNav_VSCode_Extension_WarmUp_", MaxResults = 1 });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to initialize Azure DevOps Code Search Client for {_projectUri}");
            }
        }

        public Task WarmUpAsync()
        {
            _logger.LogInformation($"Received warmup request.");
            return Task.FromResult(0);
        }

        public async Task<SearchResults> SearchCodeAsync(SearchRequest searchRequest)
        {
            if (_searchClient == null)
            {
                _logger.LogInformation($"Code Search client isn't ready. Couldn't process request with filter '{searchRequest.Filter}'.");
                return new SearchResults();
            }

            SearchResults result = null;
            string searchTypeString;
            if (searchRequest.FindReferences)
            {
                searchTypeString = $"{searchRequest.Filter}"; // Do not use ref: prefix because it misses too many things
            }
            else
            {
                searchTypeString = $"def:{searchRequest.Filter}";
            }

            var request = new CodeSearchRequest
            {
                SearchText = $"ext:cs {searchTypeString}{(searchRequest.ExactMatch ? string.Empty : "*")}",
                Skip = 0,
                Top = searchRequest.MaxResults,
                Filters = _searchFilters,
                IncludeFacets = false
            };

            try
            {
                _logger.LogInformation($"Querying Azure DevOps Code Search with filter '{request.SearchText}'");
                using (var ct = new CancellationTokenSource(searchRequest.Timeout))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    CodeSearchResponse response = await _searchClient.FetchCodeSearchResultsAsync(request, ct);
                    _logger.LogInformation($"Response from Azure DevOps Code Search for filter '{request.SearchText}' completed in {sw.Elapsed.TotalSeconds:F2} seconds and contains {response.Results.Count()} result(s)");

                    if (response != null)
                    {
                        result = await GetQuickFixesFromCodeResultsAsync(response.Results, searchRequest.FindReferences, searchRequest.Filter, searchRequest.ExactMatch);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to query Azure DevOps Code Search for filter '{request.SearchText}'");
            }

            return result ?? new SearchResults();
        }

        private async Task<SearchResults> GetQuickFixesFromCodeResultsAsync(IEnumerable<CodeResult> codeResults,
            bool findReferences, string searchFilter, bool exactMatch)
        {
            var transform = new TransformBlock<CodeResult, List<SearchResult>>(codeResult =>
            {
                string filePath = Path.Combine(_repoRootDir, codeResult.Path.TrimStart('/').Replace('/', '\\'));
                if (!File.Exists(filePath))
                {
                    _logger.LogInformation($"File {filePath} from CodeResult doesn't exist");
                    return null;
                }

                return GetQuickFixesFromCodeResult(codeResult, filePath, findReferences, searchFilter, exactMatch);
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount - 1,
                BoundedCapacity = DataflowBlockOptions.Unbounded
            });

            var buffer = new BufferBlock<List<SearchResult>>();
            transform.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });

            foreach (CodeResult codeResult in codeResults)
            {
                await transform.SendAsync(codeResult);
            }
            transform.Complete();

            var allFoundSymbols = new List<SearchResult>();
            while (await buffer.OutputAvailableAsync().ConfigureAwait(false))
            {
                foreach (List<SearchResult> symbols in buffer.ReceiveAll().Where(item => item != null))
                {
                    allFoundSymbols.AddRange(symbols);
                }
            }

            // Propagate an exception if it occurred
            await buffer.Completion;
            return new SearchResults { Results = allFoundSymbols };
        }

        private static List<SearchResult> GetQuickFixesFromCodeResult(CodeResult codeResult, string filePath, bool findReferences,
            string searchFilter, bool exactMatch)
        {
            var foundSymbols = new List<SearchResult>();
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
                        if (ConsiderMatchCandidate(filePath, findReferences, searchFilter, exactMatch, hit, lineNumber, lineOffsetUnix, line, foundSymbols))
                        {
                            // Keep only a single match per line per hit - it is already clone enough
                            continue;
                        }
                    }

                    if (hit.CharOffset >= lineOffsetWin && hit.CharOffset < lineOffsetWin + line.Length)
                    {
                        ConsiderMatchCandidate(filePath, findReferences, searchFilter, exactMatch, hit, lineNumber, lineOffsetWin, line, foundSymbols);
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

        private static bool ConsiderMatchCandidate(string filePath, bool findReferences, string searchFilter, bool exactMatch, Hit hit,
            int lineNumber, int lineOffset, string line, List<SearchResult> foundSymbols)
        {
            if (line.Trim().Length >= 2 && line.Trim().Substring(0, 2) == @"//")
            {
                // ignore obvious mismatches like comments
                return false;
            }

            var symbolLocation = new SearchResult
            {
                FileName = filePath,
                Line = lineNumber,
                EndLine = lineNumber,
                Column = hit.CharOffset - lineOffset,
                EndColumn = hit.CharOffset - lineOffset + hit.Length
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

            if (findReferences)
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
