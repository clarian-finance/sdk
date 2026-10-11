<?php

declare(strict_types=1);

namespace Clarian;

/** A network failure (connection, DNS, timeout). Retried like 502/503/504. */
final class TransportException extends \RuntimeException
{
}

/** Swap in your own HTTP stack (tests, proxies). Never throw on HTTP status; throw TransportException on network errors. */
interface HttpTransport
{
    /**
     * @param array<string, string> $headers
     * @return array{0: int, 1: string, 2: array<string, string>} status, body, lowercase response headers
     */
    public function send(string $method, string $url, array $headers, ?string $body, float $timeout): array;
}

final class CurlTransport implements HttpTransport
{
    public function send(string $method, string $url, array $headers, ?string $body, float $timeout): array
    {
        $ch = curl_init($url);
        $respHeaders = [];
        $lines = [];
        foreach ($headers as $k => $v) {
            $lines[] = "$k: $v";
        }
        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER => $lines,
            CURLOPT_TIMEOUT_MS => (int) ($timeout * 1000),
            CURLOPT_HEADERFUNCTION => function ($_ch, string $line) use (&$respHeaders): int {
                $parts = explode(':', $line, 2);
                if (count($parts) === 2) {
                    $respHeaders[strtolower(trim($parts[0]))] = trim($parts[1]);
                }
                return strlen($line);
            },
        ]);
        if ($body !== null) {
            curl_setopt($ch, CURLOPT_POSTFIELDS, $body);
        }
        $resp = curl_exec($ch);
        if ($resp === false) {
            throw new TransportException('clarian: request failed: ' . curl_error($ch));
        }
        return [(int) curl_getinfo($ch, CURLINFO_HTTP_CODE), (string) $resp, $respHeaders];
    }
}

/** Builds and sends `/v1/{live|test}/<path>` requests and unwraps `{data}`. */
final class Http
{
    private const RETRY_STATUS = [429, 502, 503, 504];

    public readonly string $environment;
    private readonly HttpTransport $transport;
    /** @var callable(float): void seconds */
    private $sleep;

    public function __construct(
        private readonly string $apiKey,
        private readonly string $workspaceId,
        private readonly string $baseUrl = 'https://api.clarian.finance',
        private readonly float $timeout = 30.0,
        public readonly ?string $subaccountId = null,
        private readonly int $maxRetries = 2,
        ?HttpTransport $transport = null,
        ?callable $sleep = null,
    ) {
        $this->environment = self::resolveEnvironment($apiKey);
        $this->transport = $transport ?? new CurlTransport();
        $this->sleep = $sleep ?? static fn (float $s) => usleep((int) ($s * 1_000_000));
    }

    public static function resolveEnvironment(string $apiKey): string
    {
        if (str_starts_with($apiKey, 'cl_live_sk_')) {
            return 'live';
        }
        if (str_starts_with($apiKey, 'cl_test_sk_')) {
            return 'sandbox';
        }
        throw new \InvalidArgumentException('Invalid API key format: must start with cl_live_sk_ or cl_test_sk_');
    }

    public function withSubaccount(string $subaccountId): self
    {
        return new self($this->apiKey, $this->workspaceId, $this->baseUrl, $this->timeout, $subaccountId, $this->maxRetries, $this->transport, $this->sleep);
    }

    /**
     * @param array<string, mixed> $query
     * @return mixed the `data` member of the reply (decoded as arrays)
     */
    public function request(string $method, string $path, array $query = [], mixed $body = null, ?string $idempotencyKey = null, ?string $otpCode = null): mixed
    {
        $env = $this->environment === 'live' ? 'live' : 'test';
        $q = array_filter($query, static fn ($v) => is_string($v) || is_int($v));
        $url = rtrim($this->baseUrl, '/') . "/v1/$env/$path" . ($q === [] ? '' : '?' . http_build_query($q, '', '&', PHP_QUERY_RFC3986));
        $headers = ['Authorization' => 'Bearer ' . $this->apiKey, 'X-Workspace-Id' => $this->workspaceId];
        if ($body !== null) {
            $headers['Content-Type'] = 'application/json';
        }
        if ($this->subaccountId !== null && $this->subaccountId !== '') {
            $headers['X-Subaccount-Id'] = $this->subaccountId;
        }
        if ($idempotencyKey !== null && $idempotencyKey !== '') {
            $headers['Idempotency-Key'] = $idempotencyKey;
        }
        if ($otpCode !== null && $otpCode !== '') {
            $headers['X-OTP-Code'] = $otpCode;
        }
        $json = $body === null ? null : json_encode(self::normalize($body), JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
        // Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
        $retryable = $method === 'GET' || $method === 'DELETE' || ($idempotencyKey !== null && $idempotencyKey !== '');
        [$status, $text] = $this->sendWithRetry($method, $url, $headers, $json, $retryable ? $this->maxRetries : 0);

        $obj = $text === '' ? [] : json_decode($text, true, 512, JSON_BIGINT_AS_STRING);
        if ($text !== '' && json_last_error() !== JSON_ERROR_NONE) {
            $obj = ['error' => 'invalid_json_response', 'message' => substr($text, 0, 500)];
        }
        $obj = is_array($obj) ? $obj : [];
        if ($status < 200 || $status > 299) {
            throw new ClarianError($status, $obj);
        }
        return array_key_exists('data', $obj) ? $obj['data'] : $obj;
    }

    /** @return array{0: int, 1: string} */
    private function sendWithRetry(string $method, string $url, array $headers, ?string $body, int $retries): array
    {
        for ($attempt = 0; ; $attempt++) {
            try {
                [$status, $text, $respHeaders] = $this->transport->send($method, $url, $headers, $body, $this->timeout);
                if (!in_array($status, self::RETRY_STATUS, true) || $attempt >= $retries) {
                    return [$status, $text];
                }
                $retryAfter = $respHeaders['retry-after'] ?? null;
            } catch (TransportException $e) {
                if ($attempt >= $retries) {
                    throw $e;
                }
                $retryAfter = null;
            }
            ($this->sleep)(is_numeric($retryAfter) && (float) $retryAfter >= 0
                ? min((float) $retryAfter, 30.0)
                : mt_rand() / mt_getrandmax() * min(8.0, 0.5 * 2 ** $attempt));
        }
    }

    /** Amounts (`*_minor`) go on the wire as integer strings; floats are refused, big integers accepted. */
    private static function normalize(mixed $v, string $key = ''): mixed
    {
        if (is_array($v)) {
            $out = [];
            foreach ($v as $k => $item) {
                $out[$k] = self::normalize($item, (string) $k);
            }
            return $out;
        }
        if ($v instanceof \GMP) {
            return gmp_strval($v);
        }
        if (class_exists(\BcMath\Number::class) && $v instanceof \BcMath\Number) {
            if ($v->scale !== 0) {
                throw new \InvalidArgumentException("$key must be an integer amount in minor units");
            }
            return (string) $v;
        }
        if (str_ends_with($key, '_minor')) {
            if (is_float($v) || (is_string($v) && !preg_match('/^-?\d+$/', $v))) {
                throw new \InvalidArgumentException("$key must be an integer amount in minor units (string, int or big integer), never a float");
            }
            return (string) $v;
        }
        return $v;
    }
}
