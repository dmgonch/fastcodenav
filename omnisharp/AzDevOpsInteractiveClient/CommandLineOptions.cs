using CommandLine;

namespace AzDevOpsInteractiveClient
{
    internal class CommandLineOptions
    {
        [Option(nameof(RootDir), Required = false, HelpText = "Repo clone local root dir.")]
        public string RootDir { get; set; }

        [Option(nameof(ProjectUri), Required = false, HelpText = "Azure DevOps repo project Uri.")]
        public string ProjectUri { get; set; }

        [Option(nameof(ProjectName), Required = false, HelpText = "Azure DevOps repo project name.")]
        public string ProjectName { get; set; }

        [Option(nameof(RepoName), Required = false, HelpText = "Azure DevOps repo name.")]
        public string RepoName { get; set; }
    }
}
