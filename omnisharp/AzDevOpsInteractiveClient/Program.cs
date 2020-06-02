using System;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using FastCodeNavPlugin.Common;
using Microsoft.Extensions.Logging;

namespace AzDevOpsInteractiveClient
{
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
                LoggerFactoryInstance = LoggerFactory.Create(builder => builder.AddProvider(new CodeSearchServiceLoggerProvider()));
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
            return await Task.FromResult(0);
        }
    }
}
