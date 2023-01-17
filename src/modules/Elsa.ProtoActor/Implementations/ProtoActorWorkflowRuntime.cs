using System.Text.Json;
using Elsa.Common.Models;
using Elsa.Extensions;
using Elsa.ProtoActor.Extensions;
using Elsa.ProtoActor.Grains;
using Elsa.ProtoActor.Protos;
using Elsa.Workflows.Core.Models;
using Elsa.Workflows.Core.Serialization;
using Elsa.Workflows.Core.Services;
using Elsa.Workflows.Core.State;
using Elsa.Workflows.Runtime.Services;
using Proto.Cluster;

namespace Elsa.ProtoActor.Implementations;

/// <summary>
/// A Proto.Actor implementation of <see cref="IWorkflowRuntime"/>.
/// </summary>
public class ProtoActorWorkflowRuntime : IWorkflowRuntime
{
    private readonly Cluster _cluster;
    private readonly SerializerOptionsProvider _serializerOptionsProvider;
    private readonly ITriggerStore _triggerStore;
    private readonly IIdentityGenerator _identityGenerator;
    private readonly IBookmarkHasher _hasher;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ProtoActorWorkflowRuntime(
        Cluster cluster,
        SerializerOptionsProvider serializerOptionsProvider,
        ITriggerStore triggerStore,
        IIdentityGenerator identityGenerator,
        IBookmarkHasher hasher)
    {
        _cluster = cluster;
        _serializerOptionsProvider = serializerOptionsProvider;
        _triggerStore = triggerStore;
        _identityGenerator = identityGenerator;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public async Task<CanStartWorkflowResult> CanStartWorkflowAsync(string definitionId, StartWorkflowRuntimeOptions options, CancellationToken cancellationToken)
    {
        var versionOptions = options.VersionOptions;
        var correlationId = options.CorrelationId;
        var input = options.Input;
        var workflowInstanceId = _identityGenerator.GenerateId();
        
        var request = new StartWorkflowRequest
        {
            DefinitionId = definitionId,
            InstanceId = workflowInstanceId, 
            VersionOptions = versionOptions.ToString(),
            CorrelationId = correlationId.EmptyIfNull(),
            Input = input?.Serialize(),
            TriggerActivityId = options.TriggerActivityId.EmptyIfNull()
        };
        
        var client = _cluster.GetNamedWorkflowGrain(workflowInstanceId);
        var response = await client.CanStart(request, cancellationToken);

        return new CanStartWorkflowResult(workflowInstanceId, response!.CanStart);
    }

    /// <inheritdoc />
    public async Task<StartWorkflowResult> StartWorkflowAsync(string definitionId, StartWorkflowRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        var versionOptions = options.VersionOptions;
        var correlationId = options.CorrelationId;
        var workflowInstanceId = _identityGenerator.GenerateId();
        var input = options.Input;

        var request = new StartWorkflowRequest
        {
            DefinitionId = definitionId,
            InstanceId = workflowInstanceId,
            VersionOptions = versionOptions.ToString(),
            CorrelationId = correlationId.WithDefault(""),
            Input = input?.Serialize(),
            TriggerActivityId = options.TriggerActivityId.WithDefault("")
        };
        
        var client = _cluster.GetNamedWorkflowGrain(workflowInstanceId);
        var response = await client.Start(request, cancellationToken);
        var bookmarks = Map(response!.Bookmarks).ToList();

        return new StartWorkflowResult(workflowInstanceId, bookmarks);
    }

    /// <inheritdoc />
    public async Task<ResumeWorkflowResult> ResumeWorkflowAsync(string workflowInstanceId, ResumeWorkflowRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        var request = new ResumeWorkflowRequest
        {
            InstanceId = workflowInstanceId,
            CorrelationId = options.CorrelationId.EmptyIfNull(),
            BookmarkId = options.BookmarkId.EmptyIfNull(),
            ActivityId = options.ActivityId.EmptyIfNull(),
            Input = options.Input?.Serialize()
        };
        
        var client = _cluster.GetNamedWorkflowGrain(workflowInstanceId);
        var response = await client.Resume(request, cancellationToken);
        var bookmarks = Map(response!.Bookmarks).ToList();

        return new ResumeWorkflowResult(bookmarks);
    }

    /// <inheritdoc />
    public async Task<ICollection<ResumedWorkflow>> ResumeWorkflowsAsync(string activityTypeName, object bookmarkPayload, ResumeWorkflowRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        var hash = _hasher.Hash(activityTypeName, bookmarkPayload);
        var client = _cluster.GetNamedBookmarkGrain(hash);

        var request = new ResolveBookmarksRequest
        {
            ActivityTypeName = activityTypeName,
            CorrelationId = options.CorrelationId.EmptyIfNull()
        };

        var bookmarksResponse = await client.Resolve(request, cancellationToken);
        var bookmarks = bookmarksResponse!.Bookmarks;
        return await ResumeWorkflowsAsync(bookmarks, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TriggerWorkflowsResult> TriggerWorkflowsAsync(string activityTypeName, object bookmarkPayload, TriggerWorkflowsRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        var triggeredWorkflows = new List<TriggeredWorkflow>();
        var hash = _hasher.Hash(activityTypeName, bookmarkPayload);

        // Start new workflows.
        var triggers = await _triggerStore.FindAsync(hash, cancellationToken);

        foreach (var trigger in triggers)
        {
            var definitionId = trigger.WorkflowDefinitionId;
            var startOptions = new StartWorkflowRuntimeOptions(options.CorrelationId, options.Input, VersionOptions.Published, trigger.ActivityId);
            var canStartResult = await CanStartWorkflowAsync(definitionId, startOptions, cancellationToken);
            
            // If we can't start the workflow, don't try it.
            if(!canStartResult.CanStart)
                continue;
            
            var startResult = await StartWorkflowAsync(definitionId, startOptions, cancellationToken);

            triggeredWorkflows.Add(new TriggeredWorkflow(startResult.InstanceId, startResult.Bookmarks));
        }
        
        // Resume existing workflow instances.
        var client = _cluster.GetNamedBookmarkGrain(hash);

        var request = new ResolveBookmarksRequest
        {
            ActivityTypeName = activityTypeName,
            CorrelationId = options.CorrelationId.EmptyIfNull()
        };

        var bookmarksResponse = await client.Resolve(request, cancellationToken);
        var bookmarks = bookmarksResponse!.Bookmarks;

        foreach (var bookmark in bookmarks)
        {
            var workflowInstanceId = bookmark.WorkflowInstanceId;

            var resumeResult = await ResumeWorkflowAsync(
                workflowInstanceId,
                new ResumeWorkflowRuntimeOptions(options.CorrelationId, bookmark.BookmarkId, null, options.Input),
                cancellationToken);

            triggeredWorkflows.Add(new TriggeredWorkflow(workflowInstanceId, resumeResult.Bookmarks));
        }

        return new TriggerWorkflowsResult(triggeredWorkflows);
    }

    /// <inheritdoc />
    public async Task<WorkflowState?> ExportWorkflowStateAsync(string workflowInstanceId, CancellationToken cancellationToken = default)
    {
        var client = _cluster.GetNamedWorkflowGrain(workflowInstanceId);
        var response = await client.ExportState(new ExportWorkflowStateRequest(), cancellationToken);
        var json = response!.SerializedWorkflowState.Text;
        var options = _serializerOptionsProvider.CreatePersistenceOptions();
        var workflowState = JsonSerializer.Deserialize<WorkflowState>(json, options);
        return workflowState;
    }

    /// <inheritdoc />
    public async Task ImportWorkflowStateAsync(WorkflowState workflowState, CancellationToken cancellationToken = default)
    {
        var options = _serializerOptionsProvider.CreatePersistenceOptions();
        var client = _cluster.GetNamedWorkflowGrain(workflowState.Id);
        var json = JsonSerializer.Serialize(workflowState, options);

        var request = new ImportWorkflowStateRequest
        {
            SerializedWorkflowState = new Json
            {
                Text = json
            }
        };

        await client.ImportState(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateBookmarksAsync(UpdateBookmarksContext context, CancellationToken cancellationToken = default)
    {
        await RemoveBookmarksAsync(context.InstanceId, context.Diff.Removed, cancellationToken);
        await StoreBookmarksAsync(context.InstanceId, context.Diff.Added, context.CorrelationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountRunningWorkflowsAsync(CountRunningWorkflowsArgs args, CancellationToken cancellationToken = default)
    {
        var client = _cluster.GetNamedRunningWorkflowsGrain();
        
        var request = new CountRunningWorkflowsRequest
        {
            DefinitionId = args.DefinitionId,
            Version = args.Version ?? -1,
            CorrelationId = args.CorrelationId
        };
        
        var response = await client.Count(request, cancellationToken);
        return response!.Count;
    }
    
    private async Task<ICollection<ResumedWorkflow>> ResumeWorkflowsAsync(IEnumerable<StoredBookmark> bookmarks, ResumeWorkflowRuntimeOptions runtimeOptions, CancellationToken cancellationToken = default)
    {
        var resumedWorkflows = new List<ResumedWorkflow>();

        foreach (var bookmark in bookmarks)
        {
            var workflowInstanceId = bookmark.WorkflowInstanceId;

            var resumeResult = await ResumeWorkflowAsync(
                workflowInstanceId,
                runtimeOptions with { BookmarkId = bookmark.BookmarkId },
                cancellationToken);

            resumedWorkflows.Add(new ResumedWorkflow(workflowInstanceId, resumeResult.Bookmarks));
        }

        return resumedWorkflows;
    }

    private async Task StoreBookmarksAsync(string instanceId, ICollection<Bookmark> bookmarks, string? correlationId, CancellationToken cancellationToken = default)
    {
        var groupedBookmarks = bookmarks.GroupBy(x => x.Hash);

        foreach (var groupedBookmark in groupedBookmarks)
        {
            var bookmarkClient = _cluster.GetNamedBookmarkGrain(groupedBookmark.Key);

            var storeBookmarkRequest = new StoreBookmarksRequest
            {
                WorkflowInstanceId = instanceId,
                CorrelationId = correlationId.EmptyIfNull()
            };

            storeBookmarkRequest.BookmarkIds.AddRange(groupedBookmark.Select(x => x.Id));
            await bookmarkClient.Store(storeBookmarkRequest, cancellationToken);
        }
    }

    private async Task RemoveBookmarksAsync(string instanceId, IEnumerable<Bookmark> bookmarks, CancellationToken cancellationToken = default)
    {
        var groupedBookmarks = bookmarks.GroupBy(x => x.Hash);

        foreach (var groupedBookmark in groupedBookmarks)
        {
            var bookmarkClient = _cluster.GetNamedBookmarkGrain(groupedBookmark.Key);
            await bookmarkClient.RemoveByWorkflow(new RemoveBookmarksByWorkflowRequest
            {
                WorkflowInstanceId = instanceId
            }, cancellationToken);
        }
    }

    private static IEnumerable<Bookmark> Map(IEnumerable<BookmarkDto> source) =>
        source.Select(x =>
            new Bookmark(
                x.Id,
                x.Name,
                x.Hash,
                x.Data.NullIfEmpty(),
                x.ActivityId,
                x.ActivityInstanceId,
                x.AutoBurn,
                x.CallbackMethodName.NullIfEmpty()));
}