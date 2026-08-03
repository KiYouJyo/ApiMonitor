using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Secrets => _secrets;

    public bool Contains(string accountId) => _secrets.ContainsKey(accountId);

    public Task<string?> GetAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_secrets.TryGetValue(accountId, out var secret) ? secret : null);
    }

    public Task SetAsync(string accountId, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets[accountId] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.Remove(accountId);
        return Task.CompletedTask;
    }
}
