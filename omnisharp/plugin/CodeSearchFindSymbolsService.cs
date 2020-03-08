using System;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.FindSymbols;

namespace OmniSharp.FastCodeNavPlugin
{
    [OmniSharpHandler(OmniSharpEndpoints.FindSymbols, LanguageNames.CSharp)]
    public class CodeSearchFindSymbolsService : IRequestHandler<FindSymbolsRequest, QuickFixResponse>
    {
        private readonly OmniSharpWorkspace _workspace;
        private readonly ICodeSearchServiceProvider _codeSearchServiceProvider;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public CodeSearchFindSymbolsService(
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory,
            ICodeSearchServiceProvider codeSearchServiceProvider
            )
        {
            _workspace = workspace;
            _codeSearchServiceProvider = codeSearchServiceProvider;
            _logger = loggerFactory.CreateLogger<CodeSearchFindSymbolsService>();
        }

        public Task<QuickFixResponse> Handle(FindSymbolsRequest request = null)
        {
            return Task.FromResult(new QuickFixResponse {QuickFixes = new[] {new QuickFix { FileName = "fake.cs", Line = 1, Column = 1, EndColumn = 2, Text = "__ZZZ" }}});
        }
    }
}