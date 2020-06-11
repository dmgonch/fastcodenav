using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using FastCodeNavPlugin.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Helpers;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.FindUsages;

namespace OmniSharp.FastCodeNavPlugin
{
    [OmniSharpHandler(OmniSharpEndpoints.FindUsages, LanguageNames.CSharp)]
    public class CodeSearchFindUsagesService : IRequestHandler<FindUsagesRequest, QuickFixResponse>
    {
        private readonly ILogger _logger;
        private readonly OmniSharpWorkspace _workspace;
        private readonly ICodeSearchProvider _codeSearchServiceProvider;

        [ImportingConstructor]
        public CodeSearchFindUsagesService(
            ILoggerFactory loggerFactory,
            OmniSharpWorkspace workspace,
            ICodeSearchProvider codeSearchServiceProvider)
        {
            _logger = loggerFactory.CreateLogger<CodeSearchFindUsagesService>();
            _workspace = workspace;
            _codeSearchServiceProvider = codeSearchServiceProvider;
        }

        // Based on https://github.com/OmniSharp/omnisharp-roslyn/blob/cbfca2cccaf814f3c906a49c9321f0bc898fa0e6/src/OmniSharp.Roslyn.CSharp/Services/Navigation/FindUsagesService.cs
        public async Task<QuickFixResponse> Handle(FindUsagesRequest request)
        {
            Document document = _workspace.GetDocument(request.FileName);

            var workspaceResults = new List<QuickFix>();
            SourceText sourceText = null;
            int position = 0;
            ISymbol symbol = null;
            if (document != null)
            {
                var semanticModel = await document.GetSemanticModelAsync();
                sourceText = await document.GetTextAsync();
                position = sourceText.Lines.GetPosition(new LinePosition(request.Line, request.Column));
                symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
                var definition = await SymbolFinder.FindSourceDefinitionAsync(symbol, _workspace.CurrentSolution);
                var usages = request.OnlyThisFile
                    ? await SymbolFinder.FindReferencesAsync(definition ?? symbol, _workspace.CurrentSolution, ImmutableHashSet.Create(document))
                    : await SymbolFinder.FindReferencesAsync(definition ?? symbol, _workspace.CurrentSolution);
                var locations = usages.SelectMany(u => u.Locations).Select(l => l.Location).ToList();

                if (!request.ExcludeDefinition)
                {
                    // always skip get/set methods of properties from the list of definition locations.
                    var definitionLocations = usages.Select(u => u.Definition)
                        .Where(def => !(def is IMethodSymbol method && method.AssociatedSymbol is IPropertySymbol))
                        .SelectMany(def => def.Locations)
                        .Where(loc => loc.IsInSource && (!request.OnlyThisFile || loc.SourceTree.FilePath == request.FileName));

                    locations.AddRange(definitionLocations);
                }

                workspaceResults = locations.Distinct().Select(l => l.GetQuickFix(_workspace)).ToList();
            }

            List<QuickFix> codeSearchResults = await QueryCodeSearchForSymbolRefsAsync(request, symbol, sourceText, position);

            // Filter out results from files that are already loaded in the workspace because the standard handler will be providing those results
            // and they might be more accurate if the files were changed locally
            HashSet<string> filesAlreadyInWorkspace = new HashSet<string>(workspaceResults.Select(f => f.FileName), PathComparer.Instance);
            List<QuickFix> codeSearchOnlyResults = codeSearchResults.Where(codeSearchResult => !filesAlreadyInWorkspace.Contains(codeSearchResult.FileName)).ToList();

            var response = new QuickFixResponse(codeSearchOnlyResults.Distinct()
                                            .OrderBy(q => q.FileName)
                                            .ThenBy(q => q.Line)
                                            .ThenBy(q => q.Column));
            _logger.LogDebug($"FastCodeNav: search for symbol in {request.FileName} at ({request.Line},{request.Column}) returned {response.QuickFixes.Count()} items");
            return response;
        }

        private async Task<List<QuickFix>> QueryCodeSearchForSymbolRefsAsync(FindUsagesRequest request, ISymbol symbol,
            SourceText sourceText, int positionInSourceText)
        {
            // OnlyThisFile is supplied for CodeLense requests which won't benefit from code search service data.
            // If symbol is available then avoid querying code search service data for private symbols because all their references will be already loaded in Roslyn's workspace.
            if (request.OnlyThisFile || (symbol != null && symbol.DeclaredAccessibility == Accessibility.Private))
            {
                return new List<QuickFix>();
            }

            // Try to get symbol text from Roslyn's symbol object - this is the most presize option
            string symbolText;
            if (symbol != null && symbol.Locations != null && symbol.Locations.Any())
            {
                Location location = symbol.Locations.First();
                symbolText = (await location.SourceTree.GetTextAsync()).ToString(location.SourceSpan);
            }
            // Try to get symbol text from Roslyn's in-memory text which might be different from what is stored on disk
            else if (sourceText != null)
            {
                int symboldStartPosition = positionInSourceText;
                do
                {
                    symboldStartPosition--;
                } while (symboldStartPosition > 0 && char.IsLetter(sourceText[symboldStartPosition]));
                symboldStartPosition++;

                int symbolEndPosition = positionInSourceText;
                while (symbolEndPosition < sourceText.Length && char.IsLetterOrDigit(sourceText[symbolEndPosition]))
                {
                    symbolEndPosition++;
                }

                symbolText = sourceText.ToString(new TextSpan(symboldStartPosition, symbolEndPosition - symboldStartPosition));
            }
            else
            {
                CodeFileUtils.TryGetSymbolTextFromFile(request.FileName, request.Line, request.Column, out symbolText);
            }

            if (string.IsNullOrWhiteSpace(symbolText) || !char.IsLetter(symbolText.ElementAt(0)))
            {
                return new List<QuickFix>();
            }

            return await _codeSearchServiceProvider.CodeSearchService.QueryAsync(
                symbolText, Constants.MaxCodeSearchResults, Constants.QueryCodeSearchTimeout, exactMatch: true, CodeSearchQueryType.FindReferences);
        }
    }
}