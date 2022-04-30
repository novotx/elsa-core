using System.Text.Json;
using Elsa.Activities;
using Elsa.Management.Materializers;
using Elsa.Management.Notifications;
using Elsa.Management.Services;
using Elsa.Mediator.Services;
using Elsa.Persistence.Commands;
using Elsa.Persistence.Entities;
using Elsa.Persistence.Models;
using Elsa.Persistence.Requests;
using Elsa.Serialization;
using Elsa.Services;

namespace Elsa.Management.Implementations
{
    public class WorkflowPublisher : IWorkflowPublisher
    {
        private readonly IMediator _mediator;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly WorkflowSerializerOptionsProvider _workflowSerializerOptionsProvider;
        private readonly ISystemClock _systemClock;

        public WorkflowPublisher(IMediator mediator, IIdentityGenerator identityGenerator, WorkflowSerializerOptionsProvider workflowSerializerOptionsProvider, ISystemClock systemClock)
        {
            _mediator = mediator;
            _identityGenerator = identityGenerator;
            _workflowSerializerOptionsProvider = workflowSerializerOptionsProvider;
            _systemClock = systemClock;
        }

        public WorkflowDefinition New()
        {
            var id = _identityGenerator.GenerateId();
            var definitionId = _identityGenerator.GenerateId();
            const int version = 1;

            return new WorkflowDefinition
            {
                Id = id,
                DefinitionId = definitionId,
                Version = version,
                IsLatest = true,
                IsPublished = false,
                CreatedAt = _systemClock.UtcNow,
                StringData = JsonSerializer.Serialize(new Sequence(), _workflowSerializerOptionsProvider.CreateDefaultOptions()),
                MaterializerName = JsonWorkflowMaterializer.MaterializerName
            };
        }

        public async Task<WorkflowDefinition?> PublishAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            var definition = await _mediator.RequestAsync(new FindWorkflowDefinitionByDefinitionId(definitionId, VersionOptions.Latest), cancellationToken);

            if (definition == null)
                return null;

            return await PublishAsync(definition, cancellationToken);
        }

        public async Task<WorkflowDefinition> PublishAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
        {
            var definitionId = definition.DefinitionId;

            // Reset current latest and published definitions.
            var publishedAndOrLatestWorkflows = await _mediator.RequestAsync(new FindLatestAndPublishedWorkflows(definitionId), cancellationToken);

            foreach (var publishedAndOrLatestWorkflow in publishedAndOrLatestWorkflows)
            {
                publishedAndOrLatestWorkflow.IsPublished = false;
                publishedAndOrLatestWorkflow.IsLatest = false;
                await _mediator.ExecuteAsync(new SaveWorkflowDefinition(publishedAndOrLatestWorkflow), cancellationToken);
            }

            if (definition.IsPublished)
                definition.Version++;
            else
                definition.IsPublished = true;

            definition.IsLatest = true;
            definition = Initialize(definition);

            await _mediator.PublishAsync(new WorkflowDefinitionPublishing(definition), cancellationToken);
            await _mediator.ExecuteAsync(new SaveWorkflowDefinition(definition), cancellationToken);
            await _mediator.PublishAsync(new WorkflowDefinitionPublished(definition), cancellationToken);
            return definition;
        }

        public async Task<WorkflowDefinition?> RetractAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            var definition = await _mediator.RequestAsync(new FindWorkflowDefinitionByDefinitionId(definitionId, VersionOptions.Published), cancellationToken);

            if (definition == null)
                return null;

            return await RetractAsync(definition, cancellationToken);
        }

        public async Task<WorkflowDefinition> RetractAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
        {
            if (!definition.IsPublished)
                throw new InvalidOperationException("Cannot retract an unpublished workflow definition.");

            definition.IsPublished = false;
            definition = Initialize(definition);

            await _mediator.PublishAsync(new WorkflowDefinitionRetracting(definition), cancellationToken);
            await _mediator.ExecuteAsync(new SaveWorkflowDefinition(definition), cancellationToken);
            await _mediator.PublishAsync(new WorkflowDefinitionRetracted(definition), cancellationToken);
            return definition;
        }

        public async Task<WorkflowDefinition?> GetDraftAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            var definition = await _mediator.RequestAsync(new FindWorkflowDefinitionByDefinitionId(definitionId, VersionOptions.Latest), cancellationToken);

            if (definition == null)
                return null;

            if (!definition.IsPublished)
                return definition;

            var draft = definition.ShallowClone();

            draft.Version++;
            draft.Id = _identityGenerator.GenerateId();
            draft.IsLatest = true;

            return draft;
        }

        public async Task<WorkflowDefinition> SaveDraftAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
        {
            var draft = definition;
            var definitionId = definition.DefinitionId;
            var latestVersion = await _mediator.RequestAsync(new FindWorkflowDefinitionByDefinitionId(definitionId, VersionOptions.Latest), cancellationToken);

            if (latestVersion is { IsPublished: true, IsLatest: true })
            {
                latestVersion.IsLatest = false;
                await _mediator.ExecuteAsync(new SaveWorkflowDefinition(latestVersion), cancellationToken);
            }

            draft.IsLatest = true;
            draft.IsPublished = false;
            draft = Initialize(draft);

            await _mediator.ExecuteAsync(new SaveWorkflowDefinition(draft), cancellationToken);
            return draft;
        }

        public async Task DeleteAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            await _mediator.ExecuteAsync(new DeleteWorkflowInstances(definitionId), cancellationToken);
            await _mediator.ExecuteAsync(new DeleteWorkflowDefinition(definitionId), cancellationToken);
        }

        public Task DeleteAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default) => DeleteAsync(definition.DefinitionId, cancellationToken);

        private WorkflowDefinition Initialize(WorkflowDefinition definition)
        {
            if (definition.Id == null!)
                definition.Id = _identityGenerator.GenerateId();

            if (definition.DefinitionId == null!)
                definition.DefinitionId = _identityGenerator.GenerateId();

            if (definition.Version == 0)
                definition.Version = 1;

            return definition;
        }
    }
}