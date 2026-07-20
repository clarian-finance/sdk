<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Clarian Finance API client.
 *
 * Environment is inferred from the API key prefix:
 * cl_test_sk_ → sandbox (BaseUrlTest), otherwise live (BaseUrlLive).
 */
final class Client
{
    public const BASE_URL_LIVE = 'https://api.clarian.finance/functions/v1/api-gateway/live';
    public const BASE_URL_TEST = 'https://api.clarian.finance/functions/v1/api-gateway/test';

    public const DEFAULT_TIMEOUT = 30.0;

    public readonly string $apiKey;
    public readonly string $workspaceId;
    public readonly string $baseUrl;
    public readonly float $timeout;

    public readonly CashIn $cashIn;
    public readonly CashOut $cashOut;
    public readonly Balances $balances;
    public readonly Transactions $transactions;
    public readonly Wallets $wallets;
    public readonly WebhooksService $webhooks;
    public readonly Sandbox $sandbox;

    private HttpTransport $transport;

    public function __construct(
        string $apiKey,
        string $workspaceId,
        ?string $baseUrl = null,
        ?float $timeout = null,
        ?HttpTransport $transport = null,
    ) {
        if ($apiKey === '') {
            throw new \InvalidArgumentException('apiKey is required');
        }
        if ($workspaceId === '') {
            throw new \InvalidArgumentException('workspaceId is required');
        }

        $this->apiKey = $apiKey;
        $this->workspaceId = $workspaceId;

        $inferred = str_starts_with($apiKey, 'cl_test_sk_')
            ? self::BASE_URL_TEST
            : self::BASE_URL_LIVE;
        $this->baseUrl = rtrim($baseUrl ?? $inferred, '/');
        $this->timeout = $timeout ?? self::DEFAULT_TIMEOUT;
        $this->transport = $transport ?? new CurlTransport();

        $this->cashIn = new CashIn($this);
        $this->cashOut = new CashOut($this);
        $this->balances = new Balances($this);
        $this->transactions = new Transactions($this);
        $this->wallets = new Wallets($this);
        $this->webhooks = new WebhooksService($this);
        $this->sandbox = new Sandbox($this);
    }

    /**
     * GET /ping: credential and workspace probe.
     *
     * @return array<string, mixed>
     */
    public function ping(): array
    {
        return $this->request('GET', '/ping');
    }

    /**
     * @param array<string, mixed>|null $body
     * @return array<string, mixed>
     */
    public function request(
        string $method,
        string $path,
        ?array $body = null,
        ?string $idempotencyKey = null,
    ): array {
        $url = $this->baseUrl . $path;
        $headers = [
            'Authorization' => 'Bearer ' . $this->apiKey,
            'X-Workspace-Id' => $this->workspaceId,
        ];

        $rawBody = null;
        if ($body !== null) {
            $rawBody = json_encode($body, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
            $headers['Content-Type'] = 'application/json';
        }
        if ($idempotencyKey !== null && $idempotencyKey !== '') {
            $headers['Idempotency-Key'] = $idempotencyKey;
        }

        [$status, $respBody] = $this->transport->request(
            $method,
            $url,
            $headers,
            $rawBody,
            $this->timeout,
        );

        if ($status < 200 || $status >= 300) {
            throw new ClarianException($status, $respBody);
        }

        if ($respBody === '') {
            return [];
        }

        try {
            $parsed = json_decode($respBody, true, 512, JSON_THROW_ON_ERROR);
        } catch (\JsonException) {
            throw new ClarianException($status, $respBody);
        }

        if (is_array($parsed)) {
            return $parsed;
        }

        return ['data' => $parsed];
    }

    public static function pathEscape(string $segment): string
    {
        return rawurlencode($segment);
    }
}
