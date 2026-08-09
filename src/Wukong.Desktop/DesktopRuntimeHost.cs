using System.IO;
using System.Collections.ObjectModel;
using Wukong.Application;
using Wukong.Contracts;
using Wukong.Domain;
using Wukong.Infrastructure;

namespace Wukong.Desktop;

public sealed class DesktopRuntimeHost
{
    private readonly BehaviorRequestService _behavior;
    private readonly FakeModelClient _model = new();
    private readonly RollingFileLogStore _logs = RollingFileLogStore.CreateDefault();
    private readonly InMemoryEventStore _productionEvents = new(RuntimeMode.Production);
    private readonly InMemoryMemoryCandidateStore _productionMemory = new(RuntimeMode.Production);
    private RuntimeState _state = RuntimeState.InitialProne();

    public DesktopRuntimeHost()
    {
        var registryPath = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "contracts", "runtime", "asset-registry.json");
        var registry = new ProductionRuntimeRegistryLoader().Load(registryPath);
        _behavior = new BehaviorRequestService(
            new RuntimeRegistryAssetCatalog(registry),
            new AnimationLifecycleOrchestrator(new NoopAnimationPlayer()),
            _productionEvents,
            _productionMemory);
    }

    public ObservableCollection<string> TraceLines { get; } = new();

    public Task RecordInputAsync(InputEvent inputEvent)
    {
        AddTrace($"input:{inputEvent.Kind} source:{inputEvent.Source}");
        return Task.CompletedTask;
    }

    public async Task SubmitOwnerInputAsync(InputEvent inputEvent)
    {
        AddTrace($"input:{inputEvent.Kind} source:{inputEvent.Source}");
        await SubmitRequestAsync(BehaviorRequest.FromIntent(
            BehaviorRequestSource.OwnerUi,
            RuntimeMode.Production,
            inputEvent.OccurredAt,
            new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch")));
    }

    public Task SubmitContextMenuIntentAsync(SemanticIntent intent) =>
        SubmitRequestAsync(BehaviorRequest.FromIntent(
            BehaviorRequestSource.ContextMenu,
            RuntimeMode.Production,
            DateTimeOffset.Now,
            intent));

    public Task SubmitAutonomousTickAsync() =>
        SubmitRequestAsync(BehaviorRequest.FromIntent(
            BehaviorRequestSource.AutonomousTick,
            RuntimeMode.Production,
            DateTimeOffset.Now,
            new SemanticIntent(SemanticIntentKind.AutonomousRest, "wk.core.prone_idle"),
            priority: -10));

    public async Task SubmitFakeModelMessageAsync(string text)
    {
        var response = await _model.SendAsync(text);
        AddTrace($"model_reply:{response.Reply}");
        _logs.Append(RuntimeMode.Production, "model_response", new { response.Reply, response.Intent, response.MemoryCandidate });
        if (!response.RespectsModelBoundary)
        {
            AddTrace("model_boundary:rejected");
            return;
        }
        if (response.Intent is null || response.Intent.Kind == SemanticIntentKind.None)
            return;

        await SubmitRequestAsync(BehaviorRequest.FromIntent(
            BehaviorRequestSource.Model,
            RuntimeMode.Production,
            DateTimeOffset.Now,
            response.Intent));
    }

    private async Task SubmitRequestAsync(BehaviorRequest request)
    {
        var result = await _behavior.SubmitAsync(request, _state);
        if (result.Outcome is not null)
            _state = _state.Apply(result.Outcome);

        AddTrace($"request:{request.Source}/{request.Intent.Kind} eligibility:{result.Eligibility.Disposition}:{result.Eligibility.ReasonCode}");
        AddTrace($"arbitration:{result.Arbitration.Disposition}:{result.Arbitration.ReasonCode}");
        if (result.Outcome is not null)
            AddTrace($"outcome:{result.Outcome.Status}:{result.Outcome.BehaviorId}");
        _logs.Append(request.RuntimeMode, "behavior_result", new
        {
            request.Source,
            request.Intent,
            result.Eligibility,
            result.Arbitration,
            result.Outcome,
            result.Trace
        });
    }

    private void AddTrace(string line)
    {
        TraceLines.Add($"{DateTimeOffset.Now:HH:mm:ss} {SensitiveDataRedactor.Redact(line)}");
        while (TraceLines.Count > 200)
            TraceLines.RemoveAt(0);
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "contracts")))
                return current.FullName;
            current = current.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
