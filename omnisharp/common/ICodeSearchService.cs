using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastCodeNavPlugin.Common
{
    public interface ICodeSearchService1
    {
        Task<int> AddAsync(int a, int b);
        Task<SearchQueryResults> QueryAsync(SearchQueryRequest request);
    }

    public class SearchQueryRequest
    {
        public string Filter { get; set; }
        public int MaxResults { get; set; }
        public TimeSpan Timeout { get; set; }
        public bool ExactMatch { get; set; }
        public bool FindReferences { get; set; }
    }

    public class SearchQueryResult
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
        public SearchQueryResult[] Results { get; set; }
    }
}
