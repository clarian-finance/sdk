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
openapi.json          # contract, copied from the main repo
scripts/gen-types.mjs # openapi.json -> src/schema.ts
src/
  client.ts      # Clarian class, composes resources, forSubaccount()
  http.ts        # fetch wrapper: /v1/{live|test}, data unwrap, safe retries
  errors.ts      # ClarianError (status, code)
  schema.ts      # generated types
  types.ts       # config and hand-written types
  resources.ts   # one class per API area
  webhooks.ts    # HMAC-SHA256 verify + constructWebhookEvent
e2e/sandbox.ts   # every method against a sandbox API + spec drift report
```

## Conventions

- All code, comments, commits in English
- Types exported from `types.ts`, re-exported via `index.ts`
- Each resource class takes `HttpClientConfig` in constructor
- No `any` — use `unknown` at boundaries, narrow immediately
- Immutable: no param reassignment, no mutation of passed objects
- Fail-fast: validate in constructor, throw early
- Webhook signing must stay in sync with `signWebhook` in the main repo's `apps/api/src/Crypto.res`

## API

The SDK wraps the v2 API (`apps/api` in `clarian-finance/clarian`), paths
`/v1/{live|test}/…`. Replies are `{ data }`; errors `{ error, message? }`.

## Key Invariants

- `idempotencyKey` is **required** on every money-moving create (payins, payouts, crypto send, link charges, checkout pay)
- Environment is auto-detected from the API key prefix (`cl_live_sk_` → `/v1/live`, `cl_test_sk_` → `/v1/test`)
- Webhook signature: `HMAC_SHA256(secret, timestamp + "." + body)` — hex encoded, constant-time compare
- Only reads, deletes and idempotent creates are retried
- No runtime dependencies — only `devDependencies` for build/test
