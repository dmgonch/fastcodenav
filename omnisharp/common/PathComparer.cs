using System;

namespace FastCodeNavPlugin.Common
{
    /// <summary>
    /// Gets path string comparer appropriate to the current operating system.
    /// Based on https://github.com/microsoft/MSBuildPrediction/blob/4df4c90e0ef0e799c4f2d9c2b79c582dbc907050/src/BuildPrediction/PathComparer.cs
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