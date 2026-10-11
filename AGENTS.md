# Agent Guidelines for @clarian-finance/sdk

## When modifying this SDK

1. **The OpenAPI file is the contract.** `openapi.json` is a copy of
   `apps/b2c/public/openapi.json` in the main repo (served at
   `https://app.clarian.finance/openapi.json`). Copy it, run `npm run gen`
   (writes `src/schema.ts`, never edit by hand), then adjust `src/resources.ts`.

2. **Prove shapes against a running API.** `npm run e2e` calls every method on a
   sandbox key and prints keys the server returned that the schema lacks. Fix
   drift in the main repo's spec, not with casts here.

3. **No runtime dependencies.** This SDK uses only Web APIs (fetch, crypto.subtle,
   AbortController, TextEncoder). Never add an npm dependency for something the
   platform provides.

4. **Webhook signing is security-critical.** The algorithm in `src/webhooks.ts`
   must exactly match `signWebhook` in the main repo's `apps/api/src/Crypto.res`. Changes to either file must
   be cross-verified. The comparison must be constant-time.

5. **Test after every change.** Run `npm test` — all tests must pass. Build with
   `npm run build` and type-check with `npm run lint` before committing.

## Code style

- TypeScript strict, no `any`
- `const` over `let`, never `var`
- Early return / guard clauses, no nested `if/else`
- Functional: `map`/`filter` over loops where natural
- No comments unless explaining a non-obvious WHY
- English only in code and comments

## Resource pattern

All resources live in `src/resources.ts`: one class per API area, each method one
`request<T>(config, method, path, { body, query, idempotencyKey })` call that
returns the reply's `data`. Money-moving creates take a required
`idempotencyKey`. Wire new classes in `src/client.ts`.

## Testing

- Unit tests in `src/__tests__/`
- Webhook tests verify the real signing algorithm (not mocked)
- Client tests verify config validation and environment detection
- No network calls in tests — mock fetch if testing HTTP paths

## Publishing

Installed from a git tag (`github:clarian-finance/sdk#vX.Y.Z`); the `prepare`
script builds `dist/` on install. Publishing to GitHub Packages is blocked by
the org policy until an admin allows package writes.
