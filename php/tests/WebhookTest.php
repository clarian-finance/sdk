<?php

declare(strict_types=1);

use Clarian\Webhook;
use PHPUnit\Framework\TestCase;

final class WebhookTest extends TestCase
{
    private const SECRET = 'whsec_test';
    private const BODY = '{"id":"evt_1","type":"payment.completed","created_at":"2026-10-11T00:00:00Z","data":{"payment_id":"p1","amount_minor":"1000"}}';

    private static function headers(string $ts, ?string $sig = null): array
    {
        return ['X-Clarian-Signature' => $sig ?? Webhook::sign(self::BODY, $ts, self::SECRET), 'X-Clarian-Timestamp' => $ts];
    }

    private static function now(int $offset = 0): string
    {
        return gmdate('Y-m-d\TH:i:s', time() + $offset) . '.123Z';
    }

    public function testValid(): void
    {
        $event = Webhook::constructEvent(self::BODY, self::headers(self::now()), self::SECRET);
        $this->assertSame('evt_1', $event['id']);
        $this->assertSame('payment.completed', $event['type']);
        $this->assertSame('1000', $event['data']['amount_minor']);
    }

    public function testSignatureMatchesNodeHmac(): void
    {
        // printf '%s' '2026-01-01T00:00:00.000Z.{}' | openssl dgst -sha256 -hmac k
        $this->assertSame(hash_hmac('sha256', '2026-01-01T00:00:00.000Z.{}', 'k'), Webhook::sign('{}', '2026-01-01T00:00:00.000Z', 'k'));
    }

    public function testBadSignature(): void
    {
        $this->expectException(\UnexpectedValueException::class);
        Webhook::constructEvent(self::BODY, self::headers(self::now(), str_repeat('0', 64)), self::SECRET);
    }

    public function testTamperedBody(): void
    {
        $this->assertFalse(Webhook::verifySignature(self::BODY . ' ', Webhook::sign(self::BODY, $ts = self::now(), self::SECRET), $ts, self::SECRET));
    }

    public function testStale(): void
    {
        $ts = self::now(-301);
        $this->assertFalse(Webhook::verifySignature(self::BODY, Webhook::sign(self::BODY, $ts, self::SECRET), $ts, self::SECRET));
        $ts = self::now(-200);
        $this->assertTrue(Webhook::verifySignature(self::BODY, Webhook::sign(self::BODY, $ts, self::SECRET), $ts, self::SECRET));
    }

    public function testUnparseableTimestamp(): void
    {
        foreach (['', 'now', 'not-a-date', (string) time()] as $ts) {
            $this->assertFalse(Webhook::verifySignature(self::BODY, Webhook::sign(self::BODY, $ts, self::SECRET), $ts, self::SECRET), $ts);
        }
    }

    public function testEmptySecretRefused(): void
    {
        $ts = gmdate('Y-m-d\TH:i:s\Z');
        $this->expectException(\InvalidArgumentException::class);
        Webhook::constructEvent(self::BODY, self::headers($ts), '');
    }
}
