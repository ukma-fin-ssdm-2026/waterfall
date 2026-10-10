# Plan — Issue #63, auth slice only (register/login, hashing, JWT)

> **AGENT WORKING NOTES — not meant for careful reading.** This file is verbose scratch coordination
> written by an AI build agent during implementation. For the human-readable summary, see the PR
> description. This file will be deleted before merge.

> **NOTE — TEMPORARY DOCUMENT: remove this file upon completion of the auth slice, before merging to `main`.**
> It exists only to coordinate work on #63 and must not ship in the final PR.
> Branch: `feature/auth` (cut from `Lab5-Task2`; rebase onto `main` before PR per `TeamCharter.md` §4).
>
> ## 0. Build status (updated 10-Oct-2026, step-by-step with owner)
>
> DONE + committed: §5.1 DTOs (`80ce892`), §5.2 entity/context (`8f67812`),
> §5.3 security core (`7d6237d`, incl. timing fix + review hardening),
> §5.4 controller + 401 helper (`537c96f`). All builds 0 warnings / 0 errors.
>
> REMAINING: §5.5 wiring (JwtBearer/Swagger-Bearer/DI/`appsettings`/key fail-fast),
> §5.6 logging-hygiene check, §5.7 OpenAPI regen + manual smoke. Details in §5.

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
| `docs/requirements/requirements.md` — `SEC-001`, `FR-008` | Passwords only as hash; `grep` over DB/logs must find 0 plaintext secrets. `FR-008`: `JobSeeker` vs `Analyst` role split via `UserRole` enum (int on the wire, like `JobSource`; `Analyst` self-registration accepted — server-side `[Authorize(Roles)]` remains the real control, `THR-13`). |
| `docs/requirements/rtm.md`, `docs/architecture/traceability-matrix.md` | `SEC-001 → Authentication/Security/Storage/Repo/DB` (audit verification); `FR-008 → JWT Bearer 401/403` (integration verification — deferred with tests, noted). |
| `docs/architecture/data-model.md` §3, §5, §6 | `User(id uuid PK, email unique, password_hash only, role)`; §5 sensitive-data handling (email/PD never to others/logs, hash never from API); §6 invariants (email unique, cascade delete user → owned data, `SavedVacancy` JobSeeker-only enforced in Core layer — untouched). |
| `docs/architecture/high-level-design.md` §3.2–3.4 | Thin `AuthController` → `IAuthenticationService`; crypto in security service; Gateway sends `Authorization: Bearer <JWT>`. |
| `docs/architecture/threat-model.md` | `THR-01`: identical generic message for “no such email” vs “bad password”, bcrypt/Argon2/PBKDF2 + future brute-force guard. `THR-02`: access-JWT lifetime 15 min. `THR-08`: mask `password`/`Authorization`/`refresh_token` in logs, never log auth bodies. `THR-09`: JWT signing key only via env / User Secrets, never committed. `THR-12`: `user_id` only from validated JWT. `THR-13`: server-side role checks (reserved). |
| `docs/architecture/decisions/ADR-002` | New `User` PK is `Guid` (`uuid`), EF Core mapping works on SQLite + MSSQL, no dialect-specific SQL. |
| `docs/api/api-design.md` §1, §4 | Base `/api/v1` (hence `/api/v1/auth/*`, not bare `/api/auth/*`); **Code-First only** — `openapi-generated.yaml` is generated, 0 lines hand-edited; errors via `ApiErrorResponse`; every operation needs `Summary/Description/OperationId/Tags` + `ProducesResponseType` + XML docs. |
| `README.md` §API | `dotnet run --project src/api` (`:3000`, `/api-docs`), regen via `--generate-openapi-and-exit`, schema completeness checks. |
| `TeamCharter.md` §4, §8 | `feature/...` branch, granular commits, self-review, PR with `Closes #63`, green CI, 1 approval, docs updated, AI disclosure rows (`\| Об'єкт \| Внесок \| ШІ \| Як перевірено \|`) + code header `AI: ...; перевірено: ...`. |

## 3. Current state gaps (originally verified read-only, Oct 2026 — status 10-Oct)

* [x] `src/api/Controllers/`: `AuthController` added (`537c96f`); `ApiControllerBase` gained `ErrorUnauthorized` → `UNAUTHORIZED`.
* [ ] `src/api/Program.cs`: still only `UseAuthorization()` — **REMAINING (§5.5)**.
* [x] `WaterfallDbContext`: `DbSet<Users>` added (`8f67812`); `UserDB` has `[Index(Email, IsUnique)]`.
* [x] `Waterfall.Api.csproj`: `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12` added; hashing needs NO package (`PasswordHasher` is in the shared framework — `NU1510` verified, ref removed).
* [ ] `appsettings.json`: still no `Jwt:*` — **REMAINING (§5.5)**.
* No `*Test*` project — intentionally not created in this slice.

## 4. Key decisions (agreed with owner during build)

1. **Route: `/api/v1/auth/*`.** Issue text says `/api/auth/*`, but `api-design.md` + both existing controllers version under `/api/v1`. Follow the convention; note the deviation from the issue text in the PR.
2. **Hashing: ASP.NET `PasswordHasher<object>` behind `IPasswordHasher`.** Zero new deps (shared framework, `NU1510`-verified); swap to BCrypt/Argon2 = one class + one DI line (each impl owns a dummy hash of its own format, so the swap stays complete). `SEC-001` says “наприклад bcrypt” (example) and the issue explicitly lists PBKDF2 as acceptable. PBKDF2-HMACSHA256 proven live: same password → different 84-char hashes, both verify, wrong → `Failed`.
3. **Token scope: access-JWT only.** No `UserSession`/refresh table in this slice; access token lifetime 15 min per `THR-02`. Claims `sub` + `userId` both = user GUID (standard interop + mapping-proof own reader), plus `email`, `role`, `jti`; HS256, `ClockSkew = Zero` (proven live: good → principal, tampered/wrong-key/garbage → null).
4. **Password policy (owner 10-Oct): `MinLength(12) + 1 digit** on `RegisterRequest.Password` only; login has no policy (don't leak it, `THR-01`).
5. **Role (owner 10-Oct): `UserRole` enum** (`JobSeeker = 0` default, `Analyst = 1`), same pattern as `JobSource` (int on the wire — no `JsonStringEnumConverter` configured; JWT `role` claim stays string via `ToString()`). `UserDB.Role` is a string column (mirrors `VacancyDB.Source`); service canonicalizes (`ignoreCase` parse, `JobSeeker` fallback) so token and response can never diverge.
6. **DisplayName: REMOVED for MVP** (owner 10-Oct) — less ПД surface (`THR-08`); re-add later as nullable column + optional field.
7. **JWT key plumbing (owner 10-Oct): env-only.** `Jwt:{Issuer,Audience,ExpiresMinutes:15}` in `appsettings.json`; `Jwt:Key` (≥32 chars) only via `Jwt__Key`/User Secrets, fail-fast at startup (`THR-09`).
8. **Packaging: contracts in `Services`, implementations in `api/`.** `waterfall.Services` multi-targets MAUI (shared with the mobile client, Z1 untrusted); server deps (JWT signing, `JwtOptions`, user-table access) must not ship to phones nor risk mobile restore. Documented in `<remarks>` on the three `api/Services` classes. `IJwtTokenService.Generate` takes primitives `(userId, email, role)` so the shared contract doesn't expose the EF entity.
9. **No `ThrowIfNull` guards in `AuthenticationService`** (owner 10-Oct) — service expects MVC-validated DTOs; misuse surfaces via natural exceptions.

### Hashing options — pros/cons (record of the evaluated choice)

* **A. `BCrypt.Net-Next`:** + matches HLD/data-model wording verbatim, adaptive cost, salt built-in; − extra NuGet dep, needs cost tuning, not NIST-approved primitive.
* **B. ASP.NET Identity `PasswordHasher<T>` (PBKDF2-HMACSHA256/512) — CHOSEN:** + built-in/maintained, zero new dep, NIST-approved, versioned format with rehash path; − CPU-only (less GPU-resistant than bcrypt/Argon2), not named verbatim in `SEC-001` examples (defensible: “наприклад” + issue allows PBKDF2).
* **C. Argon2 (e.g. Konscious):** + memory-hard PHC winner, best vs GPU/ASIC; − heaviest dep + config surface (`memory/iterations/parallelism`), misconfig → CPU/DoS risk, overkill for this MVP slice.

## 5. Detailed steps (build phase — status 10-Oct)

1. **[x] DTOs** (`src/waterfall.Services/Models/Auth/`, `80ce892`):
   `UserRole {JobSeeker = 0, Analyst = 1}` (like `JobSource`);
   `RegisterRequest { Email; Password [MinLength(12) + digit]; Role = JobSeeker }` (no `DisplayName` — MVP cut);
   `LoginRequest { Email; Password }` (no policy);
   `AuthResponse { Token; ExpiresAt; UserId Guid; Email; Role }`.
   Ukrainian XML docs + `<example>` throughout.
2. **[x] Entity + context** (`8f67812`): `UserDB { Id Guid PK; Email [320] unique; PasswordHash [1000]; Role [50] = "JobSeeker"; CreatedAt }`
   (`[Table("Users")]`, `[Index(Email, IsUnique)]` — attribute style like `VacancyDB`, SQLite+MSSQL, no dialect SQL).
   `DbSet<Users>`; no `SavedVacancyDB` rework (out of scope). MSSQL-migration note goes in PR.
3. **[x] Security core** (`7d6237d` + review hardening):
   * `Services/Interfaces/IPasswordHasher.cs` (+ own `PasswordVerifyResult {Failed, Success, SuccessRehashNeeded}` —
     MAUI-safe enum instead of plan's `bool`, carries the rehash signal without leaking ASP.NET types).
     `Verify(string? hashed, ...)` — null = unknown user → impl runs dummy work of its OWN format, returns `Failed`
     (timing-equalized `THR-01` branches; format knowledge stays per-impl, swap-safe).
   * `api/Services/AspNetPasswordHasher.cs` wraps `PasswordHasher<object>` (`null!` user: default impl ignores it).
   * `Services/Interfaces/IJwtTokenService.cs { (Token, ExpiresAt) Generate(Guid, email, role); ClaimsPrincipal? Validate(string); }`
     (tuple carries expiry; primitives keep EF out of the shared contract).
   * `api/Services/JwtTokenService.cs` — HS256, `sub`+`userId`+`email`+`role`+`jti`, `ClockSkew.Zero`.
   * `Services/Interfaces/IAuthenticationService.cs` + `Services/Exceptions/` (`DuplicateEmailException → 409`,
     `InvalidCredentialsException → 401` generic) + `Repository/{Interfaces/IUserRepository.cs, Repositories/EfUserRepository.cs}`
     (tracked read for rehash; attach-based `UpdateAsync` — concurrent delete surfaces `DbUpdateConcurrencyException`).
   * `api/Services/AuthenticationService.cs` — email `Trim().ToLowerInvariant()` before every DB touch (unique index
     becomes case-proof); pre-check + `DbUpdateException` race guard → `409`; shared generic `401` path; rehash + persist;
     logs only `email + outcome` (`THR-08`).
   * `api/Options/JwtOptions.cs` (`Issuer/Audience/Key/ExpiresMinutes = 15`).
4. **[x] Controller** (`537c96f`): `AuthController [Route("api/v1/auth")] [AllowAnonymous]`,
   `POST register → 201 + AuthResponse` (`StatusCode(201, …)` — no GET-by-id for users), `POST login → 200`.
   Thin, `SwaggerOperation` + `ProducesResponseType 200/201/400/401/409`; `ApiControllerBase.ErrorUnauthorized`
   added early (controller needs it to compile).
5. **[ ] Base + wiring — REMAINING (next step):**
   * `Program.cs`: `AddAuthentication(JwtBearer).AddJwtBearer(...)` (issuer/audience/lifetime/signing key from `Jwt:*`),
     `app.UseAuthentication()` **before** `UseAuthorization()`; Swagger `Bearer` security definition + requirement;
     **fail-fast at startup** if `Jwt:Key` missing/short (`<32` chars) or `ExpiresMinutes <= 0` (review item #2).
   * `ServiceCollectionExtensions`: register `IUserRepository → EfUserRepository`, `IPasswordHasher`,
     `IJwtTokenService`, `IAuthenticationService`, `JwtOptions` binding (`GetSection("Jwt")`).
   * `appsettings.json`: non-secret `Jwt:{Issuer: "Waterfall", Audience: "WaterfallClients", ExpiresMinutes: 15}` only;
     `.gitignore` check for secrets (`THR-09`).
6. **[ ] Logging hygiene (`THR-08`) — REMAINING (verification pass):** re-`grep` for `password`/`Authorization`/token
   in logs/responses; confirm only `email + outcome + RequestId`.
7. **[ ] Regen + manual smoke (no tests in this slice) — REMAINING:**
   `dotnet run --project src/api -- --generate-openapi-and-exit` (needs `Jwt__Key` exported, ≥32 chars);
   confirm the YAML diff only adds `/v1/auth/*` + `UNAUTHORIZED` schema.
   Smoke via Swagger `/api-docs`: register → `201`, re-register same email → `409`, login ok → `200` (decode JWT, assert `sub`/`userId` + ~15-min `exp`), login bad (unknown email and wrong password) → identical generic `401`; `grep` DB dump for plaintext password = 0 (`SEC-001` spot check).

## 6. Files to touch (build phase — [x] done, [ ] remaining)

* [x] New: `Repository/Entities/UserDB.cs`; `Repository/{Interfaces/IUserRepository.cs, Repositories/EfUserRepository.cs}`;
  `Services/{Interfaces/IPasswordHasher.cs, Interfaces/IJwtTokenService.cs, Interfaces/IAuthenticationService.cs, Exceptions/AuthExceptions.cs}`;
  `Services/Models/Auth/{UserRole,RegisterRequest,LoginRequest,AuthResponse}.cs`;
  `api/{Options/JwtOptions.cs, Services/AspNetPasswordHasher.cs, Services/JwtTokenService.cs, Services/AuthenticationService.cs}`;
  `api/Controllers/AuthController.cs`.
* [x] Edit: `Repository/Data/WaterfallDbContext.cs` (`DbSet<Users>`); `api/Controllers/ApiControllerBase.cs` (+401 helper);
  `api/Waterfall.Api.csproj` (+`JwtBearer 10.0.12` only).
* [ ] Edit REMAINING: `Services/Extensions/ServiceCollectionExtensions.cs` (DI); `api/Program.cs` (auth + Swagger Bearer);
  `appsettings.json` (non-secret `Jwt:*`); `docs/api/api-design.md` (short Auth addendum, no YAML hand-edits).
* Do NOT touch in this slice: `docs/api/openapi-generated.yaml` (generated), `docs/code-quality/*`, test projects, `SavedVacancyDB` composite-key rework.

## 7. Risks / follow-ups (do not fix silently in this slice)

* Route deviation (`/api/auth` in issue vs `/api/v1/auth` implemented) — call out in PR description.
* `EnsureCreated` vs migrations for the unique-email index on MSSQL — document; no migration-framework switch here.
* Refresh/rotation/revocation (`UserSession`, `THR-02` one-time refresh) deferred — leave a `TODO(#63-followup)` referencing `data-model.md` §3.
* Login brute-force guard (`THR-01` “5 fails → backoff”) deferred with tests — at minimum the generic `401` + timing-equalized branches ship now; register-`409` email enumeration is accepted-by-design (standard; bounded later by rate limiting); file follow-up issue.
* Packaging deviation (auth impls in `api/`, not `Services` like `VacancyService`) — rationale in `<remarks>` on the classes + §4.8; call out in PR.
* ~~Dirty tree note~~ RESOLVED 10-Oct: `openapi-generated.yaml` diff is clean — regen from current base in §5.7.
* `sub` inbound-mapping note: after `Validate`, `sub` appears as `…/nameidentifier`; `THR-12` readers must use the stable `userId` claim (or the mapped type) — bake into Step 5/future `[Authorize]` work.
* Role claim is `ClaimTypes.Role` URI on the wire (verbose but `[Authorize(Roles)]`-compatible); switch to short `"role"` only if owner wants it (open question below).

## 8. Open points for owner (all pre-build points answered 10-Oct; one remaining)

* ~~Password policy~~ → `MinLength(12) + 1 digit` (register only). DONE.
* ~~Register role~~ → `UserRole` enum, `Analyst` self-registration allowed. DONE.
* ~~JWT key plumbing~~ → `Jwt__Key` env-only. DONE (wiring in §5.5).
* Role claim shape: keep verbose `ClaimTypes.Role` URI, or switch `JwtTokenService` to short `"role"` (cleaner payload, still `[Authorize]`-compatible via inbound map)?

## 9. Definition of done (this slice)

* [x] DTOs + entity + services + controller implemented per §5 (`80ce892`, `8f67812`, `7d6237d`, `537c96f`).
* [x] `ErrorUnauthorized` helper + `JwtBearer 10.0.12` package (part of §5.5, pulled early).
* [ ] Wiring (§5.5: DI, `AddAuthentication`, Swagger Bearer, `appsettings`, key fail-fast).
* [ ] Manual smoke (§5.7) passes; OpenAPI regen diff clean.
* [ ] Self-review per `TeamCharter.md` §4 (logic, edge cases: duplicate email, bad creds, expired/tampered token, no secret leaks).
* [ ] PR `feature/auth` → `main` with `Closes #63` (or `part of #63` if the review/Bonus slice follows separately), CI green, 1 approval.
* [ ] **DELETE THIS FILE** (`docs/plan-63-auth.md`) before merge.
