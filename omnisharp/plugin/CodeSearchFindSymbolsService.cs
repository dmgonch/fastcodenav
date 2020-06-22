using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using FastCodeNavPlugin.Common;
using Microsoft.CodeAnalysis;
using OmniSharp.Extensions;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.FindSymbols;

namespace OmniSharp.FastCodeNavPlugin
{
    [OmniSharpHandler(OmniSharpEndpoints.FindSymbols, LanguageNames.CSharp)]
    public class CodeSearchFindSymbolsService : IRequestHandler<FindSymbolsRequest, QuickFixResponse>
    {
        private readonly OmniSharpWorkspace _workspace;
        private readonly ICodeSearchProvider _codeSearchServiceProvider;

        [ImportingConstructor]
        public CodeSearchFindSymbolsService(
            OmniSharpWorkspace workspace,
            ICodeSearchProvider codeSearchServiceProvider)
        {
            _workspace = workspace;
            _codeSearchServiceProvider = codeSearchServiceProvider;
        }

        // Based on https://github.com/OmniSharp/omnisharp-roslyn/blob/cbfca2cccaf814f3c906a49c9321f0bc898fa0e6/src/OmniSharp.Roslyn.CSharp/Services/Navigation/FindSymbolsService.cs
        public async Task<QuickFixResponse> Handle(FindSymbolsRequest request = null)
        {
            if (_codeSearchServiceProvider?.CodeSearchService == null)
            {
                return null;
            }

            int? filterLength = request?.Filter?.Length;
            if (!filterLength.HasValue || filterLength.Value <= 0 || filterLength.Value < request?.MinFilterLength.GetValueOrDefault())
            {
                return new QuickFixResponse { QuickFixes = Array.Empty<QuickFix>() };
            }

            int maxItems = (request?.MaxItemsToReturn).GetValueOrDefault();

            Task<List<QuickFix>> queryCodeSearchTask = _codeSearchServiceProvider.CodeSearchService.QueryAsync(
                request.Filter, Math.Min(maxItems, Constants.MaxCodeSearchResults), Constants.QueryCodeSearchTimeout, exactMatch: false, CodeSearchQueryType.FindDefinitions);

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