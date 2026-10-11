<?php

declare(strict_types=1);

use Clarian\Clarian;
use Clarian\ClarianError;
use Clarian\HttpTransport;
use Clarian\TransportException;
use PHPUnit\Framework\TestCase;

final class FakeTransport implements HttpTransport
{
    /** @var list<array{method: string, url: string, headers: array, body: ?string}> */
    public array $calls = [];
    /** @var list<array{0:int,1:string,2:array}|\Throwable> */
    public array $replies;

    public function __construct(array $replies) { $this->replies = $replies; }

    public function send(string $method, string $url, array $headers, ?string $body, float $timeout): array
    {
        $this->calls[] = compact('method', 'url', 'headers', 'body');
        $r = array_shift($this->replies) ?? [200, '{"data":{}}', []];
        if ($r instanceof \Throwable) {
            throw $r;
        }
        return $r;
    }
}

final class ClientTest extends TestCase
{
    public array $slept = [];

    private function client(FakeTransport $t, string $key = 'cl_test_sk_abc'): Clarian
    {
        return new Clarian($key, 'ws_1', baseUrl: 'https://api.local/', transport: $t, sleep: function (float $s) { $this->slept[] = $s; });
    }

    private static function ok(mixed $data): array { return [200, json_encode(['data' => $data]), []]; }

    public function testBuildsUrlAndHeadersAndUnwrapsData(): void
    {
        $t = new FakeTransport([self::ok([['currency' => 'BRL']])]);
        $out = $this->client($t)->transactions->list(['limit' => 5, 'before' => null]);
        $this->assertSame([['currency' => 'BRL']], $out);
        $c = $t->calls[0];
        $this->assertSame('GET', $c['method']);
        $this->assertSame('https://api.local/v1/test/transactions?limit=5', $c['url']);
        $this->assertSame('Bearer cl_test_sk_abc', $c['headers']['Authorization']);
        $this->assertSame('ws_1', $c['headers']['X-Workspace-Id']);
        $this->assertArrayNotHasKey('X-Subaccount-Id', $c['headers']);
        $this->assertNull($c['body']);
    }

    public function testLiveKeyAndSubaccountAndIdempotencyHeaders(): void
    {
        $t = new FakeTransport([self::ok(['id' => 'p1'])]);
        $c = $this->client($t, 'cl_live_sk_x');
        $this->assertSame('live', $c->environment);
        $c->forSubaccount('sub 1')->pix->payins->create(['amount_minor' => 10000, 'currency' => 'BRL'], 'k-1');
        $call = $t->calls[0];
        $this->assertSame('https://api.local/v1/live/pix/payins', $call['url']);
        $this->assertSame('sub 1', $call['headers']['X-Subaccount-Id']);
        $this->assertSame('k-1', $call['headers']['Idempotency-Key']);
        $this->assertSame('application/json', $call['headers']['Content-Type']);
        $this->assertSame('{"amount_minor":"10000","currency":"BRL"}', $call['body']);
    }

    public function testBigIntegerAmountsAndFloatRejected(): void
    {
        $t = new FakeTransport([self::ok([])]);
        $this->client($t)->pix->payouts->fee(['amount_minor' => gmp_init('123456789012345678901234567890')]);
        $this->assertSame('{"amount_minor":"123456789012345678901234567890"}', $t->calls[0]['body']);
        $this->expectException(\InvalidArgumentException::class);
        $this->client($t)->pix->payouts->fee(['amount_minor' => 10.5]);
    }

    public function testInvalidKeyRejected(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        new Clarian('sk_nope', 'ws_1');
    }

    public function testErrorMapping(): void
    {
        $t = new FakeTransport([[402, '{"error":"insufficient_balance","message":"Not enough","needed":"5"}', []]]);
        try {
            $this->client($t)->balances->list();
            $this->fail('expected ClarianError');
        } catch (ClarianError $e) {
            $this->assertSame(402, $e->status);
            $this->assertSame('insufficient_balance', $e->errorCode);
            $this->assertSame('Not enough', $e->getMessage());
            $this->assertSame(['needed' => '5'], $e->meta);
        }
    }

    public function testNonJsonError(): void
    {
        $t = new FakeTransport([[400, '<html>', []]]);
        try {
            $this->client($t)->ping();
            $this->fail('expected ClarianError');
        } catch (ClarianError $e) {
            $this->assertSame('invalid_json_response', $e->errorCode);
        }
    }

    public function testGetRetriedHonoringRetryAfter(): void
    {
        $t = new FakeTransport([[503, '', []], [429, '', ['retry-after' => '2']], self::ok(['pong' => true])]);
        $this->assertSame(['pong' => true], $this->client($t)->ping());
        $this->assertCount(3, $t->calls);
        $this->assertSame(2.0, $this->slept[1]);
    }

    public function testGetRetriesExhausted(): void
    {
        $t = new FakeTransport([new TransportException('down'), [502, '', []], [502, '{"error":"bad_gateway"}', []]]);
        try {
            $this->client($t)->balances->list();
            $this->fail('expected ClarianError');
        } catch (ClarianError $e) {
            $this->assertSame(502, $e->status);
        }
        $this->assertCount(3, $t->calls);
    }

    public function testPostWithoutKeyNotRetried(): void
    {
        $t = new FakeTransport([[503, '{"error":"unavailable"}', []]]);
        $this->expectException(ClarianError::class);
        try {
            $this->client($t)->customers->create(['display_name' => 'x']);
        } finally {
            $this->assertCount(1, $t->calls);
        }
    }

    public function testExecuteNeverRetried(): void
    {
        $t = new FakeTransport([new TransportException('lost')]);
        $this->expectException(TransportException::class);
        try {
            $this->client($t)->conversions->execute('q1');
        } finally {
            $this->assertCount(1, $t->calls);
            $this->assertArrayNotHasKey('Idempotency-Key', $t->calls[0]['headers']);
        }
    }

    public function testPostWithKeyRetried(): void
    {
        $t = new FakeTransport([[504, '', []], self::ok(['id' => 'p1'])]);
        $this->assertSame(['id' => 'p1'], $this->client($t)->crypto->send(['amount_minor' => '1'], 'k'));
        $this->assertCount(2, $t->calls);
    }

    public function testIdempotencyKeyRequired(): void
    {
        $t = new FakeTransport([]);
        $c = $this->client($t);
        $calls = [
            fn () => $c->pix->payins->create(['amount_minor' => 1], ''),
            fn () => $c->pix->payouts->create(['amount_minor' => 1], ''),
            fn () => $c->crypto->send(['amount_minor' => 1], ''),
            fn () => $c->paymentLinks->charge('l1', [], ''),
            fn () => $c->checkout->pay('s1', [], ''),
        ];
        foreach ($calls as $call) {
            try {
                $call();
                $this->fail('expected InvalidArgumentException');
            } catch (\InvalidArgumentException $e) {
                $this->assertStringContainsString('idempotencyKey is required', $e->getMessage());
            }
        }
        $this->assertCount(0, $t->calls);
    }

    public function testDotAndEmptyIdsRefused(): void
    {
        $t = new FakeTransport([]);
        foreach (['', '.', '..'] as $id) {
            try {
                $this->client($t)->wallets->retrieve($id);
                $this->fail("id '$id' accepted");
            } catch (\InvalidArgumentException) {
            }
        }
        $this->assertSame([], $t->calls);
    }

    public function testBlankSubaccountRefused(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        $this->client(new FakeTransport([]))->forSubaccount('  ');
    }

    public function testBaseUrlMustBeHttps(): void
    {
        foreach (['http://api.example.com', 'http://localhost.evil.com', 'ftp://x'] as $u) {
            try {
                new Clarian('cl_test_sk_x', 'ws', baseUrl: $u);
                $this->fail("$u accepted");
            } catch (\InvalidArgumentException) {
            }
        }
        foreach (['https://api.example.com', 'http://localhost:3000', 'http://127.0.0.1', 'http://host.docker.internal:8080'] as $u) {
            $this->assertInstanceOf(Clarian::class, new Clarian('cl_test_sk_x', 'ws', baseUrl: $u));
        }
    }

    public function testDumpMasksKey(): void
    {
        $c = new Clarian('cl_test_sk_secret', 'ws');
        $out = print_r($c, true) . print_r($c->forSubaccount('sub'), true);
        ob_start();
        var_dump($c);
        $out .= ob_get_clean();
        $this->assertStringNotContainsString('cl_test_sk_secret', $out);
    }
}
