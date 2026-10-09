using Wukong.Application;

namespace Wukong.Infrastructure;

public sealed class FileCompanionSessionStateStore : ICompanionSessionStateStore
{
    public const string FileName = "companion-session.json";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileCompanionSessionStateStore(string rootDirectory) =>
        _path = Path.Combine(rootDirectory, FileName);

    public async Task<CompanionSessionState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await AgentJson.ReadAsync<CompanionSessionState>(_path, cancellationToken).ConfigureAwait(false);
            return (state ?? new CompanionSessionState()).Clamp();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(CompanionSessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await AgentJson.WriteAsync(_path, state.Clamp(), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
