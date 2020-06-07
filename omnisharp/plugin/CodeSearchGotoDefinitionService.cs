using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using FastCodeNavPlugin.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.GotoDefinition;
using OmniSharp.Models.Metadata;
using OmniSharp.Options;
using OmniSharp.Roslyn.CSharp.Services;

namespace OmniSharp.FastCodeNavPlugin
{
    [OmniSharpHandler(OmniSharpEndpoints.GotoDefinition, LanguageNames.CSharp)]
    public class GotoDefinitionService : IRequestHandler<GotoDefinitionRequest, GotoDefinitionResponse>
    {
        private readonly ILogger _logger;
        private readonly ICodeSearchProvider _codeSearchServiceProvider;
        private readonly OmniSharpOptions _omnisharpOptions;
        private readonly OmniSharpWorkspace _workspace;
        private readonly ExternalSourceServiceFactory _externalSourceServiceFactory;

        [ImportingConstructor]
        public GotoDefinitionService(
            ILoggerFactory loggerFactory,
            ICodeSearchProvider codeSearchServiceProvider,
            OmniSharpWorkspace workspace,
            ExternalSourceServiceFactory externalSourceServiceFactory,
            OmniSharpOptions omnisharpOptions)
        {
            _logger = loggerFactory.CreateLogger<GotoDefinitionService>();
            _codeSearchServiceProvider = codeSearchServiceProvider;
            _workspace = workspace;
            _externalSourceServiceFactory = externalSourceServiceFactory;
            _omnisharpOptions = omnisharpOptions;
        }

        // Based on https://github.com/OmniSharp/omnisharp-roslyn/blob/cbfca2cccaf814f3c906a49c9321f0bc898fa0e6/src/OmniSharp.Roslyn.CSharp/Services/Navigation/GotoDefinitionService.cs
        public async Task<GotoDefinitionResponse> Handle(GotoDefinitionRequest request)
        {
            var externalSourceService = _externalSourceServiceFactory.Create(_omnisharpOptions);
            var document = externalSourceService.FindDocumentInCache(request.FileName) ??
                _workspace.GetDocument(request.FileName);

            var response = new GotoDefinitionResponse();

            if (document == null)
            {
                _logger.LogDebug($"FastCodeNav: Couldn't get document for {request.FileName}");
                return await GetDefinitionFromCodeSearchAsync(request);
            }

            if (document != null)
            {
                var semanticModel = await document.GetSemanticModelAsync();
                var sourceText = await document.GetTextAsync();
                var position = sourceText.Lines.GetPosition(new LinePosition(request.Line, request.Column));
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);

                if (symbol == null)
                {
                    return await GetDefinitionFromCodeSearchAsync(request);
                }

                // go to definition for namespaces is not supported
                if (symbol != null && !(symbol is INamespaceSymbol))
                {
                    // for partial methods, pick the one with body
                    if (symbol is IMethodSymbol method)
                    {
                        // Return an empty response for property accessor symbols like get and set
                        if (method.AssociatedSymbol is IPropertySymbol)
                            return response;

                        symbol = method.PartialImplementationPart ?? symbol;
                    }

                    var location = symbol.Locations.First();

                    if (location.IsInSource)
                    {
                        var lineSpan = symbol.Locations.First().GetMappedLineSpan();
                        response = new GotoDefinitionResponse
                        {
                            FileName = lineSpan.Path,
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character
                        };
                    }
                    else if (location.IsInMetadata && request.WantMetadata)
                    {
                        var cancellationToken = _externalSourceServiceFactory.CreateCancellationToken(_omnisharpOptions, request.Timeout);
                        var (metadataDocument, _) = await externalSourceService.GetAndAddExternalSymbolDocument(document.Project, symbol, cancellationToken);
                        if (metadataDocument != null)
                        {
                            cancellationToken = _externalSourceServiceFactory.CreateCancellationToken(_omnisharpOptions, request.Timeout);
                            var metadataLocation = await externalSourceService.GetExternalSymbolLocation(symbol, metadataDocument, cancellationToken);
                            var lineSpan = metadataLocation.GetMappedLineSpan();

                            response = new GotoDefinitionResponse
                            {
                                Line = lineSpan.StartLinePosition.Line,
                                Column = lineSpan.StartLinePosition.Character,
                                MetadataSource = new MetadataSource()
                                {
                                    AssemblyName = symbol.ContainingAssembly.Name,
                                    ProjectName = document.Project.Name,
                                    TypeName = symbol.GetSymbolName()
                                },
                            };
                        }
                    }
                }
            }
            return response;
        }

        private async Task<GotoDefinitionResponse> GetDefinitionFromCodeSearchAsync(GotoDefinitionRequest request)
        {
            var response = new GotoDefinitionResponse();
            if (!CodeFileUtils.TryGetSymbolTextFromFile(request.FileName, request.Line, request.Column, out string symbolText))
            {
                return response;
            }

            List<QuickFix> hits = await _codeSearchServiceProvider.CodeSearchService.QueryAsync(
                symbolText, maxResults: 1, Constants.QueryCodeSearchTimeout, exactMatch: true, CodeSearchQueryType.FindDefinitions);
            if (hits.Count != 0)
            {
                response.FileName = hits[0].FileName;
                response.Column = hits[0].Column;
                response.Line = hits[0].Line;
            }
            return response;
        }
    }
}