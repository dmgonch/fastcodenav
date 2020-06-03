using CommandLine;

namespace AzDevOpsInteractiveClient
{
    internal class CommandLineOptions
    {
        [Option(nameof(RootDir), Required = true, HelpText = "Repo clone local root dir.")]
        public string RootDir { get; set; }

        [Option(nameof(ProjectUri), Required = true, HelpText = "Azure DevOps repo project Uri.")]
        public string ProjectUri { get; set; }

        [Option(nameof(ProjectName), Required = true, HelpText = "Azure DevOps repo project name.")]
        public string ProjectName { get; set; }

        [Option(nameof(RepoName), Required = true, HelpText = "Azure DevOps repo name.")]
        public string RepoName { get; set; }

        [Option(nameof(RpcPipeName), Required = true, HelpText = "Pipe name for RPC communication.")]
        public string RpcPipeName { get; set; }
    }
}
