<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Injectable HTTP transport for the Clarian client.
 *
 * Implementations must return [statusCode, responseBody] and never throw on
 * HTTP error statuses (the client maps those to ClarianException).
 */
interface HttpTransport
{
    /**
     * @param array<string, string> $headers
     * @return array{0: int, 1: string}
     */
    public function request(
        string $method,
        string $url,
        array $headers,
        ?string $body,
        float $timeout,
    ): array;
}
