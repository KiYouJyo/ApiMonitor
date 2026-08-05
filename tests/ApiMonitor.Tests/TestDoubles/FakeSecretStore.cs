using ApiMonitor.Services;
using ApiMonitor.Models;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Secrets => _secrets;

    public bool Contains(string accountId) => _secrets.ContainsKey(accountId);

    public Task<string?> GetAsync(
        string accountId,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = Key(accountId, slot);
        return Task.FromResult(_secrets.TryGetValue(key, out var secret) ? secret : null);
    }

    public Task SetAsync(
        string accountId,
        string secret,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets[Key(accountId, slot)] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (string slot in CredentialSlots.All)
        {
            _secrets.Remove(Key(accountId, slot));
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetPresentSlots(string accountId) =>
        CredentialSlots.All
            .Where(slot => _secrets.ContainsKey(Key(accountId, slot)))
            .ToList();

    private static string Key(string accountId, string slot) =>
        string.Equals(slot, CredentialSlots.Primary, StringComparison.Ordinal)
            ? accountId
            : accountId + "::" + slot;
}
