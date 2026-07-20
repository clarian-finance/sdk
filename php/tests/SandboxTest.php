<?php

declare(strict_types=1);

namespace Clarian\Tests;

use Clarian\Client;
use Clarian\HttpTransport;
use Clarian\Sandbox;
use PHPUnit\Framework\TestCase;

final class FailTransport implements HttpTransport
{
    public function request(
        string $method,
        string $url,
        array $headers,
        ?string $body,
        float $timeout,
    ): array {
        throw new \RuntimeException('unexpected HTTP call');
    }
}

final class SandboxTest extends TestCase
{
    public function testLiveKeyRejectedBeforeNetwork(): void
    {
        $c = new Client('cl_live_sk_x', 'ws', 'https://mock', null, new FailTransport());
        try {
            $c->sandbox->simulateCashIn('ord_1', 'completed');
            $this->fail('expected LogicException');
        } catch (\LogicException $e) {
            $this->assertStringContainsString('cl_test_sk_', $e->getMessage());
        }

        $this->expectException(\LogicException::class);
        $c->sandbox->simulateCashOut('out_1', 'completed');
    }

    public function testLiveKeyRejectedOnAllSandboxMethods(): void
    {
        $c = new Client('cl_live_sk_x', 'ws', 'https://mock', null, new FailTransport());
        foreach ([
            fn () => $c->sandbox->simulateCashIn('ord_1'),
            fn () => $c->sandbox->simulateCashOut('out_1'),
            fn () => $c->sandbox->sendWebhookEvent('wh1', Sandbox::EVENT_PIX_PAYIN_COMPLETED),
            fn () => $c->sandbox->resendWebhookDelivery('del_1'),
        ] as $call) {
            try {
                $call();
                $this->fail('expected LogicException');
            } catch (\LogicException $e) {
                $this->assertStringContainsString('cl_test_sk_', $e->getMessage());
            }
        }
    }

    public function testInvalidStatusNoHttp(): void
    {
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, new FailTransport());
        try {
            $c->sandbox->simulateCashIn('ord_1', 'bogus');
            $this->fail('expected InvalidArgumentException');
        } catch (\InvalidArgumentException $e) {
            $this->assertStringContainsString('invalid cash-in', $e->getMessage());
        }

        try {
            $c->sandbox->simulateCashOut('out_1', 'expired');
            $this->fail('expected InvalidArgumentException');
        } catch (\InvalidArgumentException $e) {
            $this->assertStringContainsString('invalid cash-out', $e->getMessage());
        }
    }

    public function testSimulateCashInHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'environment' => 'sandbox',
            'order' => [
                'id' => 'ord_1',
                'status' => 'completed',
                'amount' => 19.50,
            ],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws-uuid', 'https://mock', null, $mt);
        $res = $c->sandbox->simulateCashIn('ord_1', 'completed');
        $this->assertSame('ord_1', $res['order']['id']);
        $this->assertSame('POST', $mt->last()->method);
        $this->assertSame('/pix/payins/ord_1/simulate', $mt->last()->path);
        $this->assertSame('Bearer cl_test_sk_x', $mt->last()->headers['Authorization']);
        $body = json_decode((string) $mt->last()->body, true);
        $this->assertSame('completed', $body['status']);
    }

    public function testSimulateCashInDefaultStatus(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'ord_2', 'status' => 'completed'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $c->sandbox->simulateCashIn('ord_2');
        $body = json_decode((string) $mt->last()->body, true);
        $this->assertSame('completed', $body['status']);
    }

    public function testSimulateCashOutHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'out_1', 'status' => 'failed'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $res = $c->sandbox->simulateCashOut('out_1', 'failed');
        $this->assertSame('failed', $res['order']['status']);
        $this->assertSame('/pix/payouts/out_1/simulate', $mt->last()->path);
    }

    public function testSendWebhookEventHappyPath(): void
    {
        $mt = new FakeTransport(function (RecordedRequest $req) {
            $this->assertSame('/webhooks/wh1/test', $req->path);

            return [202, [
                'ok' => true,
                'environment' => 'sandbox',
                'event_type' => 'pix_payin.completed',
                'enqueued' => ['enqueued' => 1, 'delivery_id' => 'del_99'],
            ]];
        });
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $res = $c->sandbox->sendWebhookEvent('wh1', Sandbox::EVENT_PIX_PAYIN_COMPLETED);
        $this->assertSame(Sandbox::EVENT_PIX_PAYIN_COMPLETED, $res['event_type']);
        $this->assertSame(1, $res['enqueued']);
        $this->assertSame('del_99', $res['delivery_id']);
        $body = json_decode((string) $mt->last()->body, true);
        $this->assertSame('pix_payin.completed', $body['event_type']);
    }

    public function testResendWebhookDeliveryHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [202, [
            'ok' => true,
            'delivery_id' => 'del_new',
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $newId = $c->sandbox->resendWebhookDelivery('del_old');
        $this->assertSame('del_new', $newId);
        $this->assertSame('/webhooks/deliveries/del_old/resend', $mt->last()->path);
        $this->assertSame('POST', $mt->last()->method);
    }

    public function testMagicPixKeyConstants(): void
    {
        $this->assertSame('fail@sandbox.clarian', Sandbox::SANDBOX_FAIL_PIX_KEY);
        $this->assertSame('pending@sandbox.clarian', Sandbox::SANDBOX_PENDING_PIX_KEY);
    }

    public function testPathEscape(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'x', 'status' => 'completed'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $c->sandbox->simulateCashIn('a/b', 'completed');
        $this->assertSame('/pix/payins/a%2Fb/simulate', $mt->last()->path);
    }
}
