namespace OmniSharp.FastCodeNavPlugin
{
    public interface ICodeSearchProvider
    {
        ICodeSearch CodeSearchService { get; }
    }
}
