# @clarian-finance/sdk

SDK TypeScript da API v2 da Clarian: PIX de entrada e saída, cotação e conversão entre real e stablecoins, carteiras e envio de cripto, clientes e KYC, links de pagamento, subcontas e webhooks. Sem dependências em tempo de execução. Node 18+, Bun, Deno e navegadores com `fetch`.

[Read in English](./README.md)

## Instalação

```bash
npm install github:clarian-finance/sdk#v1.0.0
```

## Começo rápido

```ts
import { Clarian } from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: process.env.CLARIAN_API_KEY!, // cl_test_sk_… (sandbox) ou cl_live_sk_… (produção)
  workspaceId: process.env.CLARIAN_WORKSPACE_ID!,
});

await clarian.ping();
const saldos = await clarian.balances.list();
```

O prefixo da chave escolhe o ambiente: `cl_test_sk_` chama `/v1/test`, `cl_live_sk_` chama `/v1/live`. Crie chaves no painel em Negócios > API (pede 2FA) e dê a cada chave só as permissões que ela usa.

## Valores

Todo valor é um inteiro em unidades mínimas, enviado e devolvido como string. Real tem escala 2 (R$ 10,00 é `"1000"`), USDT e USDC escala 6 (1 USDT é `"1000000"`). Passe `string`, `number` ou `bigint`, nunca número com casas decimais. Cada saldo traz a sua `scale`.

## Receber PIX (cash-in)

```ts
const cobranca = await clarian.pix.payins.create({ amount_minor: "25000", currency: "BRL" }, "pedido-981");
cobranca.pix_qr_code; // copia-e-cola para o pagador
```

O QR vale 30 minutos. O valor passa de `pending` para `available` quando o PIX liquida.

## Enviar PIX (cash-out)

```ts
const taxa = await clarian.pix.payouts.fee({ amount_minor: "10000" });
const saque = await clarian.pix.payouts.create(
  { amount_minor: "10000", currency: "BRL", pix_key: "liquidacao@suaempresa.com" },
  "saque-2026-10-10-001",
);
```

Pela API, o dinheiro vai para a chave de liquidação cadastrada na sua conta. Cadastre-a uma vez no painel; antes disso a chamada responde 403 `settlement_key_required`. Valor e taxa saem do `available` na hora e voltam se o PIX falhar.

## Cotar e converter

A direção é sempre `base_currency` → `quote_currency`: você paga `base_amount_minor` na moeda base e recebe a outra.

```ts
// Comprar USDT com R$ 500,00
const cotacao = await clarian.conversions.quote({ base_currency: "BRL", quote_currency: "USDT", base_amount_minor: "50000" });
cotacao.quote_amount_minor; // USDT que você recebe (escala 6), já sem taxas
const resultado = await clarian.conversions.execute(cotacao.id!, "conversao-981");
```

Uma cotação converte uma vez só: repetir dá 409 `quote_not_open`, e uma vencida dá 410 `quote_expired`. Para vender USDT por real, inverta as moedas.

## Webhooks

```ts
const hook = await clarian.webhooks.create({ url: "https://exemplo.com/clarian", events: ["payment.completed", "payment.failed"] });
hook.secret; // aparece só aqui: guarde
```

Verifique toda entrega sobre o corpo bruto antes de confiar nela:

```ts
import { constructWebhookEvent } from "@clarian-finance/sdk";

const evento = await constructWebhookEvent(await request.text(), request.headers, process.env.CLARIAN_WEBHOOK_SECRET!);
if (evento.type === "payment.completed") {
  const { payment_id, amount_minor } = evento.data;
}
```

A assinatura é HMAC-SHA256 de `${x-clarian-timestamp}.${corpoBruto}`; entregas com mais de 5 minutos são recusadas. Deduplique por `evento.id` e responda 2xx rápido.

## Sandbox

Com chave `cl_test_sk_` nada chega a banco ou blockchain. Liquide ou falhe o que estiver pendente:

```ts
await clarian.pix.payins.simulate(cobranca.id!);
await clarian.pix.payouts.simulate(saque.id!, { status: "failed", reason: "teste" });
```

## Erros, novas tentativas e idempotência

Respostas fora de 2xx lançam `ClarianError` com `status` e o `code` estável da API. Criações que movem dinheiro exigem chave de idempotência: repetir com a mesma chave devolve o mesmo pagamento e nunca paga duas vezes. O SDK tenta de novo em 429/502/503/504 e erro de rede (até `maxRetries`, padrão 2), mas só em leituras, exclusões e chamadas com chave de idempotência. O limite é de 120 requisições por minuto por chave.

## Fora da API

O que exige o segundo fator de uma pessoa fica no painel: convidar ou mudar membros da equipe, votar aprovações, criar ou rotacionar chaves de API, salvar contas de saque e endereços da lista de saque, e mudar configurações da conta. As leituras correspondentes estão no SDK.

A referência completa de métodos está no [README em inglês](./README.md#reference).
