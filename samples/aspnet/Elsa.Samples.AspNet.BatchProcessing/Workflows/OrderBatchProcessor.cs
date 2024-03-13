using Elsa.Samples.AspNet.BatchProcessing.Activities;
using Elsa.Samples.AspNet.BatchProcessing.Models;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;

namespace Elsa.Samples.AspNet.BatchProcessing.Workflows;

/// <summary>
/// A workflow that processes orders in batches.
/// </summary>
public class OrderBatchProcessor : WorkflowBase
{
    /// <inheritdoc />
    protected override void Build(IWorkflowBuilder builder)
    {
        var orders = builder.WithVariable<IAsyncEnumerable<ICollection<Order>>>();
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Fetching orders..."),
                new FetchOrders
                {
                    Result = new(orders)
                },
                new ParallelForEach<Order>
                {
                    Items = new(orders) 
                },
                new WriteLine("Done!")
            }
        };
    }
}