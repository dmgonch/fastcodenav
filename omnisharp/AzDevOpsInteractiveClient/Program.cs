using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommandLine;
using CommandLine.Text;
using FastCodeNavPlugin.Common;
using StreamJsonRpc;
using System.Diagnostics;

namespace AzDevOpsInteractiveClient
{
    /// <summary>
    /// JSON RPC service that executes Code Search requests against Azure DevOps Code Search service and uses interactive authentication that is only available on Windows. 
    /// </summary>
    public class Program
    {
        private static  ILogger Logger;
        private static ILoggerFactory LoggerFactoryInstance;
        private static bool CatchUnobservedTaskExceptions = true;

        private CommandLineOptions _opts;
        
        private Program(CommandLineOptions opts)
        {
            _opts = opts;
        }

        public static int  Main(string[] args)
        {
            int retCode = 0;
            try
            {
                // When switching to Microsoft.Extensions.Logging 3.0+ replace with the following
                // (more info at https://docs.microsoft.com/en-us/aspnet/core/migration/logging-nonaspnetcore?view=aspnetcore-3.1)
                //LoggerFactoryInstance = LoggerFactory.Create(builder => builder.AddProvider(new CodeSearchServiceLoggerProvider()));
                LoggerFactoryInstance = new LoggerFactory();
                LoggerFactoryInstance.AddProvider(new CodeSearchServiceLoggerProvider());

                Logger = LoggerFactoryInstance.CreateLogger<Program>();
                Logger.LogInformation($"AzDevOpsInteractiveClient is starting");

                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

                try
                {
                    var cmdLineParser = new Parser(
                        with =>
                        {
                            with.EnableDashDash = true;
                            with.AutoHelp = true;
                            with.CaseSensitive = false;
                            with.CaseInsensitiveEnumValues = true;
                        });


                    ParserResult<CommandLineOptions> parserResult = cmdLineParser.ParseArguments<CommandLineOptions>(args);
                    parserResult
                        .WithParsed(opts => retCode = (new Program(opts).RunAsync().ConfigureAwait(false).GetAwaiter().GetResult()))
                        .WithNotParsed(_ => retCode = DisplayHelp(parserResult));
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Exiting due to unhandled exception");
                }
            }
            finally
            {
                LoggerFactoryInstance?.Dispose();
            }

            return retCode;
        }

        private static int DisplayHelp(ParserResult<CommandLineOptions> parserResult)
        {
            HelpText helpText = HelpText.AutoBuild(parserResult);
            Console.Error.WriteLine(helpText);
            return 1;
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            try
            {
                Logger.LogError(args?.Exception, "An unobserved task exception has occurred.");
            }
            catch (Exception loggerException)
            {
                Console.WriteLine($"Logger threw exception: {loggerException} while trying to log an unobserved task exception: {args?.Exception}");
            }

            if (CatchUnobservedTaskExceptions && args != null && !args.Observed)
            {
                args.SetObserved();
            }
        }

        internal async Task<int> RunAsync()
        {
            Logger.LogInformation($"AzDevOpsInteractiveClient is running");

            if (_opts.WaitDebug)
            {
                while (!Debugger.IsAttached)
                {
                    Logger.LogInformation("Awaiting for an attached debugger");
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
            }

            try
            {
                // Create and asyncronously initialize Azure DevOps client
                var service = new AzureDevOpsCodeSearchService(LoggerFactoryInstance, _opts);
                service.InitializeAsync().FireAndForget(Logger);

                Logger.LogInformation($"Waiting for client to make a connection to pipe {_opts.RpcPipeName} for repo {_opts.ProjectUri}");
                using (var stream = new NamedPipeServerStream(_opts.RpcPipeName, 
                    PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    await stream.WaitForConnectionAsync();
                    using (var jsonRpc = new JsonRpc(stream))
                    {
                        jsonRpc.AddLocalRpcTarget(service);
                        jsonRpc.StartListening();
                        await jsonRpc.Completion;
                    }
                }

                Logger.LogInformation($"AzDevOpsInteractiveClient is existing");
            }
            catch(Exception e)
            {
                Logger.LogError(e, $"Failure while processing RPC requests");
                return 1;
            }

            return 0;
        }
    }
}
