<?php

declare(strict_types=1);

namespace Clarian;

use Clarian\Resources as R;

final class Clarian
{
    public readonly string $environment;
    public readonly R\Balances $balances;
    public readonly R\Wallets $wallets;
    public readonly R\Transactions $transactions;
    public readonly R\Pix $pix;
    public readonly R\Conversions $conversions;
    public readonly R\Crypto $crypto;
    public readonly R\Trading $trading;
    public readonly R\Customers $customers;
    public readonly R\Kyc $kyc;
    public readonly R\Whitelist $whitelist;
    public readonly R\BankAccounts $bankAccounts;
    public readonly R\Webhooks $webhooks;
    public readonly R\PaymentLinks $paymentLinks;
    public readonly R\Checkout $checkout;
    public readonly R\SubAccounts $subaccounts;
    public readonly R\ApiKeys $apiKeys;
    public readonly R\Settings $settings;
    public readonly R\Notifications $notifications;
    public readonly R\Team $team;
    public readonly R\Workflows $workflows;
    public readonly R\Approvals $approvals;
    public readonly R\Treasury $treasury;
    private readonly Http $http;

    /**
     * @param string $apiKey cl_live_sk_... (live) or cl_test_sk_... (sandbox). The key picks the environment.
     * @param string $baseUrl API origin.
     * @param float $timeout Per-request timeout in seconds.
     * @param string|null $subaccountId Act on one child workspace. Same as forSubaccount().
     * @param int $maxRetries Retries for 429/502/503/504 and network errors on reads, deletes and idempotent creates.
     * @param callable(float): void|null $sleep Backoff sleeper in seconds (tests).
     */
    public function __construct(
        string $apiKey,
        string $workspaceId,
        string $baseUrl = 'https://api.clarian.finance',
        float $timeout = 30.0,
        ?string $subaccountId = null,
        int $maxRetries = 2,
        ?HttpTransport $transport = null,
        ?callable $sleep = null,
        ?Http $http = null,
    ) {
        if ($apiKey === '') {
            throw new \InvalidArgumentException('apiKey is required');
        }
        if ($workspaceId === '') {
            throw new \InvalidArgumentException('workspaceId is required');
        }
        $h = $this->http = $http ?? new Http($apiKey, $workspaceId, $baseUrl, $timeout, $subaccountId, $maxRetries, $transport, $sleep);
        $this->environment = $h->environment;
        $this->balances = new R\Balances($h);
        $this->wallets = new R\Wallets($h);
        $this->transactions = new R\Transactions($h);
        $this->pix = new R\Pix($h);
        $this->conversions = new R\Conversions($h);
        $this->crypto = new R\Crypto($h);
        $this->trading = new R\Trading($h);
        $this->customers = new R\Customers($h);
        $this->kyc = new R\Kyc($h);
        $this->whitelist = new R\Whitelist($h);
        $this->bankAccounts = new R\BankAccounts($h);
        $this->webhooks = new R\Webhooks($h);
        $this->paymentLinks = new R\PaymentLinks($h);
        $this->checkout = new R\Checkout($h);
        $this->subaccounts = new R\SubAccounts($h);
        $this->apiKeys = new R\ApiKeys($h);
        $this->settings = new R\Settings($h);
        $this->notifications = new R\Notifications($h);
        $this->team = new R\Team($h);
        $this->workflows = new R\Workflows($h);
        $this->approvals = new R\Approvals($h);
        $this->treasury = new R\Treasury($h);
    }

    /** Checks the key and workspace. */
    public function ping(): array
    {
        return $this->http->request('GET', 'ping');
    }

    /** A client that acts on one child workspace (sends X-Subaccount-Id). */
    public function forSubaccount(string $subaccountId): self
    {
        if (trim($subaccountId) === '') {
            throw new \InvalidArgumentException('subaccountId is required');
        }
        return new self('-', '-', http: $this->http->withSubaccount($subaccountId));
    }

    /** var_dump / print_r never show the API key. */
    public function __debugInfo(): array
    {
        return ['environment' => $this->environment, 'http' => $this->http];
    }
}
