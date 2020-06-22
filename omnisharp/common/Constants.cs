using System;

namespace FastCodeNavPlugin.Common
{
    public static class Constants
    {
        public static readonly TimeSpan QueryCodeSearchTimeout = TimeSpan.FromSeconds(5);
        public const int MaxCodeSearchResults = 200;
    }
}
