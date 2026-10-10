# Plan — Issue #63, auth slice only (register/login, hashing, JWT)

> **NOTE — TEMPORARY DOCUMENT: remove this file upon completion of the auth slice, before merging to `main`.**
> It exists only to coordinate work on #63 and must not ship in the final PR.
> Branch: `feature/auth-63-plan` (cut from `Lab5-Task2`; rebase onto `main` before PR per `TeamCharter.md` §4).

## 1. Scope lock

**In scope (auth code only):**

* `AuthController` with `POST /api/v1/auth/register` and `POST /api/v1/auth/login`.
* Secure password hashing via ASP.NET `PasswordHasher<T>` behind an `IPasswordHasher` interface (swappable to BCrypt/Argon2 later, no new hashing dependency now).
* JWT issuance + validation carrying `userId` (`sub` **and** `userId` claims) with expiry.
* Credential validation + error mapping `400 / 401 / 409` in the `ApiErrorResponse` envelope.
* Wiring (`AddAuthentication().AddJwtBearer()`, `UseAuthentication()`), Swagger Bearer support, Code-First OpenAPI regen.

**Explicitly out of scope for this slice (per owner decision):**

* Unit/integration tests (no test project in this slice).
* Full refresh-token flow / `UserSession` table with rotation + revocation.
* `/docs/code-quality/review-report.md`, Bonus 4 AI comparison table, Loom video (rest of issue #63, separate slice).
* Login rate-limit guard (beyond the generic `401` message) — file as follow-up.

Related issues owned by others and untouched here: #60 (UI/Gateway), #61 (EF Core repo/caching/SAST C#), #62 (BackgroundService/TS audit).

## 2. Documentation consulted (constraints)

| Doc | Constraint applied to this plan |
|---|---|
| `docs/requirements/requirements.md` — `SEC-001`, `FR-008` | Passwords only as hash; `grep` over DB/logs must find 0 plaintext secrets. `FR-008`: `JobSeeker` vs `Analyst` role split (role claim reserved, default `JobSeeker` on register). |
| `docs/requirements/rtm.md`, `docs/architecture/traceability-matrix.md` | `SEC-001 → Authentication/Security/Storage/Repo/DB` (audit verification); `FR-008 → JWT Bearer 401/403` (integration verification — deferred with tests, noted). |
| `docs/architecture/data-model.md` §3, §5, §6 | `User(id uuid PK, email unique, password_hash only, role)`; §5 sensitive-data handling (email/PD never to others/logs, hash never from API); §6 invariants (email unique, cascade delete user → owned data, `SavedVacancy` JobSeeker-only enforced in Core layer — untouched). |
| `docs/architecture/high-level-design.md` §3.2–3.4 | Thin `AuthController` → `IAuthenticationService`; crypto in security service; Gateway sends `Authorization: Bearer <JWT>`. |
| `docs/architecture/threat-model.md` | `THR-01`: identical generic message for “no such email” vs “bad password”, bcrypt/Argon2/PBKDF2 + future brute-force guard. `THR-02`: access-JWT lifetime 15 min. `THR-08`: mask `password`/`Authorization`/`refresh_token` in logs, never log auth bodies. `THR-09`: JWT signing key only via env / User Secrets, never committed. `THR-12`: `user_id` only from validated JWT. `THR-13`: server-side role checks (reserved). |
| `docs/architecture/decisions/ADR-002` | New `User` PK is `Guid` (`uuid`), EF Core mapping works on SQLite + MSSQL, no dialect-specific SQL. |
| `docs/api/api-design.md` §1, §4 | Base `/api/v1` (hence `/api/v1/auth/*`, not bare `/api/auth/*`); **Code-First only** — `openapi-generated.yaml` is generated, 0 lines hand-edited; errors via `ApiErrorResponse`; every operation needs `Summary/Description/OperationId/Tags` + `ProducesResponseType` + XML docs. |
| `README.md` §API | `dotnet run --project src/api` (`:3000`, `/api-docs`), regen via `--generate-openapi-and-exit`, schema completeness checks. |
| `TeamCharter.md` §4, §8 | `feature/...` branch, granular commits, self-review, PR with `Closes #63`, green CI, 1 approval, docs updated, AI disclosure rows (`\| Об'єкт \| Внесок \| ШІ \| Як перевірено \|`) + code header `AI: ...; перевірено: ...`. |

## 3. Current state gaps (verified read-only, Oct 2026)

* `src/api/Controllers/`: only `Vacancies`, `SavedVacancies`, `ApiControllerBase` (has `ErrorNotFound/BadRequest/Conflict`, **no 401 helper**). No `AuthController`.
* `src/api/Program.cs`: only `UseAuthorization()` — **no `AddAuthentication().AddJwtBearer()` / `UseAuthentication()`**.
* `WaterfallDbContext`: only `Vacancies`/`SavedVacancies`. No `User` entity, no unique-email index.
* `Waterfall.Api.csproj`: Swashbuckle + `SecurityCodeScan` present; **no `JwtBearer` package yet**; no hashing package needed (using built-in `PasswordHasher<T>`).
* `appsettings.json`: no `Jwt:*` section.
* No `*Test*` project exists — intentionally not created in this slice.

## 4. Key decisions (already agreed with owner)

1. **Route: `/api/v1/auth/*`.** Issue text says `/api/auth/*`, but `api-design.md` + both existing controllers version under `/api/v1`. Follow the convention; note the deviation from the issue text in the PR.
2. **Hashing: ASP.NET `PasswordHasher<T>` behind `IPasswordHasher`.** Avoids a new dependency now; interface keeps a later swap to BCrypt/Argon2 to one class + one DI line. `SEC-001` says “наприклад bcrypt” (example) and the issue explicitly lists PBKDF2 as acceptable.
3. **Token scope: access-JWT only.** No `UserSession`/refresh table in this slice; access token lifetime 15 min per `THR-02`.

### Hashing options — pros/cons (record of the evaluated choice)

* **A. `BCrypt.Net-Next`:** + matches HLD/data-model wording verbatim, adaptive cost, salt built-in; − extra NuGet dep, needs cost tuning, not NIST-approved primitive.
* **B. ASP.NET Identity `PasswordHasher<T>` (PBKDF2-HMACSHA256/512) — CHOSEN:** + built-in/maintained, zero new dep, NIST-approved, versioned format with rehash path; − CPU-only (less GPU-resistant than bcrypt/Argon2), not named verbatim in `SEC-001` examples (defensible: “наприклад” + issue allows PBKDF2).
* **C. Argon2 (e.g. Konscious):** + memory-hard PHC winner, best vs GPU/ASIC; − heaviest dep + config surface (`memory/iterations/parallelism`), misconfig → CPU/DoS risk, overkill for this MVP slice.

## 5. Detailed steps (build phase)

1. **DTOs** (`src/waterfall.Services/Models/Auth/`):
   `RegisterRequest { [Required, EmailAddress] Email; [Required, MinLength(8)] Password; [MaxLength] DisplayName? }`,
   `LoginRequest { Email; Password }`,
   `AuthResponse { Token; ExpiresAt; UserId Guid; Email }`.
   XML `<summary>` on every type/member (feeds Swashbuckle → Code-First YAML).
2. **Entity + context** (`src/waterfall.Repository/Entities/UserDB.cs`, `Data/WaterfallDbContext.cs`):
   `Id Guid PK; Email required; PasswordHash required; DisplayName?; Role = "JobSeeker" default; CreatedAt`.
   `DbSet<Users>` + `HasIndex(u => u.Email).IsUnique()`. Local SQLite covered by `EnsureCreated` in seeder; add MSSQL-migration note in PR.
3. **Security core** (`src/waterfall.Services/`):
   * `Interfaces/IPasswordHasher.cs { string Hash(string); bool Verify(string hashed, string provided); }`
   * `AspNetPasswordHasher : IPasswordHasher` wrapping `PasswordHasher<UserDB>` (handle `SuccessRehashNeeded` → rehash path).
   * `Interfaces/IJwtTokenService.cs { string Generate(UserDB); ClaimsPrincipal? Validate(string); }` — claims `sub = userId` **and** `userId = userId` (satisfies “`sub` or `userId`”), plus `email`, `role`, `jti`, `exp = 15 min`; HMAC-SHA256, `ClockSkew = Zero`.
   * `Interfaces/IAuthenticationService.cs { RegisterAsync; LoginAsync }` — duplicate email → caller maps to `409`; unknown email and bad password → same generic `401` message (`THR-01`).
4. **Controller** (`src/api/Controllers/AuthController.cs`, `[Route("api/v1/auth")]`, `[AllowAnonymous]`):
   `POST register → 201 + AuthResponse`; `POST login → 200 + AuthResponse`. Thin (delegate to service), `[SwaggerOperation(Summary/Description/OperationId/Tags)]`, `ProducesResponseType` for `200/201/400/401/409` with `ApiErrorResponse`.
5. **Base + wiring:**
   * `ApiControllerBase`: add `ErrorUnauthorized(string)` → `code: "UNAUTHORIZED"` envelope (mirrors existing helpers).
   * `Program.cs`: `AddAuthentication(JwtBearer).AddJwtBearer(...)` (issuer/audience/lifetime/signing key from `Jwt:*`), `app.UseAuthentication()` **before** `UseAuthorization()`; Swagger `Bearer` security definition + requirement.
   * `ServiceCollectionExtensions`: register `IPasswordHasher`, `IJwtTokenService`, `IAuthenticationService` (+ minimal `IUserRepository` or direct `DbContext` use — prefer repository for testability later).
   * `appsettings.json`: add non-secret `Jwt:{Issuer,Audience,ExpiresMinutes:15}`; real `Jwt:Key` **only** via environment (`Jwt__Key`) / User Secrets — never commit (`THR-09`), `.gitignore` check.
6. **Logging hygiene (`THR-08`):** log only `email + outcome + RequestId`; never `password`, `Authorization` header, or auth request bodies.
7. **Regen + manual smoke (no tests in this slice):**
   `dotnet run --project src/api -- --generate-openapi-and-exit`; confirm the YAML diff only adds `/v1/auth/*` + `UNAUTHORIZED` schema.
   Smoke via Swagger `/api-docs`: register → `201`, re-register same email → `409`, login ok → `200` (decode JWT, assert `sub`/`userId` + ~15-min `exp`), login bad (unknown email and wrong password) → identical generic `401`; `grep` DB dump for plaintext password = 0 (`SEC-001` spot check).

## 6. Files to touch (build phase)

* New: `Repository/Entities/UserDB.cs`; `Services/{Interfaces/IPasswordHasher.cs, Interfaces/IJwtTokenService.cs, Interfaces/IAuthenticationService.cs, <impls>}`; `Services/Models/Auth/*.cs`; `api/Controllers/AuthController.cs`.
* Edit: `Repository/Data/WaterfallDbContext.cs`; `Services/Extensions/ServiceCollectionExtensions.cs`; `api/Program.cs`; `api/Controllers/ApiControllerBase.cs` (+401 helper); `appsettings.json` (non-secret part); `docs/api/api-design.md` (short Auth addendum, no YAML hand-edits).
* Do NOT touch in this slice: `docs/api/openapi-generated.yaml` (generated), `docs/code-quality/*`, test projects, `SavedVacancyDB` composite-key rework.

## 7. Risks / follow-ups (do not fix silently in this slice)

* Route deviation (`/api/auth` in issue vs `/api/v1/auth` implemented) — call out in PR description.
* `EnsureCreated` vs migrations for the unique-email index on MSSQL — document; no migration-framework switch here.
* Refresh/rotation/revocation (`UserSession`, `THR-02` one-time refresh) deferred — leave a `TODO(#63-followup)` referencing `data-model.md` §3.
* Login brute-force guard (`THR-01` “5 fails → backoff”) deferred with tests — at minimum the generic `401` ships now; file follow-up issue.
* Dirty tree note: `docs/api/openapi-generated.yaml` is currently modified on the base branch (`Lab5-Task2`) — **do not commit it** in the auth PR; regen from a clean base instead.

## 8. Open points for owner before build

* Password policy: `MinLength(8)` sufficient, or require complexity classes?
* Register role: always `JobSeeker`, or allow `Analyst` (affects `FR-008`/`THR-13`)?
* JWT key plumbing: `Jwt__Key` env var + User Secrets acceptable?

## 9. Definition of done (this slice)

* [ ] DTOs + entity + services + controller + wiring implemented per §5.
* [ ] Manual smoke (§5.7) passes; OpenAPI regen diff clean.
* [ ] Self-review per `TeamCharter.md` §4 (logic, edge cases: duplicate email, bad creds, expired/tampered token, no secret leaks).
* [ ] PR `feature/auth-*` → `main` with `Closes #63` (or `part of #63` if the review/Bonus slice follows separately), CI green, 1 approval.
* [ ] **DELETE THIS FILE** (`docs/plan-63-auth.md`) before merge.
