using OmniSharp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmniSharp.FastCodeNavPlugin
{
    public interface ICodeSearchService
    {
        Task<List<QuickFix>> QueryAsync(string filter, int maxResults, TimeSpan timeout, bool exactMatch, CodeSearchQueryType searchType);
    }

    public enum CodeSearchQueryType
    {
        FindDefinitions = 1,
        FindReferences = 2,
    };
}
