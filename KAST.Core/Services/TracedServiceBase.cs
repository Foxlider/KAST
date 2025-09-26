using KAST.Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace KAST.Core.Services
{
    public abstract class TracedServiceBase
    {
        protected readonly ActivitySource ActivitySource;
        protected readonly ILogger Logger;

        protected TracedServiceBase(ITracingNamingProvider namingProvider, ILogger logger)
        {
            var sourceName = namingProvider.GetActivitySourceName(GetType());
            ActivitySource = new ActivitySource(sourceName);
            Logger = logger;
        }

        /// <summary>
        /// Starts a new activity with the given name and optional tags
        /// </summary>
        protected Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, 
            IEnumerable<KeyValuePair<string, object?>>? tags = null)
        {
            var activity = ActivitySource.StartActivity(name, kind);

            if (activity == null)
                return null;

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }

            Logger.LogDebug("Started activity {ActivityName} with ID {ActivityId}", name, activity.Id);
            return activity;
        }

        /// <summary>
        /// Executes an operation with automatic telemetry tracking
        /// </summary>
        protected async Task<T> ExecuteWithTelemetryAsync<T>(string operationName, Func<Activity?, Task<T>> operation,
            IEnumerable<KeyValuePair<string, object?>>? tags = null)
        {
            using var activity = StartActivity(operationName, ActivityKind.Internal, tags);
            
            try
            {
                Logger.LogInformation("Starting operation {OperationName}", operationName);
                var result = await operation(activity);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                Logger.LogInformation("Completed operation {OperationName} successfully", operationName);
                
                return result;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("exception.type", ex.GetType().Name);
                activity?.SetTag("exception.message", ex.Message);
                
                Logger.LogError(ex, "Operation {OperationName} failed", operationName);
                throw;
            }
        }

        /// <summary>
        /// Executes an operation with automatic telemetry tracking (void return)
        /// </summary>
        protected async Task ExecuteWithTelemetryAsync(string operationName, Func<Activity?, Task> operation,
            IEnumerable<KeyValuePair<string, object?>>? tags = null)
        {
            using var activity = StartActivity(operationName, ActivityKind.Internal, tags);
            
            try
            {
                Logger.LogInformation("Starting operation {OperationName}", operationName);
                await operation(activity);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                Logger.LogInformation("Completed operation {OperationName} successfully", operationName);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("exception.type", ex.GetType().Name);
                activity?.SetTag("exception.message", ex.Message);
                
                Logger.LogError(ex, "Operation {OperationName} failed", operationName);
                throw;
            }
        }

        /// <summary>
        /// Executes a synchronous operation with automatic telemetry tracking
        /// </summary>
        protected T ExecuteWithTelemetry<T>(string operationName, Func<Activity?, T> operation,
            IEnumerable<KeyValuePair<string, object?>>? tags = null)
        {
            using var activity = StartActivity(operationName, ActivityKind.Internal, tags);
            
            try
            {
                Logger.LogInformation("Starting operation {OperationName}", operationName);
                var result = operation(activity);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                Logger.LogInformation("Completed operation {OperationName} successfully", operationName);
                
                return result;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("exception.type", ex.GetType().Name);
                activity?.SetTag("exception.message", ex.Message);
                
                Logger.LogError(ex, "Operation {OperationName} failed", operationName);
                throw;
            }
        }
    }
}
