# Codex CLI startup tests

The startup component tests use the pinned npm Codex package, a real Node.js executable, and a Unix login shell. Install Node.js and the fixture dependency from the repository root:

```sh
npm ci --prefix tests/fixtures/codex-cli --ignore-scripts --no-audit --no-fund
dotnet test --project tests/CodexSdk.Tests --filter-class '*CodexCliStartupTests'
```

The tests query `--version` and require no Codex credentials. They cover inherited and supplied environments, relative shell PATH entries, explicit executable paths, and the missing-Node failure condition. An isolated test process runs the explicit inheritance probe automatically.

测试使用固定版本的 npm Codex、实际 Node.js 和 Unix login shell。测试只执行版本查询，测试文件保存在项目目录内，调用方和宿主进程的环境保持原值。
