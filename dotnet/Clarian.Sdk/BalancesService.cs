namespace Clarian;

/// <summary>Workspace ledger balances.</summary>
public sealed class BalancesService
{
    private readonly ClarianClient _client;

    internal BalancesService(ClarianClient client) => _client = client;

    /// <summary>GET /account/balances.</summary>
    public async Task<IReadOnlyList<Balance>> ListAsync(CancellationToken cancellationToken = default)
    {
        var res = await _client.RequestAsync<BalancesResponse>(
            HttpMethod.Get,
            "/account/balances",
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Balances ?? new List<Balance>();
    }
}
