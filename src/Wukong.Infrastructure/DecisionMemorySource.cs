using Wukong.Application;

namespace Wukong.Infrastructure;

public sealed class LocalPetDecisionMemorySource : IPetDecisionMemorySource
{
    private readonly IAgentMemoryConfigurationStore _configuration;
    private readonly IConversationMemoryStore _conversationMemory;
    private readonly IAlbumMemoryRetriever _albumMemory;
    private readonly PetDecisionMemoryProjector _projector;
    private readonly Func<DateTimeOffset> _now;

    public LocalPetDecisionMemorySource(
        IAgentMemoryConfigurationStore configuration,
        IConversationMemoryStore conversationMemory,
        IAlbumMemoryRetriever albumMemory,
        PetDecisionMemoryProjector? projector = null,
        Func<DateTimeOffset>? now = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _conversationMemory = conversationMemory ?? throw new ArgumentNullException(nameof(conversationMemory));
        _albumMemory = albumMemory ?? throw new ArgumentNullException(nameof(albumMemory));
        _projector = projector ?? new PetDecisionMemoryProjector();
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<PetDecisionMemoryProfile> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configuration.LoadAsync(cancellationToken);
        var evidence = new List<PetMemoryEvidence>();

        if (configuration.UseLongTermMemory)
        {
            var memories = await _conversationMemory.ReadAsync(cancellationToken);
            evidence.AddRange(memories
                .Where(item => item.Status == ConversationMemoryStatus.Confirmed)
                .OrderByDescending(item => item.CreatedAt)
                .Take(100)
                .Select(item => new PetMemoryEvidence(
                    item.Id.ToString("N"),
                    PetMemoryEvidenceSource.ConfirmedConversation,
                    Clip(item.Content, 2000))));
        }

        if (configuration.UseAlbumMemory)
        {
            foreach (var query in PetDecisionMemoryProjector.AlbumQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = await _albumMemory.SearchAsync(query.Value, 5, cancellationToken);
                evidence.AddRange(matches.Select(item => new PetMemoryEvidence(
                    item.MemoryId,
                    PetMemoryEvidenceSource.AlbumDescription,
                    Clip($"{item.AlbumTitle}\n{item.Excerpt}", 2400),
                    query.Key)));
            }
        }

        return _projector.Project(evidence, _now());
    }

    private static string Clip(string? value, int maximum)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
