# Contributing to WinLANTransfer

Thank you for your interest in contributing! Everyone is welcome here — whether you're fixing a typo, reporting a bug, or building a big new feature.

## Ways to Contribute

- 🐞 **Report bugs** — open an issue with steps to reproduce, expected vs. actual behavior, Windows version, and app version.
- 💡 **Suggest features** — open an issue describing the problem it solves and how you'd use it.
- 🛠️ **Submit pull requests** — fork, branch, code, test, and open a PR.
- 💬 **Join discussions** — share ideas, help others, and review PRs.

## Getting Started

1. Install **Visual Studio 2022 17.8+** with the **.NET Desktop Development** and **Windows App SDK** workloads, plus **.NET 8 SDK**.
2. Fork and clone:
   ```bash
   git clone https://github.com/uponatime2019/WinLANTransfer.git
   cd WinLANTransfer
   ```
3. Build and run:
   ```bash
   dotnet build "WinLANTransfer.csproj" -p:Platform=x64
   dotnet run --project "WinLANTransfer.csproj" -p:Platform=x64
   ```
4. Open `WinLANTransfer.sln` in Visual Studio for UI work (XAML Hot Reload recommended).

## Pull Request Guidelines

- Keep PRs focused — one feature/fix per PR.
- Describe *what* and *why*; link related issues.
- Ensure `dotnet build "WinLANTransfer.csproj" -p:Platform=x64` passes with **0 errors**.
- Match existing code style (nullable enabled, file-scoped namespaces where already used).
- Update `README.md` if you change user-facing behavior.

## Bug Reports

Please include:

- App version (see Release tag, e.g. `v1.0.0`)
- Windows version (`winver`)
- Steps to reproduce + screenshots if UI-related
- Logs from `%LocalAppData%\WinLANTransfer\` if relevant

## Code of Conduct

Be kind, respectful, and constructive. Harassment, spam, or hate speech will not be tolerated. We're all here to build something useful together. 💙
