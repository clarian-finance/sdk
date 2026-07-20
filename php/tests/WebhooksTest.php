<?php

declare(strict_types=1);

namespace Clarian\Tests;

use Clarian\Webhooks;
use PHPUnit\Framework\TestCase;

final class WebhooksTest extends TestCase
{
    private string $secret = 'whsec_test_secret';
    private string $body;
    private string $ts;

    protected function setUp(): void
    {
        $this->body = '{"id":"evt_1","type":"pix_payin.completed",'
            . '"created_at":"2026-01-01T00:00:00Z","environment":"sandbox",'
            . '"data":{"transaction_id":"ord_abc","status":"completed",'
            . '"amount":19.50,"fee":0.37,"currency":"BRL"}}';
        $this->ts = (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))
            ->format('Y-m-d\TH:i:s.u\Z');
    }

    public function testSignVerifyRoundtrip(): void
    {
        $sig = Webhooks::signPayload($this->secret, $this->ts, $this->body);
        $this->assertTrue(
            Webhooks::verifySignature($this->body, $this->ts, $sig, $this->secret),
        );
    }

    public function testTamperedSignatureRejected(): void
    {
        $sig = Webhooks::signPayload($this->secret, $this->ts, $this->body);
        $tampered = str_replace('ord_abc', 'ord_evil', $this->body);
        $this->assertFalse(
            Webhooks::verifySignature($tampered, $this->ts, $sig, $this->secret),
        );
    }

    public function testWrongSecretRejected(): void
    {
        $sig = Webhooks::signPayload('whsec_wrong', $this->ts, $this->body);
        $this->assertFalse(
            Webhooks::verifySignature($this->body, $this->ts, $sig, $this->secret),
        );
    }

    public function testStaleTimestampRejected(): void
    {
        $stale = (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))
            ->modify('-6 minutes')
            ->format('Y-m-d\TH:i:s.u\Z');
        $sig = Webhooks::signPayload($this->secret, $stale, $this->body);
        $this->assertFalse(
            Webhooks::verifySignature($this->body, $stale, $sig, $this->secret),
        );
    }

    public function testFutureTimestampBeyondToleranceRejected(): void
    {
        $future = (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))
            ->modify('+6 minutes')
            ->format('Y-m-d\TH:i:s.u\Z');
        $sig = Webhooks::signPayload($this->secret, $future, $this->body);
        $this->assertFalse(
            Webhooks::verifySignature($this->body, $future, $sig, $this->secret),
        );
    }

    public function testCustomTolerance(): void
    {
        $old = (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))
            ->modify('-10 seconds')
            ->format('Y-m-d\TH:i:s.u\Z');
        $sig = Webhooks::signPayload($this->secret, $old, $this->body);
        $this->assertFalse(
            Webhooks::verifySignature($this->body, $old, $sig, $this->secret, 5),
        );
        $this->assertTrue(
            Webhooks::verifySignature($this->body, $old, $sig, $this->secret, 60),
        );
    }

    public function testMissingInputsRejected(): void
    {
        $sig = Webhooks::signPayload($this->secret, $this->ts, $this->body);
        $this->assertFalse(Webhooks::verifySignature($this->body, $this->ts, $sig, ''));
        $this->assertFalse(Webhooks::verifySignature($this->body, '', $sig, $this->secret));
        $this->assertFalse(Webhooks::verifySignature($this->body, $this->ts, '', $this->secret));
    }

    public function testExtractHeaders(): void
    {
        $headers = [
            Webhooks::HEADER_TIMESTAMP => $this->ts,
            Webhooks::HEADER_SIGNATURE => 'abc123',
            Webhooks::HEADER_EVENT => 'pix_payin.completed',
            Webhooks::HEADER_DELIVERY_ID => 'del_1',
            Webhooks::HEADER_IDEMPOTENCY_KEY => 'idem_1',
            Webhooks::HEADER_ATTEMPT => '1',
        ];
        $h = Webhooks::extractHeaders($headers);
        $this->assertSame($this->ts, $h['timestamp']);
        $this->assertSame('abc123', $h['signature']);
        $this->assertSame('pix_payin.completed', $h['event']);
        $this->assertSame('del_1', $h['deliveryId']);
        $this->assertSame('idem_1', $h['idempotencyKey']);
        $this->assertSame('1', $h['attempt']);
    }

    public function testExtractHeadersCaseInsensitive(): void
    {
        $h = Webhooks::extractHeaders([
            'x-clarian-timestamp' => 't1',
            'x-clarian-signature' => 's1',
        ]);
        $this->assertSame('t1', $h['timestamp']);
        $this->assertSame('s1', $h['signature']);
    }

    public function testExtractHeadersListValues(): void
    {
        $h = Webhooks::extractHeaders([
            'X-Clarian-Timestamp' => ['t2'],
            'X-Clarian-Signature' => ['s2'],
        ]);
        $this->assertSame('t2', $h['timestamp']);
        $this->assertSame('s2', $h['signature']);
    }

    public function testUnixTimestampAccepted(): void
    {
        $now = (string) time();
        $sig = Webhooks::signPayload($this->secret, $now, $this->body);
        $this->assertTrue(
            Webhooks::verifySignature($this->body, $now, $sig, $this->secret),
        );
    }
}
