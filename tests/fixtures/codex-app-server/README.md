# Codex app-server tests

The app-server component tests use the pinned npm Codex package and run the real `codex app-server` process. Install Node.js and the fixture dependency from the repository root:

```sh
npm ci --prefix tests/fixtures/codex-app-server --ignore-scripts --no-audit --no-fund
dotnet test --project tests/CodexSdk.AppServer.Tests --filter-class '*TransportProcessTests'
```

The fixture pins `@openai/codex` to `0.163.0`. The component test starts the CLI's app-server mode and verifies its handshake and process cleanup. It runs on macOS and Linux and does not require Codex credentials.

测试使用固定版本的 npm Codex CLI，并启动实际的 `codex app-server` 进程。测试文件保存在项目目录内，运行测试不需要 Codex 凭据。
