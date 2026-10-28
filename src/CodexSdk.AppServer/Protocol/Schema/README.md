# Codex app-server protocol schema

These schema files come from the official Codex CLI npm package `@openai/codex` version `0.163.0`. The `codex-0.163.0.json` bundle contains the stable and v2 app-server protocol definitions; `codex-0.163.0-v2.json` is the extracted v2 schema.

Regenerate the pinned schema files from the repository root with the CLI fixture:

```sh
tests/fixtures/codex-app-server/node_modules/.bin/codex app-server generate-json-schema --experimental --out src/CodexSdk.AppServer/Protocol/Schema
```

The bundle declares 172 client requests, 11 server requests, and 85 server notifications. The generator also adds typed notification streams for `rawResponseItem/completed` and `rawResponse/completed`. The client performs the `initialize` request and sends the `initialized` notification as part of its connection handshake.

`CodexSdk.AppServer.Generator` reads the bundled schema at build time and emits the protocol models, one public `{Method}Async` client method per request, request-handler registration methods, and `Subscribe{Method}` streams with typed notification parameters. The `CodexSdk.AppServer` NuGet package includes the pinned schema under `schema/`.
