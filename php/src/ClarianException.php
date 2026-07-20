<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Thrown for any non-2xx HTTP response from the Clarian API.
 *
 * Exception message truncates the raw body to 500 characters so logs stay safe
 * when bodies echo request fields (PIX keys, documents). Full body remains on
 * {@see $body}.
 *
 * Public fields (also via property access): status, code, message, meta, body.
 * Note: Exception already defines protected $code / $message, so those contract
 * fields are exposed through {@see __get()}.
 */
final class ClarianException extends \RuntimeException
{
    public readonly int $status;

    /** @var array<string, mixed> */
    public readonly array $meta;

    /** Full raw response body (untruncated). */
    public readonly string $body;

    private readonly string $apiCode;
    private readonly string $apiMessage;

    public function __construct(int $status, ?string $body = null)
    {
        $raw = $body ?? '';
        $code = '';
        $detail = '';
        $meta = [];

        if ($raw !== '') {
            try {
                $parsed = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
                if (is_array($parsed)) {
                    if (array_key_exists('error', $parsed) && $parsed['error'] !== null) {
                        $code = (string) $parsed['error'];
                    }
                    if (array_key_exists('detail', $parsed) && $parsed['detail'] !== null) {
                        $detail = (string) $parsed['detail'];
                    }
                    foreach ($parsed as $k => $v) {
                        if ($k === 'error' || $k === 'detail') {
                            continue;
                        }
                        $meta[(string) $k] = $v;
                    }
                }
            } catch (\JsonException) {
                // non-JSON body
            }
        }

        $truncated = strlen($raw) <= 500 ? $raw : substr($raw, 0, 500);
        if ($detail !== '') {
            $message = $detail;
        } elseif ($code !== '') {
            $message = $code;
        } else {
            $message = $truncated;
        }

        if ($code !== '') {
            $excMsg = "HTTP {$status}: {$code}";
        } elseif ($truncated !== '') {
            $excMsg = "HTTP {$status}: {$truncated}";
        } else {
            $excMsg = "HTTP {$status}";
        }

        parent::__construct($excMsg);
        $this->status = $status;
        $this->apiCode = $code;
        $this->apiMessage = $message;
        $this->meta = $meta;
        $this->body = $raw;
    }

    public function __get(string $name): mixed
    {
        return match ($name) {
            'code' => $this->apiCode,
            'message' => $this->apiMessage,
            default => throw new \Error('Undefined property ClarianException::$' . $name),
        };
    }

    public function __isset(string $name): bool
    {
        return $name === 'code' || $name === 'message';
    }
}
