using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastCodeNavPlugin.Common
{
    /// <summary>
    /// Represents online Code Search service
    /// </summary>
    public interface ICodeSearchOnlineService
    {
        Task<SearchQueryResults> SearchCodeAsync(SearchRequest searchRequest);
    }

    /// <summary>
    /// Filled in from O# request for example like https://github.com/OmniSharp/omnisharp-roslyn/blob/54d9230b66ba039e51371a598a9f9abafadc8c33/src/OmniSharp.Abstractions/Models/v1/FindSymbols/FindSymbolsRequest.cs
    /// </summary>
    public class SearchRequest
    {
        public string Filter { get; set; }
        public int MaxResults { get; set; }
        public TimeSpan Timeout { get; set; }
        public bool ExactMatch { get; set; }
        public bool FindReferences { get; set; }
    }

    /// <summary>
    /// Filled in from https://github.com/OmniSharp/omnisharp-roslyn/blob/62b3b52d01251fdc0564a600010936e677f24a2e/src/OmniSharp.Abstractions/Models/v1/QuickFix.cs
    /// </summary>
    public class SearchResult
    {
        public string FileName { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string Text { get; set; }
    }

    public class SearchQueryResults
    {
        public List<SearchResult> Results { get; set; }
    }
}
