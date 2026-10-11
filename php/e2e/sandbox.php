<?php

// End-to-end run of every SDK method against a sandbox API, plus a spec drift check.
// Usage: CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... [CLARIAN_BASE_URL=http://localhost:3000] php e2e/sandbox.php

declare(strict_types=1);

require __DIR__ . '/../vendor/autoload.php';

use Clarian\Clarian;
use Clarian\ClarianError;

$apiKey = getenv('CLARIAN_API_KEY') ?: '';
$workspaceId = getenv('CLARIAN_WORKSPACE_ID') ?: '';
if (!str_starts_with($apiKey, 'cl_test_sk_') || $workspaceId === '') {
    fwrite(STDERR, "CLARIAN_API_KEY (cl_test_sk_...) and CLARIAN_WORKSPACE_ID are required\n");
    exit(2);
}
$clarian = new Clarian($apiKey, $workspaceId, baseUrl: getenv('CLARIAN_BASE_URL') ?: 'https://api.clarian.finance');
$spec = json_decode(file_get_contents(__DIR__ . '/../../openapi.json'), true);

$outcomes = [];
$drift = [];

$checkShape = function (string $schema, mixed $value) use ($spec, &$drift): void {
    $props = $spec['components']['schemas'][$schema]['properties'] ?? [];
    $rows = is_array($value) && array_is_list($value) ? $value : [$value];
    foreach (array_slice(array_filter($rows, 'is_array'), 0, 5) as $row) {
        foreach (array_keys($row) as $k) {
            if (!array_key_exists($k, $props)) {
                $drift[$schema]["+$k"] = true;
            }
        }
    }
};
$step = function (string $name, callable $fn, ?string $schema = null) use (&$outcomes, $checkShape): mixed {
    try {
        $v = $fn();
        if ($schema) {
            $checkShape($schema, $v);
        }
        $outcomes[] = [$name, true, substr((string) json_encode($v), 0, 140)];
        return $v;
    } catch (\Throwable $e) {
        $outcomes[] = [$name, false, $e instanceof ClarianError ? "{$e->status} {$e->errorCode}" : get_class($e) . ': ' . $e->getMessage()];
        return null;
    }
};
$expectError = function (string $name, string $code, callable $fn) use (&$outcomes): void {
    try {
        $fn();
        $outcomes[] = [$name, false, "accepted, expected $code"];
    } catch (\Throwable $e) {
        $got = $e instanceof ClarianError ? $e->errorCode : $e->getMessage();
        $outcomes[] = [$name, $got === $code, $got];
    }
};
$check = function (string $name, bool $ok, string $note) use (&$outcomes): void {
    $outcomes[] = [$name, $ok, $note];
};
$key = fn () => bin2hex(random_bytes(16));
// A fresh valid CPF per run: the API rejects a document already on file.
$cpf = function (): string {
    $base = array_map(fn () => random_int(0, 9), range(1, 9));
    $digit = function (array $ds): int {
        $s = 0;
        foreach ($ds as $i => $d) {
            $s += $d * (count($ds) + 1 - $i);
        }
        return ($s * 10) % 11 % 10;
    };
    $d1 = $digit($base);
    return implode('', [...$base, $d1, $digit([...$base, $d1])]);
};
$brl = function (?array $rows): int {
    foreach ($rows ?? [] as $r) {
        if ($r['currency'] === 'BRL' && $r['kind'] === 'available') {
            return (int) $r['balance_minor'];
        }
    }
    return 0;
};

$step('ping', fn () => $clarian->ping());
$before = $step('balances.list', fn () => $clarian->balances->list());

// Cash-in
$payin = $step('pix.payins.create', fn () => $clarian->pix->payins->create(['amount_minor' => gmp_init(10000), 'currency' => 'BRL'], $key()), 'Payment');
if (isset($payin['id'])) {
    $step('pix.payins.retrieve', fn () => $clarian->pix->payins->retrieve($payin['id']), 'Payment');
    $step('pix.payins.simulate', fn () => $clarian->pix->payins->simulate($payin['id']), 'Payment');
}
$afterIn = $step('balances after cash-in', fn () => $clarian->balances->list());
$check('cash-in credited 100.00', $brl($afterIn) - $brl($before) === 10000, (string) ($brl($afterIn) - $brl($before)));

// Cash-out
$step('pix.payouts.fee', fn () => $clarian->pix->payouts->fee(['amount_minor' => '1000']), 'PixFeeQuote');
$step('pix.payouts.lookup', fn () => $clarian->pix->payouts->lookup(['pix_key' => 'maria@example.com']), 'PixDictResult');
$step('pix.payouts.listAccounts', fn () => $clarian->pix->payouts->listAccounts(), 'PayoutAccount');
$idem = $key();
$payoutBody = ['amount_minor' => 1000, 'currency' => 'BRL', 'pix_key' => 'maria@example.com'];
$payout = $step('pix.payouts.create', fn () => $clarian->pix->payouts->create($payoutBody, $idem), 'Payment');
$replay = $step('pix.payouts.create replay', fn () => $clarian->pix->payouts->create($payoutBody, $idem));
$check('payout replay returns same payment', isset($payout['id']) && ($replay['id'] ?? null) === $payout['id'], ($payout['id'] ?? '-') . ' ' . ($replay['id'] ?? '-'));
if (isset($payout['id'])) {
    $step('pix.payouts.retrieve', fn () => $clarian->pix->payouts->retrieve($payout['id']), 'Payment');
    $step('pix.payouts.simulate', fn () => $clarian->pix->payouts->simulate($payout['id']), 'Payment');
}
$afterOut = $step('balances after cash-out', fn () => $clarian->balances->list());
$check('cash-out debited 10.00 + fee', $brl($afterIn) - $brl($afterOut) > 1000, (string) ($brl($afterIn) - $brl($afterOut)));

// Quote and convert: pay 50.00 BRL for USDT
$step('conversions.prices', fn () => $clarian->conversions->prices(['USDT']));
$quote = $step('conversions.quote', fn () => $clarian->conversions->quote(['base_currency' => 'BRL', 'quote_currency' => 'USDT', 'base_amount_minor' => 5000]), 'Quote');
if (isset($quote['id'])) {
    $exec = $step('conversions.execute', fn () => $clarian->conversions->execute($quote['id']));
    $ledger = $exec['ledger'] ?? [];
    $check('convert debited BRL, credited USDT', ($ledger['from_currency'] ?? null) === 'BRL' && ($ledger['to_currency'] ?? null) === 'USDT', (string) json_encode($ledger));
    $expectError('conversions.execute twice', 'quote_not_open', fn () => $clarian->conversions->execute($quote['id']));
}

// Wallets and crypto
$wallets = $step('wallets.list', fn () => $clarian->wallets->list(), 'Wallet');
$w = $wallets[0] ?? null;
if (isset($w['id'])) {
    $step('wallets.retrieve', fn () => $clarian->wallets->retrieve($w['id']), 'Wallet');
    $step('wallets.balance', fn () => $clarian->wallets->balance($w['id']));
    $step('wallets.transactions', fn () => $clarian->wallets->transactions($w['id'], ['limit' => 5]), 'Payment');
}
$step('crypto.networks', fn () => $clarian->crypto->networks(), 'PayableNetwork');

// Ledger views
$step('transactions.list', fn () => $clarian->transactions->list(['limit' => 5]), 'Payment');
if (isset($payin['id'])) {
    $step('transactions.retrieve', fn () => $clarian->transactions->retrieve($payin['id']), 'Payment');
}
$step('transactions.statement', fn () => $clarian->transactions->statement(['limit' => 5]), 'StatementLine');

// Customers and KYC
$customer = $step('customers.create', fn () => $clarian->customers->create(['kind' => 'person', 'display_name' => 'SDK E2E', 'document_type' => 'cpf', 'document_number' => $cpf()]), 'Customer');
$step('customers.list', fn () => $clarian->customers->list(), 'Customer');
if (isset($customer['id'])) {
    $step('customers.retrieve', fn () => $clarian->customers->retrieve($customer['id']), 'Customer');
    $step('customers.update', fn () => $clarian->customers->update($customer['id'], ['display_name' => 'SDK E2E 2']), 'Customer');
}
$step('kyc.list', fn () => $clarian->kyc->list(), 'KycCase');

// Webhooks
$hook = $step('webhooks.create', fn () => $clarian->webhooks->create(['url' => 'https://hooks.example.com/clarian', 'events' => ['payment.completed']]), 'WebhookCreateResponse');
$check('webhook secret returned once', is_string($hook['secret'] ?? null) && strlen($hook['secret']) > 20, isset($hook['secret']) ? 'present' : 'missing');
if (isset($hook['id'])) {
    $step('webhooks.retrieve', fn () => $clarian->webhooks->retrieve($hook['id']), 'Webhook');
    $step('webhooks.update', fn () => $clarian->webhooks->update($hook['id'], ['events' => ['payment.completed', 'payment.failed']]), 'Webhook');
    $step('webhooks.test', fn () => $clarian->webhooks->test($hook['id']));
    $step('webhooks.list', fn () => $clarian->webhooks->list(), 'Webhook');
    $step('webhooks.deliveries', fn () => $clarian->webhooks->deliveries(), 'WebhookDelivery');
    $step('webhooks.delete', fn () => $clarian->webhooks->delete($hook['id']));
}
$expectError('webhooks.create private url', 'invalid_webhook_url', fn () => $clarian->webhooks->create(['url' => 'https://169.254.169.254/x']));

// Payment links and checkout
$link = $step('paymentLinks.create', fn () => $clarian->paymentLinks->create(['description' => 'SDK E2E', 'amount_minor' => '2500', 'currency' => 'BRL']), 'PaymentLink');
$step('paymentLinks.list', fn () => $clarian->paymentLinks->list(), 'PaymentLink');
if (isset($link['id'])) {
    $step('paymentLinks.retrieve', fn () => $clarian->paymentLinks->retrieve($link['id']), 'PaymentLink');
    $step('paymentLinks.update', fn () => $clarian->paymentLinks->update($link['id'], ['title' => 'SDK E2E 2']), 'PaymentLink');
    $step('paymentLinks.splits', fn () => $clarian->paymentLinks->splits($link['id']), 'PaymentLinkSplit');
    $step('paymentLinks.charge', fn () => $clarian->paymentLinks->charge($link['id'], ['payer_name' => 'Maria Silva', 'payer_document' => '52998224725'], $key()), 'CheckoutPaymentResult');
    if (isset($link['slug'])) {
        $step('checkout.retrieve', fn () => $clarian->checkout->retrieve($link['slug']), 'PublicCheckout');
    }
}
$step('paymentLinks.payments', fn () => $clarian->paymentLinks->payments(['limit' => 5])['items'], 'Payment');
$step('paymentLinks.stats', fn () => $clarian->paymentLinks->stats());

// Sub-accounts
$sub = $step('subaccounts.create', fn () => $clarian->subaccounts->create(['display_name' => 'SDK E2E ' . (int) (microtime(true) * 1000)]), 'SubAccount');
$step('subaccounts.list', fn () => $clarian->subaccounts->list(), 'SubAccount');
if (isset($sub['id'])) {
    $step('subaccounts.retrieve', fn () => $clarian->subaccounts->retrieve($sub['id']), 'SubAccount');
    $step('subaccounts.balances', fn () => $clarian->subaccounts->balances($sub['id']));
    $step('subaccounts.transactions', fn () => $clarian->subaccounts->transactions($sub['id']), 'Payment');
    $step('forSubaccount()->balances', fn () => $clarian->forSubaccount($sub['id'])->balances->list());
}

// Workspace admin reads
$step('whitelist.list', fn () => $clarian->whitelist->list(), 'WhitelistEntry');
$step('whitelist.settings', fn () => $clarian->whitelist->settings(), 'WhitelistSettings');
$step('bankAccounts.list', fn () => $clarian->bankAccounts->list(), 'BankAccount');
$step('apiKeys.list', fn () => $clarian->apiKeys->list(), 'ApiKey');
$step('apiKeys.audit', fn () => $clarian->apiKeys->audit(['limit' => 5]), 'ApiKeyAuditEntry');
$step('settings.retrieve', fn () => $clarian->settings->retrieve(), 'WorkspaceSettings');
$step('notifications.list', fn () => $clarian->notifications->list(), 'Notification');
$step('team.list', fn () => $clarian->team->list());
$step('workflows.list', fn () => $clarian->workflows->list());
$step('workflows.runs', fn () => $clarian->workflows->runs());
$step('approvals.list', fn () => $clarian->approvals->list());
$step('approvals.policy', fn () => $clarian->approvals->policy());
$step('treasury.list', fn () => $clarian->treasury->list());

// Client-side guards
$expectError('missing idempotency key', 'idempotencyKey is required: reuse the same key to retry safely', fn () => $clarian->pix->payouts->create(['amount_minor' => 1], ''));

foreach ($outcomes as [$name, $ok, $note]) {
    echo ($ok ? 'PASS' : 'FAIL') . " $name | $note\n";
}
echo "\nSPEC DRIFT (keys returned but not in the schema):\n";
foreach ($drift as $name => $keys) {
    echo "  $name " . implode(' ', array_keys($keys)) . "\n";
}
$failed = count(array_filter($outcomes, fn ($o) => !$o[1]));
printf("\n%d/%d passed\n", count($outcomes) - $failed, count($outcomes));
exit($failed === 0 ? 0 : 1);
