# Contributing to RouterPlus

Cảm ơn bạn quan tâm đóng góp cho RouterPlus. Tài liệu này mô tả quy trình phát triển, code style, testing, và PR process.

## Yêu cầu môi trường

- Windows 10/11 x64
- .NET 8 SDK (dự án có `.dotnet/` local SDK)
- Git
- Chrome (để test auto-login flows)
- Node.js/npm (để chạy 9Router local)

## Setup

```powershell
git clone https://github.com/hieuck/9router-plus.git
cd 9router-plus
.\.dotnet\dotnet.exe build RouterPlus.sln --configuration Release
```

Build phải thoát với **0 errors, 0 warnings** (`TreatWarningsAsErrors` đang bật).

## Quy trình phát triển

### Branching

- Cut branch từ `main`: `feat/<topic>` cho feature, `fix/<topic>` cho bug
- `main` được protect — luôn qua PR, không push trực tiếp
- Parallel/high-risk work nên dùng worktree

### Commit message format (Conventional Commits)

```
<type>: <short description>

<optional body>
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

- Test + implementation đi trong **cùng commit**
- Không `git add .` — stage specific files only
- Không `--no-verify`; không `--force` trừ khi own branch exclusively

### Workflow phases

| Phase | What happens | Proof required |
|---|---|---|
| Kickoff | Hiểu objective, đọc source, identify risks | Written summary |
| Ready | One objective, one failing test (red) | Red test exists |
| Implement | Minimal code to pass test, 80%+ coverage | `dotnet test` green |
| Close | Commit, push, open PR | PR cites test evidence |

Commit at every phase boundary. Never carry uncommitted changes across phases.

## Code style

- Nullable và ImplicitUsings enabled — không `#nullable disable`
- Immutable patterns preferred; không mutate shared state
- Functions ≤ 50 lines, files ≤ 800 lines — extract khi tới limit
- No hardcoded secrets; DPAPI (`CurrentUser`) cho API keys at rest
- Handle errors explicitly at every level; never silently swallow

## Testing (TDD)

- Write red test **trước** implementation code
- Tests trong matching project dưới `tests/`
- xUnit + Moq — never reach real filesystem/registry trong unit tests
- Behavior-style assertions (describe what system does, not how)
- Coverage target: **80%+** across all test projects

```powershell
.\.dotnet\dotnet.exe build RouterPlus.sln --configuration Release
.\.dotnet\dotnet.exe test RouterPlus.sln --configuration Release --no-build
```

### Biến môi trường test

| Biến | Giá trị | Tác dụng |
|---|---|---|
| `ROUTERPLUS_RUN_E2E` | `1` | Kích hoạt E2E tests (mặc định skip) |
| `ROUTERPLUS_DEBUG_AUTOLOGIN` | `1` | Debug auto-login flow |

## Security

- Không secrets trong source. Validate tất cả external inputs.
- API keys stored via DPAPI vault; không log.
- Trước commit: confirm no credentials, tokens, passwords staged.
- Báo lỗi security qua private channel (xem `SECURITY.md`)

## PR process

1. Analyze full commit history (`git diff main...HEAD`)
2. Điền `.github/PULL_REQUEST_TEMPLATE.md`
3. Title ≤ 70 characters
4. PR description gồm: what changed, why, test evidence
5. CI phải pass trước khi merge

## Build & Release

- Build: `scripts/build.ps1`
- Version bump: `scripts/bump-version.ps1`
- Release: GitHub Actions workflow (`.github/workflows/release.yml`)
- Self-update chỉ install checksum-verified (`*.sha256`) stable packages
