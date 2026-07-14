# Agent Guidelines for @clarian-finance/sdk

## When modifying this SDK

1. **Read the gateway first.** Every resource method maps 1:1 to an endpoint in
   the `api-gateway` source (in `clarian-finance/clarian-main`).
   Check the real shapes before changing types.

2. **Keep types in sync.** All public types live in `src/types.ts`. If a gateway
   response field changes, update the type AND the resource method that uses it.

3. **No runtime dependencies.** This SDK uses only Web APIs (fetch, crypto.subtle,
   AbortController, TextEncoder). Never add an npm dependency for something the
   platform provides.

4. **Webhook signing is security-critical.** The algorithm in `src/webhooks.ts`
   must exactly match `webhook-dispatch/index.ts`. Changes to either file must
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

Each resource follows the same shape:

```typescript
import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";

export class ResourceName {
  constructor(private readonly config: HttpClientConfig) {}

  async methodName(params: ParamsType): Promise<ReturnType> {
    const res = await request<GatewayResponse>(this.config, "METHOD", "path", body);
    return res.relevantField;
  }
}
```

Then wire it in `src/client.ts` and re-export types from `src/index.ts`.

## Testing

- Unit tests in `src/__tests__/`
- Webhook tests verify the real signing algorithm (not mocked)
- Client tests verify config validation and environment detection
- No network calls in tests — mock fetch if testing HTTP paths

## Publishing

Published to GitHub Packages under `@clarian-finance` scope.
Bump version in `package.json`, then `npm publish`.
