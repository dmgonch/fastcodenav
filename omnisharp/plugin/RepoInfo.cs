using System;
using System.IO;
using System.Text.RegularExpressions;
using OmniSharp.Utilities;

namespace OmniSharp.FastCodeNavPlugin
{
    public class RepoInfo
    {
        private static readonly Regex AzureDevOpsUriRegex = new Regex(
            @"(?ix)^https://(?<account>.+)\.(?<domain>visualstudio\.com|azure.com)/(?<collection>[^/]+)(/(?<project>.+))?/_git/(?<optimization>_full/|_optimized/)?(?<repo>.+)$", RegexOptions.Compiled);

        public string RootDir { get; }
        public Uri ProjectUri { get; }
        public string ProjectName { get; }
        public string RepoName { get; }
        public RepoSearchProviderType SearchProviderType { get; }

        private RepoInfo(string rootDir, RepoSearchProviderType searchProviderType, Uri projectUri, string projectName, string repoName)
        {
            RootDir = rootDir;
            SearchProviderType = searchProviderType;
            ProjectUri = projectUri;
            ProjectName = projectName;
            RepoName = repoName;
        }

        public static bool TryDetectRepoInfo(string targetDirectory, out RepoInfo repoInfo)
        {
            repoInfo = null;
            if (!TryGetRepoRoot(targetDirectory, out string repoRoot))
            {
                return false;
            }

            string repoUri = ProcessHelper.RunAndCaptureOutput("git.exe", "config --get remote.origin.url", repoRoot);
            if (string.IsNullOrEmpty(repoUri) || !TryParseRepoUri(repoUri, out Uri projectUri, out string projectName, out string repoName))
            {
                return false;
            }

            repoInfo = new RepoInfo(repoRoot, RepoSearchProviderType.AzureDevOps, projectUri, projectName, repoName);
            return true;
        }

        private static bool TryGetRepoRoot(string targetDirectory, out string repoRoot)
        {
            repoRoot = null;
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return false;
            }

            repoRoot = targetDirectory;
            while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot, ".git")))
            {
                repoRoot = Path.GetDirectoryName(repoRoot);
            }

            return repoRoot != null;
        }

        private static bool TryParseRepoUri(
            string repoUri,
            out Uri projectUri,
            out string projectName,
            out string repoName)
        {
            projectUri = null;
            projectName = null;
            repoName = null;

            Match match = AzureDevOpsUriRegex.Match(repoUri);
            if (!match.Success)
            {
                return false;
            }

            string account = GetMatchValue(match, "account");
            string domain = GetMatchValue(match, "domain");
            repoName = GetMatchValue(match, "repo").TrimEnd('/');
            string collectionValue = GetMatchValue(match, "collection");    // May be collection or project name
            string projectValue = GetMatchValue(match, "project");  // May be omitted in the Uri

            projectName = !string.IsNullOrEmpty(projectValue) ? projectValue : !collectionValue.Equals("DefaultCollection") ? collectionValue : repoName;
            string collectionName = !string.IsNullOrEmpty(projectValue) ? collectionValue : string.Empty;

            projectUri = new Uri($"https://{account}.{domain}/{collectionName}", UriKind.Absolute);
            return true;
        }

        private static string GetMatchValue(Match match, string group)
        {
            return match.Groups[group].Success ? match.Groups[group].Value : string.Empty;
        }
    }
}