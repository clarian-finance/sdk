<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Sandbox-only test helpers.
 *
 * Server-side these endpoints return 404 {error:"sandbox_only"} outside the
 * test environment; the client also refuses non-cl_test_sk_ keys before any
 * HTTP call (throws LogicException).
 */
final class Sandbox
{
    public const SANDBOX_FAIL_PIX_KEY = 'fail@sandbox.clarian';
    public const SANDBOX_PENDING_PIX_KEY = 'pending@sandbox.clarian';

    public const EVENT_PIX_PAYIN_CREATED = 'pix_payin.created';
    public const EVENT_PIX_PAYIN_COMPLETED = 'pix_payin.completed';
    public const EVENT_PIX_PAYIN_EXPIRED = 'pix_payin.expired';
    public const EVENT_PIX_PAYOUT_CREATED = 'pix_payout.created';
    public const EVENT_PIX_PAYOUT_COMPLETED = 'pix_payout.completed';
    public const EVENT_PIX_PAYOUT_FAILED = 'pix_payout.failed';
    public const EVENT_CHECKOUT_PAID = 'checkout.paid';

    private const CASH_IN_STATUSES = ['completed', 'expired', 'failed'];
    private const CASH_OUT_STATUSES = ['completed', 'failed'];

    public function __construct(private readonly Client $client)
    {
    }

    private function requireTestKey(): void
    {
        if (!str_starts_with($this->client->apiKey, 'cl_test_sk_')) {
            throw new \LogicException('sandbox helpers require a cl_test_sk_ key');
        }
    }

    /**
     * POST /pix/payins/{id}/simulate.
     * Status: completed | expired | failed. Empty/null defaults to completed.
     *
     * @return array<string, mixed>
     */
    public function simulateCashIn(string $orderId, ?string $status = null): array
    {
        $this->requireTestKey();
        $resolved = ($status === null || $status === '') ? 'completed' : $status;
        if (!in_array($resolved, self::CASH_IN_STATUSES, true)) {
            throw new \InvalidArgumentException(
                'invalid cash-in simulate status "' . $resolved . '"',
            );
        }

        return $this->client->request(
            'POST',
            '/pix/payins/' . Client::pathEscape($orderId) . '/simulate',
            ['status' => $resolved],
        );
    }

    /**
     * POST /pix/payouts/{id}/simulate.
     * Status: completed | failed. Empty/null defaults to completed.
     *
     * @return array<string, mixed>
     */
    public function simulateCashOut(string $orderId, ?string $status = null): array
    {
        $this->requireTestKey();
        $resolved = ($status === null || $status === '') ? 'completed' : $status;
        if (!in_array($resolved, self::CASH_OUT_STATUSES, true)) {
            throw new \InvalidArgumentException(
                'invalid cash-out simulate status "' . $resolved . '"',
            );
        }

        return $this->client->request(
            'POST',
            '/pix/payouts/' . Client::pathEscape($orderId) . '/simulate',
            ['status' => $resolved],
        );
    }

    /**
     * POST /webhooks/{id}/test: enqueue a sample delivery.
     *
     * @return array{
     *   ok: mixed,
     *   environment: mixed,
     *   event_type: mixed,
     *   enqueued: int,
     *   delivery_id: string
     * }
     */
    public function sendWebhookEvent(string $subscriptionId, string $eventType): array
    {
        $this->requireTestKey();
        $res = $this->client->request(
            'POST',
            '/webhooks/' . Client::pathEscape($subscriptionId) . '/test',
            ['event_type' => $eventType],
        );
        $enqueued = $res['enqueued'] ?? [];
        if (!is_array($enqueued)) {
            $enqueued = [];
        }
        $deliveryId = $enqueued['delivery_id'] ?? '';

        return [
            'ok' => $res['ok'] ?? null,
            'environment' => $res['environment'] ?? null,
            'event_type' => $res['event_type'] ?? null,
            'enqueued' => (int) ($enqueued['enqueued'] ?? 0),
            'delivery_id' => $deliveryId !== null ? (string) $deliveryId : '',
        ];
    }

    /**
     * POST /webhooks/deliveries/{id}/resend; returns the new delivery id.
     */
    public function resendWebhookDelivery(string $deliveryId): string
    {
        $this->requireTestKey();
        $res = $this->client->request(
            'POST',
            '/webhooks/deliveries/' . Client::pathEscape($deliveryId) . '/resend',
        );

        return (string) ($res['delivery_id'] ?? '');
    }
}
