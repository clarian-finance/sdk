<?php

declare(strict_types=1);

namespace Clarian;

/** Verifies Clarian webhook deliveries. */
final class Webhook
{
    public const DEFAULT_TOLERANCE_SECONDS = 300;

    /** Signs "<timestamp>.<body>" exactly as Clarian does. For tests of your own handler. */
    public static function sign(string $body, string $timestamp, string $secret): string
    {
        return hash_hmac('sha256', "$timestamp.$body", $secret);
    }

    public static function verifySignature(string $body, string $signature, string $timestamp, string $secret, int $toleranceSeconds = self::DEFAULT_TOLERANCE_SECONDS): bool
    {
        // An unparseable timestamp fails closed.
        $sentAt = self::parseIso8601($timestamp);
        if ($sentAt === null || abs(microtime(true) - $sentAt) > $toleranceSeconds) {
            return false;
        }
        return hash_equals(self::sign($body, $timestamp, $secret), $signature);
    }

    /**
     * Verifies a delivery and returns its parsed body {id, type, created_at, data}. `$rawBody` must be the
     * exact bytes received. Throws when the signature or timestamp is bad. Dedupe on `id`.
     *
     * @param array<string, string|list<string>> $headers e.g. getallheaders() or a PSR-7 getHeaders()
     */
    public static function constructEvent(string $rawBody, array $headers, string $secret, int $toleranceSeconds = self::DEFAULT_TOLERANCE_SECONDS): array
    {
        $lower = array_change_key_case($headers, CASE_LOWER);
        $get = static function (string $name) use ($lower): string {
            $v = $lower[$name] ?? '';
            return is_array($v) ? (string) ($v[0] ?? '') : (string) $v;
        };
        if (!self::verifySignature($rawBody, $get('x-clarian-signature'), $get('x-clarian-timestamp'), $secret, $toleranceSeconds)) {
            throw new \UnexpectedValueException('Invalid webhook signature or timestamp outside tolerance');
        }
        return json_decode($rawBody, true, 512, JSON_THROW_ON_ERROR | JSON_BIGINT_AS_STRING);
    }

    private static function parseIso8601(string $ts): ?float
    {
        foreach (['Y-m-d\TH:i:s.uP', 'Y-m-d\TH:i:s.vP', 'Y-m-d\TH:i:sP'] as $fmt) {
            $d = \DateTimeImmutable::createFromFormat($fmt, $ts);
            $e = \DateTimeImmutable::getLastErrors();
            if ($d !== false && ($e === false || $e['warning_count'] + $e['error_count'] === 0)) {
                return (float) $d->format('U.u');
            }
        }
        return null;
    }
}
