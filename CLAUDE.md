# @clarian-finance/sdk

TypeScript SDK for the Clarian Finance API. Zero runtime dependencies.

## Stack

- **Language**: TypeScript 5, strict mode
- **Build**: tsup (dual ESM + CJS)
- **Test**: Vitest
- **Target**: Node.js >= 18 (uses Web Crypto, fetch, AbortController)
- **Publish**: GitHub Packages (`@clarian-finance` scope)

## Commands

```bash
npm run build       # tsup → dist/ (ESM + CJS + .d.ts)
npm run lint        # tsc --noEmit
npm test            # vitest run
npm run test:watch  # vitest watch
```

## Architecture

```
src/
  client.ts           # Clarian class — entry point, composes resources
  http.ts             # fetch wrapper, env detection, error handling
  errors.ts           # ClarianError (status, code, detail)
  types.ts            # All public types/interfaces
  webhooks.ts         # HMAC-SHA256 signature verify + event constructor
  resources/          # One file per API resource
    rfq.ts            # quote, execute
    cash-in.ts        # create, retrieve
    cash-out.ts       # create, retrieve, dictCheck
    balances.ts       # list
    wallets.ts        # list, retrieveBalance
    transactions.ts   # list, retrieve
    webhooks-api.ts   # CRUD for webhook subscriptions
```

## Conventions

- All code, comments, commits in English
- Types exported from `types.ts`, re-exported via `index.ts`
- Each resource class takes `HttpClientConfig` in constructor
- No `any` — use `unknown` at boundaries, narrow immediately
- Immutable: no param reassignment, no mutation of passed objects
- Fail-fast: validate in constructor, throw early
- Result shapes match the api-gateway responses exactly
- Webhook signing algorithm must stay in sync with `webhook-dispatch/index.ts` in the main repo

## API Gateway

The SDK wraps the `api-gateway` edge function. The canonical reference for
endpoint shapes is the gateway source in `clarian-finance/clarian-main`.
When the gateway changes, update the corresponding resource + types here.

## Key Invariants

- `idempotencyKey` is **required** on `cashOut.create()` (gateway rejects without it)
- Environment is auto-detected from the API key prefix (`cl_live_sk_` → production, `cl_test_sk_` → sandbox)
- Webhook signature: `HMAC_SHA256(secret, timestamp + "." + body)` — hex encoded, constant-time compare
- No runtime dependencies — only `devDependencies` for build/test
