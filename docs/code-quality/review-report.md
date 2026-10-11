## Огляд коду

Огляд коду команди [pun-intended-team](https://github.com/ukma-fin-ssdm-2026/pun-intended-team), хеш коміту `00fc939e21b03c1556817c53eb81f7f3f76a9185`.

### Сильні сторони

- Бізнес-логіка добре відокремлена від фреймворку - сервіси залежать лише від абстракцій, а також декоратор `@Endpoint` дозволять легко замінити фреймворк за потреби.

- Гарно пророблена безпека додатку: для хешування паролів використовується надійний алгоритм Argon 2, salting. Також застосовані техніки, щоб уникнути зайвого розголошенню інформації: уніфікована помилка для "невідомий логін" та "неправильний пароль", обраховування хешу навіть, якщо користувач не знайдений та інші.

- API доволі гарно покрите e2e тестами ще на стадії прототипу.

### Пропозиції покращення

- Додати більше логування: у проекті логується лише старт сервера та неочікувані 500 у ApiExceptionFilter, плюс заглушка SMS-коду в консоль - немає логів запитів, входів у систему або помилок при вході, адмінських дій.

- Слід додати керування сесіями та відкликання токенів - це дозволить імплементувати відкликання сесій, а також інвалідувати токени одразу при логауті.

- Окрім наявних тестів варто також покрити ізольованими unit-тестами наявний функціонал, щоб можна було перевіряти справність бізнес-логіки у відриві від HTTP запитів.

### Потенційна вразливість

Rate-limiting не імплементований, а отже додаток вразливий до парсерів, а також перебору паролів, що у комбінації із майже відсутнім логуванням не дасть змоги швидко це виявити.

## ШІ-огляд

### 3 сильні сторони

#### 1. Шарована архітектура як код: `api -> core <- infra`

Розділення витримано послідовно: `src/api/` — тільки HTTP, `src/core/` — бізнес-логіка без HTTP/БД (репозиторії як `abstract class` DI-токени), `src/infra/` — адаптери та stubs. Порушення заборонено ESLint `no-restricted-imports` в `eslint.config.mjs`, документовано в `docs/architecture/mechanisms/code-structure.md`. Плюс строгий `tsconfig.json`: `strict`, `noUncheckedIndexedAccess`, `verbatimModuleSyntax` + `import type`.

#### 2. Code-First API: `@Endpoint` + Zod = валідація + OpenAPI з одного місця

`src/api/common/endpoint.ts`, `src/api/common/routes.ts`, `src/api/common/registry.ts`: централізовані шляхи, `operationId` = ім'я MCP-тула, `access: public|guest|user|admin`, `ValidationInterceptor` (400 з `details[].{location,field}`) + `ResponseSchemaInterceptor` (обрізає зайві поля, невідповідність = 500), `z.strictObject` проти mass-assignment, єдиний `ErrorResponse` в `src/api/common/schemas/errors.ts`. Спек `docs/api/openapi-generated.yaml` генерується (`npm run docs:generate`), CI падає на застарілому (`api-docs.yml`).

#### 3. Ґрунтовні e2e-тести та гігієна CI

`test/e2e/api.e2e.test.ts` (~940 рядків): реальний HTTP, на кожен ендпоінт happy + reject, 10 кейсів поганих токенів (`expired/tampered/alg:none/kid/audience/typ`), межі 18 років, одноразовість коду, `Location`-заголовок, security headers. Плюс: `typecheck/lint/format/e2e/docs/audit` в CI, `securityHeaders()` (`nosniff/no-store/CSP/HSTS`), `json limit 100kb`, CORS за замовчуванням вимкнений (`src/api/main.ts`), gitleaks + hooks в `.githooks/`.

---

### 3 пропозиції для покращення

#### 1. Реалізувати rate-limit (документовано, але відсутній)

`docs/architecture/mechanisms/catalog-access.md:12-13`, `docs/api/quality-attributes.md:103-112` обіцяють 100 req/min на токен / 1000 на IP + `429+Retry-After`, `requirements.md:169-172` — блок після 5 невдалих sign-in. Фактично є лише `CODE_RESEND_TOO_SOON` 60с в `src/core/identity/services/auth.service.ts`. `docs/code-quality/static-analysis.md:18 (SA-04)`: 150 запитів поспіль — всі 200.

#### 2. Прибрати дублювання та додати unit-тести

Магічні межі без констант (`120`, `5000/2000/500`, `20`, `60`) в `src/api/catalog/schemas/events.ts:69-76`, `src/api/listings/schemas/listings.ts:48-54`, `src/api/profiles/schemas/profiles.ts:62-74`; коди помилок синхронізуються вручну в 3 місцях (`core/common/domain-error.ts` + `api/common/schemas/errors.ts` + `api/common/api-exception.ts`); схожа join-логіка `detailsFor/withDetails` в `listings.service.ts:75-96` і `profiles.service.ts:82-104`. Чисті функції (`ageOn`, `kyiv-time.ts`, `checkTimes`, `isAtLeast`) покриті лише опосередковано e2e — винести в unit-тести, e2e залишити тонким.

#### 3. Зупинити дрейф docs/generated-коду

Dev-сервер переписує комічений спек при старті (`src/api/main.ts:12-16`) — ризик брудного diff; валідаційний лог описує `PATCH /events/{id}`, код реалізує `PUT` (`docs/api/quality-attributes.md:313` vs `events.controller.ts:90-117`); важкі коммічені `docs/api/index.html + vendor/swagger-ui/*` вимагають ручної синхронізації. MCP: `zod@3/TS 5.8` проти `zod@4/TS 6.0` в корені, `any` + `eval(jsonSchemaToZod)` в `src/mcp/src/index.ts:872-883`, виключений з dependabot і кореневого ESLint.

---

### 1 потенційна вразливість

#### Brute-force auth без лімітів + оракули переліку (висока)

`src/core/identity/services/auth.service.ts:36-54,83-99`, `src/api/identity/controllers/auth.controller.ts`: немає лічильника невдалих `sign-in`, немає per-IP/per-token лімітів. 6-значний код обходиться циклом `register → 3 спроби → чекати 60с → register` (новий код обнуляє лічильник через `findLatest`); `POST /auth/guest` — безлімітний мінт токенів; `429 CODE_RESEND_TOO_SOON` видає «номер нещодавно просив код», а `verify → 409 CONFLICT` після валідного коду — «номер вже зареєстрований». Наслідок: credential-stuffing, SMS-pumping (TM-08), скрапінг каталогу. Лікується: bucket на IP+номер, lockout sign-in 5/15хв, однотипні відповіді без оракулів, `Retry-After`, денний cap на SMS + алерти.

> Додатково звернути увагу (не в рахунок): зашиті dev-акаунти `+380501110001..005` з паролем `nearby-dev-password` включно з admin (`src/infra/stubs/data.ts:24-66`) та код верифікації відкритим текстом в логах (`src/infra/messaging/console-messaging-provider.ts:10-12`) — не випускати в прод до виправлення.

---

> Використаний агент OpenCode, модель Muse Spark 1.3

## Порівняння

Загалом ШІ агент описав знахідки в значно більших деталях з референсами на код, а також в один пункт впихував по 2-3 різні ідеї. Через це ШІ знайшов більше пунктів за людського оглядача, проте деякі з них були хибні, а деякі не є важливими через те, що вони зумовлені попередніми роботами, про які ШІ не знав. Також деякі моменти ШІ не знайшов (або не визначив достатньо важливими).

### Порівняльна таблиця

| Знайдений концепт                                    | Хто знайшов   |
|------------------------------------------------------|---------------|
| Гарне розділення фреймворку та логіки                | Обидва        |
| Декоратор @Endpoint                                  | Обидва        |
| Гарне e2e тестування                                 | Обидва        |
| Автоматична генерація OpenAPI                        | ШІ*           |
| Гарна валідація                                      | ШІ            |
| Гарне CI                                             | ШІ*           |
| Відсутній rate-limiting                              | Обидва        |
| Додати unit тести                                    | Обидва        |
| Магічні межі без констант                            | ШІ            |
| схожа join-логіка  `detailsFor/withDetails`          | ШІ (хибне)    |
| коди помилок синхронізуються вручну в 3 місцях       | ШІ (хибне)    |
| Невідповідність реалізації з `quality-attributes.md` | ШІ            |
| Вразливість до брутфорсу паролів та парсингу         | Обидва        |
| Недостатнє логування                                 | ШІ не знайшов |
| Відсутній менеджмент сесій та відкликання токенів    | ШІ не знайшов |