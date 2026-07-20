<?php

declare(strict_types=1);

namespace Clarian\Tests;

use Clarian\HttpTransport;

/**
 * In-memory transport for tests (no network).
 *
 * @phpstan-type Handler callable(RecordedRequest): array{0: int, 1: string|array}
 */
final class FakeTransport implements HttpTransport
{
    /** @var list<RecordedRequest> */
    public array $calls = [];

    /** @var callable(RecordedRequest): array{0: int, 1: string|array} */
    private $handler;

    /**
     * @param callable(RecordedRequest): array{0: int, 1: string|array} $handler
     */
    public function __construct(callable $handler)
    {
        $this->handler = $handler;
    }

    public function request(
        string $method,
        string $url,
        array $headers,
        ?string $body,
        float $timeout,
    ): array {
        $path = parse_url($url, PHP_URL_PATH) ?? '';
        $query = parse_url($url, PHP_URL_QUERY);
        if (is_string($query) && $query !== '') {
            $path .= '?' . $query;
        }

        $rec = new RecordedRequest($method, $path, $headers, $body, $timeout, $url);
        $this->calls[] = $rec;

        [$status, $resp] = ($this->handler)($rec);
        if (is_array($resp)) {
            $resp = json_encode($resp, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
        }

        return [(int) $status, (string) $resp];
    }

    public function last(): RecordedRequest
    {
        if ($this->calls === []) {
            throw new \RuntimeException('no requests recorded');
        }

        return $this->calls[array_key_last($this->calls)];
    }
}

final class RecordedRequest
{
    /**
     * @param array<string, string> $headers
     */
    public function __construct(
        public readonly string $method,
        public readonly string $path,
        public readonly array $headers,
        public readonly ?string $body,
        public readonly float $timeout,
        public readonly string $url,
    ) {
    }
}
