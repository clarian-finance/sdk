namespace Clarian;

/// <summary>On-chain wallets (crypto, separate from the BRL ledger).</summary>
public sealed class WalletsService
{
    private readonly ClarianClient _client;

    internal WalletsService(ClarianClient client) => _client = client;

    /// <summary>GET /wallets with optional network filter.</summary>
    public async Task<IReadOnlyList<Wallet>> ListAsync(
        string? network = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/wallets";
        if (!string.IsNullOrEmpty(network))
            path += "?network=" + Uri.EscapeDataString(network);

        var res = await _client.RequestAsync<WalletsListResponse>(
            HttpMethod.Get,
            path,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Wallets ?? new List<Wallet>();
    }

    /// <summary>GET /wallets/{id}/balance.</summary>
    public Task<WalletBalanceResponse> RetrieveBalanceAsync(
        string walletId,
        CancellationToken cancellationToken = default) =>
        _client.RequestAsync<WalletBalanceResponse>(
            HttpMethod.Get,
            "/wallets/" + ClarianClient.PathEscape(walletId) + "/balance",
            null,
            null,
            cancellationToken);
}
