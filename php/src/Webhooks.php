<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Webhook signature helpers.
 *
 * Signature is hex(HMAC-SHA256(secret, timestamp + "." + body)) using the full
 * whsec_… secret (prefix included; do not strip or base64-decode it).
 */
final class Webhooks
{
    public const HEADER_SIGNATURE = 'X-Clarian-Signature';
    public const HEADER_TIMESTAMP = 'X-Clarian-Timestamp';
    public const HEADER_EVENT = 'X-Clarian-Event';
    public const HEADER_DELIVERY_ID = 'X-Clarian-Delivery-Id';
    public const HEADER_IDEMPOTENCY_KEY = 'X-Clarian-Idempotency-Key';
    public const HEADER_ATTEMPT = 'X-Clarian-Attempt';

    public const DEFAULT_TOLERANCE_SECONDS = 300;

    private function __construct()
    {
    }

    /**
     * Return hex HMAC-SHA256 of "{timestamp}.{body}" using secret.
     */
    public static function signPayload(string $secret, string $timestamp, string $payload): string
    {
        return hash_hmac('sha256', $timestamp . '.' . $payload, $secret);
    }

    /**
     * Verify HMAC-SHA256 signature and timestamp freshness.
     *
     * Returns false on missing inputs, bad signature, or stale/future timestamp.
     */
    public static function verifySignature(
        string $payload,
        string $timestamp,
        string $signature,
        string $secret,
        int $toleranceSeconds = self::DEFAULT_TOLERANCE_SECONDS,
    ): bool {
        if ($secret === '' || $signature === '' || $timestamp === '') {
            return false;
        }

        try {
            $eventTs = self::parseTimestampEpoch($timestamp);
        } catch (\InvalidArgumentException) {
            return false;
        }

        $age = abs(time() - $eventTs);
        if ($age > $toleranceSeconds) {
            return false;
        }

        $expected = self::signPayload($secret, $timestamp, $payload);

        return hash_equals($expected, $signature);
    }

    /**
     * Read Clarian delivery headers (case-insensitive).
     *
     * Keys match the TypeScript SDK: signature, timestamp, event, deliveryId,
     * idempotencyKey, attempt. Missing values become empty strings.
     *
     * @param array<string, mixed> $headers
     * @return array{
     *   signature: string,
     *   timestamp: string,
     *   event: string,
     *   deliveryId: string,
     *   idempotencyKey: string,
     *   attempt: string
     * }
     */
    public static function extractHeaders(array $headers): array
    {
        $lower = [];
        foreach ($headers as $key => $value) {
            $lower[strtolower((string) $key)] = self::firstValue($value);
        }

        return [
            'signature' => $lower[strtolower(self::HEADER_SIGNATURE)] ?? '',
            'timestamp' => $lower[strtolower(self::HEADER_TIMESTAMP)] ?? '',
            'event' => $lower[strtolower(self::HEADER_EVENT)] ?? '',
            'deliveryId' => $lower[strtolower(self::HEADER_DELIVERY_ID)] ?? '',
            'idempotencyKey' => $lower[strtolower(self::HEADER_IDEMPOTENCY_KEY)] ?? '',
            'attempt' => $lower[strtolower(self::HEADER_ATTEMPT)] ?? '',
        ];
    }

    private static function firstValue(mixed $value): string
    {
        if ($value === null) {
            return '';
        }
        if (is_array($value)) {
            if ($value === []) {
                return '';
            }
            $first = reset($value);

            return $first === null ? '' : (string) $first;
        }

        return (string) $value;
    }

    private static function parseTimestampEpoch(string $timestamp): int
    {
        // Unix epoch (integer or float string)
        if (is_numeric($timestamp)) {
            return (int) $timestamp;
        }

        // ISO 8601 / RFC3339 (with or without fractional seconds, Z or offset)
        $normalized = str_replace('Z', '+00:00', $timestamp);
        $dt = \DateTimeImmutable::createFromFormat(
            \DateTimeInterface::RFC3339_EXTENDED,
            $normalized,
        );
        if ($dt === false) {
            $dt = \DateTimeImmutable::createFromFormat(
                \DateTimeInterface::RFC3339,
                $normalized,
            );
        }
        if ($dt === false) {
            $dt = date_create_immutable($timestamp);
        }
        if ($dt === false) {
            throw new \InvalidArgumentException('invalid webhook timestamp: ' . $timestamp);
        }

        return $dt->getTimestamp();
    }
}
