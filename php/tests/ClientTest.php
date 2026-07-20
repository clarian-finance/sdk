<?php

declare(strict_types=1);

namespace Clarian\Tests;

use Clarian\ClarianException;
use Clarian\Client;
use PHPUnit\Framework\TestCase;

final class ClientTest extends TestCase
{
    public function testEnvDetectionTestPrefix(): void
    {
        $c = new Client('cl_test_sk_abc', 'ws');
        $this->assertSame(Client::BASE_URL_TEST, $c->baseUrl);
    }

    public function testEnvDetectionLivePrefix(): void
    {
        $c = new Client('cl_live_sk_abc', 'ws');
        $this->assertSame(Client::BASE_URL_LIVE, $c->baseUrl);
    }

    public function testEnvDetectionUnknownDefaultsLive(): void
    {
        $c = new Client('other_key', 'ws');
        $this->assertSame(Client::BASE_URL_LIVE, $c->baseUrl);
    }

    public function testBaseUrlOverrideStripsTrailingSlash(): void
    {
        $c = new Client('cl_test_sk_x', 'ws', 'http://localhost:9999/v1/');
        $this->assertSame('http://localhost:9999/v1', $c->baseUrl);
    }

    public function testAuthHeadersOnEveryCall(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'environment' => 'sandbox',
            'master_account_id' => 'm1',
        ]]);
        $c = new Client('cl_test_sk_key', 'ws-uuid', 'https://mock', null, $mt);
        $res = $c->ping();
        $this->assertTrue($res['ok']);
        $this->assertSame('GET', $mt->last()->method);
        $this->assertSame('/ping', $mt->last()->path);
        $this->assertSame('Bearer cl_test_sk_key', $mt->last()->headers['Authorization']);
        $this->assertSame('ws-uuid', $mt->last()->headers['X-Workspace-Id']);
    }

    public function testCashInCreateSendsIdempotencyAndWorkspace(): void
    {
        $mt = new FakeTransport(static fn () => [201, [
            'ok' => true,
            'environment' => 'sandbox',
            'order' => [
                'id' => 'ord_1',
                'status' => 'pending',
                'amount' => 19.50,
                'currency' => 'BRL',
                'pix' => ['copy_paste' => 'brcode'],
            ],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws-uuid', 'https://mock', null, $mt);
        $res = $c->cashIn->create([
            'amount' => 19.50,
            'payer' => ['name' => 'Ana', 'document_number' => '52998224725'],
        ], 'pay-ext-1');
        $this->assertSame('ord_1', $res['order']['id']);
        $this->assertSame('POST', $mt->last()->method);
        $this->assertSame('/cash-in/pix', $mt->last()->path);
        $this->assertSame('pay-ext-1', $mt->last()->headers['Idempotency-Key']);
        $this->assertSame('ws-uuid', $mt->last()->headers['X-Workspace-Id']);
        $sent = json_decode((string) $mt->last()->body, true);
        $this->assertSame(19.50, $sent['amount']);
        $this->assertSame('Ana', $sent['payer']['name']);
    }

    public function testCashOutCreateSendsIdempotency(): void
    {
        $mt = new FakeTransport(static fn () => [201, [
            'ok' => true,
            'order' => ['id' => 'out_9', 'status' => 'processing'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $c->cashOut->create([
            'amount' => 10.00,
            'pix_key' => 'abc',
            'pix_key_type' => 'EVP',
        ], 'repasse-1');
        $this->assertSame('repasse-1', $mt->last()->headers['Idempotency-Key']);
        $this->assertSame('/cash-out/pix', $mt->last()->path);
    }

    public function testGetDoesNotSendIdempotencyKey(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'x', 'status' => 'pending'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $c->cashIn->retrieve('x');
        $this->assertArrayNotHasKey('Idempotency-Key', $mt->last()->headers);
    }

    public function testCashInRetrieveHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'ord_paid', 'status' => 'completed'],
        ]]);
        $c = new Client('cl_live_sk_x', 'ws', 'https://mock', null, $mt);
        $res = $c->cashIn->retrieve('ord_paid');
        $this->assertSame('completed', $res['order']['status']);
        $this->assertSame('/cash-in/ord_paid', $mt->last()->path);
    }

    public function testCashOutRetrieveHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'order' => ['id' => 'out_1', 'status' => 'completed'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $res = $c->cashOut->retrieve('out_1');
        $this->assertSame('out_1', $res['order']['id']);
        $this->assertSame('/cash-out/out_1', $mt->last()->path);
    }

    public function testCashOutDictCheckHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'dict' => ['name' => 'Maria', 'keyType' => 'EMAIL'],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $res = $c->cashOut->dictCheck('maria@example.com', 'EMAIL');
        $this->assertSame('Maria', $res['dict']['name']);
        $this->assertSame('POST', $mt->last()->method);
        $this->assertSame('/pix/payouts/dict', $mt->last()->path);
        $sent = json_decode((string) $mt->last()->body, true);
        $this->assertSame('maria@example.com', $sent['pix_key']);
        $this->assertSame('EMAIL', $sent['key_type']);
    }

    public function testBalancesListHappyPath(): void
    {
        $mt = new FakeTransport(static fn () => [200, [
            'ok' => true,
            'balances' => [[
                'currency' => 'BRL',
                'available' => 100.50,
                'pending' => 0,
                'locked' => 0,
            ]],
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $bals = $c->balances->list();
        $this->assertCount(1, $bals);
        $this->assertSame('BRL', $bals[0]['currency']);
        $this->assertSame('/account/balances', $mt->last()->path);
    }

    public function testTransactionsListAndRetrieveHappyPath(): void
    {
        $mt = new FakeTransport(function (RecordedRequest $req) {
            if (str_starts_with($req->path, '/transactions?') || $req->path === '/transactions') {
                $this->assertStringContainsString('type=pix_in', $req->path);
                $this->assertStringContainsString('limit=20', $req->path);

                return [200, [
                    'transactions' => [[
                        'id' => 'tx1',
                        'type' => 'pix_in',
                        'status' => 'completed',
                        'amount' => 10,
                        'fee' => 0,
                        'currency' => 'BRL',
                    ]],
                ]];
            }
            if ($req->path === '/transactions/tx-99') {
                return [200, [
                    'transaction' => [
                        'id' => 'tx-99',
                        'type' => 'pix_in',
                        'status' => 'completed',
                        'amount' => 10,
                        'fee' => 0.1,
                        'currency' => 'BRL',
                    ],
                ]];
            }
            $this->fail('unexpected path ' . $req->path);
        });
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $lst = $c->transactions->list('pix_in', null, 20);
        $this->assertSame('tx1', $lst[0]['id']);
        $tx = $c->transactions->retrieve('tx-99');
        $this->assertSame('tx-99', $tx['id']);
    }

    public function testWalletsListAndBalanceHappyPath(): void
    {
        $mt = new FakeTransport(function (RecordedRequest $req) {
            if ($req->path === '/wallets' || str_starts_with($req->path, '/wallets?')) {
                return [200, [
                    'ok' => true,
                    'wallets' => [[
                        'id' => 'w1',
                        'network' => 'polygon',
                        'address' => '0xabc',
                    ]],
                ]];
            }
            if ($req->path === '/wallets/w1/balance') {
                return [200, [
                    'wallet_id' => 'w1',
                    'network' => 'polygon',
                    'address' => '0xabc',
                    'balances' => [['currency' => 'USDT', 'amount' => '1.5']],
                ]];
            }
            $this->fail('unexpected path ' . $req->path);
        });
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $wallets = $c->wallets->list();
        $this->assertSame('w1', $wallets[0]['id']);
        $bal = $c->wallets->retrieveBalance('w1');
        $this->assertSame('w1', $bal['wallet_id']);
        $this->assertSame('USDT', $bal['balances'][0]['currency']);
    }

    public function testWebhooksCrudHappyPath(): void
    {
        $mt = new FakeTransport(function (RecordedRequest $req) {
            if ($req->method === 'POST' && $req->path === '/webhooks') {
                return [201, [
                    'ok' => true,
                    'subscription' => [
                        'id' => 'wh1',
                        'url' => 'https://ex.com',
                        'events' => ['pix_payin.completed'],
                        'is_active' => true,
                    ],
                    'secret' => 'whsec_abc',
                ]];
            }
            if ($req->method === 'GET' && $req->path === '/webhooks') {
                return [200, [
                    'subscriptions' => [[
                        'id' => 'wh1',
                        'url' => 'https://ex.com',
                        'events' => ['pix_payin.completed'],
                        'is_active' => true,
                    ]],
                ]];
            }
            if ($req->method === 'PATCH' && $req->path === '/webhooks/wh1') {
                return [200, [
                    'subscription' => [
                        'id' => 'wh1',
                        'url' => 'https://ex.com/v2',
                        'events' => ['pix_payin.completed'],
                        'is_active' => false,
                    ],
                ]];
            }
            if ($req->method === 'DELETE' && $req->path === '/webhooks/wh1') {
                return [200, ['ok' => true]];
            }
            $this->fail('unexpected ' . $req->method . ' ' . $req->path);
        });
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        $created = $c->webhooks->create([
            'url' => 'https://ex.com',
            'events' => ['pix_payin.completed'],
        ]);
        $this->assertSame('whsec_abc', $created['secret']);
        $this->assertSame('wh1', $created['id']);
        $subs = $c->webhooks->list();
        $this->assertCount(1, $subs);
        $upd = $c->webhooks->update('wh1', [
            'url' => 'https://ex.com/v2',
            'events' => ['pix_payin.completed'],
            'is_active' => false,
        ]);
        $this->assertFalse($upd['is_active']);
        $c->webhooks->delete('wh1');
        $this->assertSame('DELETE', $mt->calls[array_key_last($mt->calls)]->method);
    }

    public function testErrorJsonCodeAndMeta(): void
    {
        $mt = new FakeTransport(static fn () => [402, [
            'error' => 'insufficient_balance',
            'detail' => 'saldo insuficiente',
            'available' => 10,
            'requested' => 100,
        ]]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        try {
            $c->cashOut->create(['amount' => 100], 'idem');
            $this->fail('expected ClarianException');
        } catch (ClarianException $err) {
            $this->assertSame(402, $err->status);
            $this->assertSame('insufficient_balance', $err->code);
            $this->assertSame('saldo insuficiente', $err->message);
            $this->assertSame(10, $err->meta['available']);
            $this->assertStringContainsString('insufficient_balance', $err->getMessage());
        }
    }

    public function testBodyTruncationInMessage(): void
    {
        $longBody = str_repeat('x', 600);
        $mt = new FakeTransport(static fn () => [429, $longBody]);
        $c = new Client('cl_test_sk_x', 'ws', 'https://mock', null, $mt);
        try {
            $c->ping();
            $this->fail('expected ClarianException');
        } catch (ClarianException $err) {
            $this->assertSame(429, $err->status);
            $this->assertSame('', $err->code);
            $this->assertSame(500, strlen($err->message));
            $this->assertLessThanOrEqual(500 + strlen('HTTP 429: '), strlen($err->getMessage()));
            $this->assertStringNotContainsString(str_repeat('x', 600), $err->getMessage());
            $this->assertSame($longBody, $err->body);
        }
    }
}
