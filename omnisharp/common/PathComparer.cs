// See https://github.com/microsoft/MSBuildPrediction/blob/4df4c90e0ef0e799c4f2d9c2b79c582dbc907050/src/BuildPrediction/PathComparer.cs

namespace FastCodeNavPlugin.Common
{
    using System;

    /// <summary>
    /// Gets the appdomain-wide appropriate filesystem path string comparer
    /// appropriate to the current operating system.
    /// </summary>
    public static class PathComparer
    {
        public static readonly StringComparer Instance = GetPathComparer();
        public static readonly StringComparison Comparison = GetPathComparison();

        private static StringComparer GetPathComparer()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    return StringComparer.Ordinal;
                default:
                    return StringComparer.OrdinalIgnoreCase;
            }
        }

        private static StringComparison GetPathComparison()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    return StringComparison.Ordinal;
                default:
                    return StringComparison.OrdinalIgnoreCase;
            }
        }
    }
}