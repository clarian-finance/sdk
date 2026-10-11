<?php

declare(strict_types=1);

namespace Clarian;

/** Every non-2xx reply. `code` is the API's stable `error` string, e.g. `insufficient_balance`. */
final class ClarianError extends \RuntimeException
{
    public readonly int $status;
    public readonly string $errorCode;
    /** @var array<string, mixed> Remaining reply fields (besides error and message). */
    public readonly array $meta;

    /** @param array<string, mixed> $body */
    public function __construct(int $status, array $body)
    {
        $code = is_string($body['error'] ?? null) ? $body['error'] : 'unknown_error';
        parent::__construct(is_string($body['message'] ?? null) ? $body['message'] : $code, $status);
        $this->status = $status;
        $this->errorCode = $code;
        unset($body['error'], $body['message']);
        $this->meta = $body;
    }
}
