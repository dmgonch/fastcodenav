using System;
using System.IO;
using System.Text.RegularExpressions;
using OmniSharp.Utilities;

namespace OmniSharp.FastCodeNavPlugin
{
    internal class RepoInfo
    {
        private static readonly Regex AzureDevOpsUriRegex = new Regex(
            @"(?ix)^https://(?<account>.+)\.(?<domain>visualstudio\.com|azure.com)/(?<collection_or_project>[^/]+)(/(?<project>.+))?/_git/(?<optimization>_full/|_optimized/)?(?<repo>.+)$", RegexOptions.Compiled);

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

            string account = match.Groups["account"].Value;
            string domain = match.Groups["domain"].Value;
            repoName = match.Groups["repo"].Value.TrimEnd('/');

            string collection_or_project = match.Groups["collection_or_project"].Value;
            projectName = match.Groups["project"].Success ? match.Groups["project"].Value :
                (!string.IsNullOrEmpty(collection_or_project) && !collection_or_project.Equals("DefaultCollection") ? collection_or_project : repoName);

            projectUri = new Uri($"https://{account}.{domain}/{projectName}", UriKind.Absolute);
            return true;
        }
    }
}