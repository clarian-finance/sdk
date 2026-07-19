# @clarian-finance/sdk

SDK oficial em TypeScript para a API da [Clarian Finance](https://clarian.finance).

> **Read in English:** [README.md](./README.md)
>
> **Go:** o SDK oficial em Go fica em [`go/`](./go/), instale com `go get github.com/clarian-finance/sdk/go`.

## Instalação

```bash
npm install github:clarian-finance/sdk
```

## Início rápido

```typescript
import { Clarian } from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: "cl_test_sk_sua_chave_aqui",
  workspaceId: "seu-workspace-uuid",
});

// Health check
const ping = await clarian.ping();
console.log(ping.environment); // "production" ou "sandbox"

// Consultar saldos
const saldos = await clarian.balances.list();
console.log(saldos);

// Criar um cash-in PIX (depósito) — chave de idempotência obrigatória
const pedido = await clarian.cashIn.create({
  amount: 100.00,
  payer: {
    name: "Maria Silva",
    document: { number: "12345678900" },
  },
}, "deposito-001");
console.log(pedido.pix.copy_paste); // código copia-e-cola do PIX
```

## Autenticação

As chaves de API seguem o padrão `cl_{ambiente}_sk_{segredo}`:

| Prefixo | Ambiente |
|---------|----------|
| `cl_live_sk_` | Produção (rotas `/live/`) |
| `cl_test_sk_` | Sandbox (rotas `/test/`) |

O SDK detecta o ambiente automaticamente pelo prefixo da chave.

## Recursos

### Ping

```typescript
const ping = await clarian.ping();
// { ok: true, environment: "production", master_account_id: "...", scope: "master" }
```

### RFQ (Cotações)

```typescript
// Solicitar uma cotação
const cotacao = await clarian.rfq.quote({
  base_currency: "BRL",
  quote_currency: "USDT",
  amount: 5000,
  amount_currency: "BRL",
});

// Executar a cotação (em breve)
// const resultado = await clarian.rfq.execute(
//   { quote_id: cotacao.quote_id },
//   "rfq-exec-001",
// );
```

### PIX Cash-in (Depósitos)

```typescript
// Criar QR code para depósito (chave de idempotência obrigatória)
const deposito = await clarian.cashIn.create({
  amount: 50.00,
  payer: {
    name: "João Santos",
    document: { number: "12345678900" },
  },
  description: "Fatura #42",
  expiration_seconds: 3600,
}, "deposito-fatura-42");

// Consultar um depósito
const pedido = await clarian.cashIn.retrieve(deposito.id);
```

### PIX Cash-out (Pagamentos)

```typescript
// Consulta DICT (pré-visualizar o destinatário)
const dict = await clarian.cashOut.dictCheck({
  pix_key: "email@exemplo.com",
});

// Criar um pagamento (chave de idempotência é obrigatória)
const pagamento = await clarian.cashOut.create({
  amount: 200.00,
  pix_key: "email@exemplo.com",
  description: "Pagamento de fornecedor",
}, "chave-idempotencia-unica");

// Consultar um pagamento
const pedido = await clarian.cashOut.retrieve(pagamento.id);
```

### Wallets (Carteiras)

```typescript
// Listar todas as carteiras
const carteiras = await clarian.wallets.list();

// Filtrar por rede
const carteiras_tron = await clarian.wallets.list("tron");

// Saldo on-chain em tempo real
const saldo = await clarian.wallets.retrieveBalance(carteiras[0].id);
```

### Transactions (Transações)

```typescript
// Listar transações recentes
const { transactions } = await clarian.transactions.list({ limit: 20 });

// Filtrar por tipo
const depositos = await clarian.transactions.list({ type: "pix_in" });

// Consultar uma transação
const tx = await clarian.transactions.retrieve("tx-uuid");
```

### Balances (Saldos)

```typescript
const saldos = await clarian.balances.list();
// [{ currency: "BRL", available: 1500.00, pending: 200.00, locked: 0 }]
```

### Webhooks

```typescript
// Criar uma assinatura de webhook
const { subscription, secret } = await clarian.webhooks.create({
  url: "https://seuapp.com/webhooks/clarian",
  events: ["pix_payin.completed", "pix_payout.completed"],
  description: "Webhook principal",
});
// Guarde o `secret` com segurança - exibido apenas uma vez

// Listar assinaturas
const subs = await clarian.webhooks.list();

// Atualizar
await clarian.webhooks.update(subscription.id, { is_active: false });

// Remover
await clarian.webhooks.delete(subscription.id);
```

### Testes no sandbox

Helpers exclusivos do sandbox (chaves `cl_test_sk_`). Fora do sandbox, lançam erro antes de qualquer chamada de rede.

Chaves PIX mágicas do rail de payout no sandbox:

| Chave | Comportamento |
|-------|---------------|
| `fail@sandbox.clarian` | Payout falha e o valor é estornado |
| `pending@sandbox.clarian` | Payout fica pendente até você simular |

```typescript
import {
  Clarian,
  SANDBOX_FAIL_PIX_KEY,
  SANDBOX_PENDING_PIX_KEY,
  signWebhookPayload,
} from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: "cl_test_sk_sua_chave_aqui",
  workspaceId: "seu-workspace-uuid",
});

// Simular liquidação de um cash-in (status padrão: completed)
const pago = await clarian.sandbox.simulateCashIn(deposito.id);
// Ou: "expired" | "failed"
await clarian.sandbox.simulateCashIn(deposito.id, "expired");

// Criar um payout que fica pendente e depois completar
const payoutPendente = await clarian.cashOut.create(
  { amount: 10, pix_key: SANDBOX_PENDING_PIX_KEY },
  "payout-sim-001",
);
const liquidado = await clarian.sandbox.simulateCashOut(payoutPendente.id, "completed");

// Forçar um payout com falha
await clarian.cashOut.create(
  { amount: 10, pix_key: SANDBOX_FAIL_PIX_KEY },
  "payout-fail-001",
);

// Enfileirar um webhook de exemplo em uma assinatura
const { enqueued, delivery_id } = await clarian.sandbox.sendWebhookEvent(
  subscription.id,
  "pix_payin.completed",
);

// Reenviar uma entrega anterior
const { deliveryId } = await clarian.sandbox.resendWebhookDelivery(delivery_id!);

// Assinar um payload localmente para testar o handler de webhooks
const timestamp = new Date().toISOString();
const body = JSON.stringify({ amount: 100, status: "completed" });
const signature = await signWebhookPayload(body, timestamp, webhookSecret);
```

## Verificação de Webhooks

Verifique assinaturas de webhooks recebidos para garantir autenticidade:

```typescript
import {
  constructWebhookEvent,
  extractWebhookHeaders,
} from "@clarian-finance/sdk";

// Exemplo com Express / Node.js
app.post("/webhooks/clarian", async (req, res) => {
  const headers = extractWebhookHeaders(req.headers);
  const rawBody = req.body; // deve ser a string crua, não JSON parseado

  try {
    const event = await constructWebhookEvent(rawBody, headers, WEBHOOK_SECRET);
    console.log(`Recebido ${event.event}`, event.data);
    res.sendStatus(200);
  } catch (err) {
    console.error("Falha na verificação do webhook:", err);
    res.sendStatus(401);
  }
});
```

### Formato da assinatura

| Header | Descrição |
|--------|-----------|
| `X-Clarian-Signature` | HMAC-SHA256 hex de `{timestamp}.{body}` |
| `X-Clarian-Timestamp` | Timestamp ISO 8601 do envio |
| `X-Clarian-Event` | Tipo do evento (ex: `pix_payin.completed`) |
| `X-Clarian-Delivery-Id` | UUID único da entrega |
| `X-Clarian-Idempotency-Key` | Chave de idempotência para dedup |
| `X-Clarian-Attempt` | Número da tentativa de entrega |

## Tratamento de erros

```typescript
import { Clarian, ClarianError } from "@clarian-finance/sdk";

try {
  await clarian.cashOut.create({ amount: 100, pix_key: "..." }, "chave-1");
} catch (err) {
  if (err instanceof ClarianError) {
    console.error(err.status);  // 400, 401, 403, 429...
    console.error(err.code);    // "invalid_amount", "rate_limited"...
    console.error(err.detail);  // detalhe legível
  }
}
```

## Exemplos com cURL

```bash
# Ping (verificação de autenticação)
curl -X GET \
  "https://api.clarian.finance/functions/v1/api-gateway/test/ping" \
  -H "Authorization: Bearer cl_test_sk_SUA_CHAVE" \
  -H "X-Workspace-Id: SEU_WORKSPACE_ID"

# Criar um cash-in PIX
curl -X POST \
  "https://api.clarian.finance/functions/v1/api-gateway/test/pix/payins" \
  -H "Authorization: Bearer cl_test_sk_SUA_CHAVE" \
  -H "X-Workspace-Id: SEU_WORKSPACE_ID" \
  -H "Content-Type: application/json" \
  -d '{"amount": 10, "payer": {"name": "Teste", "document": {"number": "12345678900"}}}'

# Consultar saldos
curl -X GET \
  "https://api.clarian.finance/functions/v1/api-gateway/test/account/balances" \
  -H "Authorization: Bearer cl_test_sk_SUA_CHAVE" \
  -H "X-Workspace-Id: SEU_WORKSPACE_ID"
```

## Licença

MIT
