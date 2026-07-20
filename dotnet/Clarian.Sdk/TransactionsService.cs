using System.Text;

namespace Clarian;

/// <summary>Ledger transactions.</summary>
public sealed class TransactionsService
{
    private readonly ClarianClient _client;

    internal TransactionsService(ClarianClient client) => _client = client;

    /// <summary>GET /transactions with optional type/status/limit filters.</summary>
    public async Task<IReadOnlyList<Transaction>> ListAsync(
        ListTransactionsParams? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildPath(parameters);
        var res = await _client.RequestAsync<TransactionsListResponse>(
            HttpMethod.Get,
            path,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Transactions ?? new List<Transaction>();
    }

    /// <summary>GET /transactions/{id}.</summary>
    public async Task<Transaction> RetrieveAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        var res = await _client.RequestAsync<TransactionResponse>(
            HttpMethod.Get,
            "/transactions/" + ClarianClient.PathEscape(transactionId),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Transaction;
    }

    private static string BuildPath(ListTransactionsParams? parameters)
    {
        if (parameters is null)
            return "/transactions";

        var q = new List<string>();
        if (!string.IsNullOrEmpty(parameters.Type))
            q.Add("type=" + Uri.EscapeDataString(parameters.Type));
        if (!string.IsNullOrEmpty(parameters.Status))
            q.Add("status=" + Uri.EscapeDataString(parameters.Status));
        if (parameters.Limit is > 0)
            q.Add("limit=" + parameters.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (q.Count == 0)
            return "/transactions";

        var sb = new StringBuilder("/transactions?");
        sb.Append(string.Join("&", q));
        return sb.ToString();
    }
}
