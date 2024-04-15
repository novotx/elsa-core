using Elsa.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Results;

namespace Elsa.Workflows.Runtime.Contracts;

/// <summary>
/// Publishes events using the workflow runtime, effectively triggering all <see cref="Event"/> activities.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes the specified event.
    /// </summary>
    Task<ICollection<WorkflowExecutionResult>> PublishAsync(string eventName, string? correlationId = default, string? workflowInstanceId = default, string? activityInstanceId = default, object? payload = default, CancellationToken cancellationToken = default);
}