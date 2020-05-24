using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.FindSymbols;

namespace OmniSharp.FastCodeNavPlugin
{
    [OmniSharpHandler(OmniSharpEndpoints.FindSymbols, LanguageNames.CSharp)]
    public class CodeSearchFindSymbolsService : IRequestHandler<FindSymbolsRequest, QuickFixResponse>
    {
        private static readonly TimeSpan QueryCodeSearchTimeout = TimeSpan.FromSeconds(5);
        private const int MaxCodeSearchResults = 200;

        private readonly OmniSharpWorkspace _workspace;
        private readonly ICodeSearchServiceProvider _codeSearchServiceProvider;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public CodeSearchFindSymbolsService(
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory,
            ICodeSearchServiceProvider codeSearchServiceProvider)
        {
            _workspace = workspace;
            _codeSearchServiceProvider = codeSearchServiceProvider;
            _logger = loggerFactory.CreateLogger<CodeSearchFindSymbolsService>();
        }

        public async Task<QuickFixResponse> Handle(FindSymbolsRequest request = null)
        {
            if (_codeSearchServiceProvider.CodeSearchService == null)
            {
                return null;
            }

            int? filterLength = request?.Filter?.Length;
            if (!filterLength.HasValue || filterLength.Value <= 0 || filterLength.Value < request?.MinFilterLength.GetValueOrDefault())
            {
                return new QuickFixResponse { QuickFixes = Array.Empty<QuickFix>() };
            }

            int maxItems = (request?.MaxItemsToReturn).GetValueOrDefault();

            Task<List<QuickFix>> queryCodeSearchTask = _codeSearchServiceProvider.CodeSearchService.Query(
                request.Filter, Math.Min(maxItems, MaxCodeSearchResults), QueryCodeSearchTimeout, exactMatch: false, CodeSearchQueryType.FindDefinitions);

            QuickFixResponse queryWorkspaceSymbols = await _workspace.CurrentSolution.FindSymbols(request?.Filter, ".csproj", maxItems);

            // Filter out symbols from files that are already loaded in the workspace because the standard FindSymbols handler will be providing those results
            // and they might be more accurate if the files were changed locally
            HashSet<string> filesAlreadyInWorkspace = new HashSet<string>(queryWorkspaceSymbols.QuickFixes.Select(l => l.FileName), PathComparer.Instance);
            List<QuickFix> codeSearchSymbols = await queryCodeSearchTask;
            List<QuickFix> codeSearchOnlySymbols = codeSearchSymbols.Where(codeSearchSymbol => !filesAlreadyInWorkspace.Contains(codeSearchSymbol.FileName)).ToList();

            return new QuickFixResponse()
            {
                QuickFixes = codeSearchOnlySymbols
            };
        }
    }
}