using System;
using Microsoft.Extensions.Logging;

namespace FastCodeNavPlugin.Common
{
    public class CodeSearchServiceLoggerProvider : ILoggerProvider
    {
        // Keep length the same for all names
        private static string[] LogLevelNames = new[] {"TRACE", "DEBUG", "INFO ", "WARN ", "ERROR", "CRIT ", "NONE "};

        public void Dispose()
        { 
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CodeSearchServiceLogger(categoryName);
        }

        public class CodeSearchServiceLogger : ILogger
        {
            private readonly string _categoryName;

            public CodeSearchServiceLogger(string categoryName)
            {
                _categoryName = categoryName;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string exceptionDetails = exception == null ? string.Empty : $"{Environment.NewLine}{exception}";
                string logLine = $"{LogLevelNames[(int)logLevel]} [{_categoryName}] {formatter(state, exception)}{exceptionDetails}";
                
                if (logLevel == LogLevel.Error || logLevel == LogLevel.Critical)
                {
                    Console.Error.WriteLine(logLine);
                }
                else
                {
                    Console.Out.WriteLine(logLine);
                }
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }
        }
    }
}
