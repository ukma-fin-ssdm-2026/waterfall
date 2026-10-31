# Waterfall

[![CI](https://github.com/ukma-fin-ssdm-2026/waterfall/actions/workflows/ci.yml/badge.svg)](https://github.com/ukma-fin-ssdm-2026/waterfall/actions/workflows/ci.yml)

Цей проєкт - парсер сайтів для пошуку вакансій. Метою додатку є спрощення знаходження роботи за допомогою агрегації різних ресурсів, зручного пошуку між ними, та ШІ-агента; також зручна аналітика для роботодавців.

## Основні можливості

* **Агрегація вакансій** — автоматизований збір і нормалізація даних із найпопулярніших платформ працевлаштування.
* **Гнучкий пошук та фільтрація** — зручний відбір за ключовими словами, технологічним стеком, заробітною платою та форматом роботи.
* **Персоналізовані рекомендації** — підбір релевантних пропозицій на основі вподобань та історії взаємодії користувача.
* **Аналітика та статистика** — огляд трендів ринку праці, порівняння зарплатних вилок і затребуваності навичок.
* **ШІ-консультант** — діалоговий помічник для підбору пропозицій під запит природною мовою та аналізу вимог роботодавців.

---

## Стек технологій

* **Backend**: C# (.NET)
* **Frontend**: .NET MAUI (C#), HTML5, TypeScript
* **База даних та кешування**: Microsoft SQL Server (продакшн), SQLite (локальна розробка та офлайн-режим)
* **ШІ-інтеграція**: LLM API (Google Gemini, OpenAI API, Anthropic Claude)
* **CI/CD & Інструменти**: GitHub Actions, GitHub Flow

---

## Команда проєкту

| Учасник | Роль у команді | GitHub | Контакт (НаУКМА) |
| :--- | :--- | :--- | :--- |
| **Саєнко Данило** | Repo Maintainer | [@Danylo202](https://github.com/Danylo202) | [d.saienko@ukma.edu.ua](mailto:d.saienko@ukma.edu.ua) |
| **Мельник Дмитро** | CI Maintainer | [@deffin05](https://github.com/deffin05) | [dm.melnyk@ukma.edu.ua](mailto:dm.melnyk@ukma.edu.ua) |
| **Садиков Константін** | Documentation & Project Brief Lead | [@int3rrobang](https://github.com/int3rrobang) | [k.sadykov@ukma.edu.ua](mailto:k.sadykov@ukma.edu.ua) |
| **Сидоренко Денис** | Issue Tracker Lead | [@s1ngleo](https://github.com/s1ngleo) | [d.sydorenko@ukma.edu.ua](mailto:d.sydorenko@ukma.edu.ua) |

---

## Документація

* [**TeamCharter.md**](./TeamCharter.md) — командний статут (ролі, принципи комунікації, Git-процеси, етика та політика використання ШІ)
* [**Project-Description.md**](./Project-Description.md) — детальний опис проєкту, рамки розробки (Scope), припущення та Міні-ТЗ

### Артефакти вимог
* [**requirements.md**](./docs/requirements/requirements.md) — функціональні та нефункціональні вимоги (FR, NFR)
* [**user-stories.md**](./docs/requirements/user-stories.md) — користувацькі історії з критеріями прийнятності
* [**rtm.md**](./docs/requirements/rtm.md) — матриця простежуваності вимог (Requirements Traceability Matrix)

### Архітектура
* [**high-level-design.md**](./docs/architecture/high-level-design.md) — стиль, компоненти, розгортання, послідовність
* [**traceability-matrix.md**](./docs/architecture/traceability-matrix.md) — простежуваність вимог
* [**data-model.md**](./docs/architecture/data-model.md) — модель даних
* [**threat-model.md**](./docs/architecture/threat-model.md) — STRIDE (загрози)
* [**decisions/**](./docs/architecture/decisions/) — ADR-001…003
* [**uml/**](./docs/architecture/uml/) — `.puml` + `.svg`

### Проєктування та специфікація API (Лаб 4)
* [**api-design.md**](./docs/api/api-design.md) — документ проєктування REST API (ресурсна модель, ендпоінти, пагінація, обробка помилок)
* [**quality-attributes.md**](./docs/api/quality-attributes.md) — атрибути якості API
* [**openapi-generated.yaml**](./docs/api/openapi-generated.yaml) — згенерована специфікація OpenAPI 3.0 (100% Code-First, 0 рядків вручну; не редагувати)
* [**Swagger UI (інтерактивна документація)**](./docs/api/index.html) — автономний Swagger UI (CDN) поверх `openapi-generated.yaml`
* [**Код бекенду Web API**](./src/api/) — реалізація на C# (.NET 10 / ASP.NET Core)

#### API: запуск, Swagger UI та регенерація специфікації
1. Запуск API (див. ### Запуск нижче): `dotnet run --project src/api` — слухає `http://localhost:3000` (див. `src/api/Properties/launchSettings.json`).
2. Жива документація: `http://localhost:3000/api-docs` (Swagger UI), корінь `/` редіректить туди ж.
3. Статична документація без бекенда: `npx serve docs/api` → відкрити надрукований URL + `/index.html` (та сама сторінка, що й на GitHub Pages).
4. Онлайн на GitHub Pages: https://ukma-fin-ssdm-2026.github.io/waterfall/ — збірка через `.github/workflows/api-docs.yml`.
5. Регенерація специфікації з коду (Code-First): `dotnet run --project src/api -- --generate-openapi-and-exit` (або просто запуск сервера) — перезаписує `docs/api/openapi-generated.yaml` лише за наявності змін.
6. Перевірка повноти схеми: усі операції мають summary/description, усі моделі — описи та приклади, усі `$ref` вказують на визначені схеми, помилки — через `ApiErrorResponse`.

### Клієнт і якість коду (Лаб 5)
* [**progress.md**](./docs/code-quality/progress.md) — структура модулів, стан коду, запуск застосунку, результати перевірок якості
* [**static-analysis.md**](./docs/code-quality/static-analysis.md) — статичний аналіз безпеки
* [**review-report.md**](./docs/code-quality/review-report.md) — огляд коду команди [pun-intended-team](https://github.com/ukma-fin-ssdm-2026/pun-intended-team)
* [**TeamCharter.md §9**](./TeamCharter.md#9-політика-якості-та-огляду-коду) — стандарти коду, лінтери, чекліст Peer Review, Definition of Done
* Застосунок (Windows): `dotnet run --project src/waterfall.UI -f net10.0-windows10.0.19041.0` (поруч має працювати API)
* Перед комітом: `dotnet format waterfall.slnx --verify-no-changes`; хук `gitleaks` — `pre-commit install` (див. [`.pre-commit-config.yaml`](./.pre-commit-config.yaml))

### Тестування та налагодження (Лаб 6)
* [**testing-strategy.md**](./docs/testing/testing-strategy.md) — стратегія тестування
* [**debugging-log.md**](./docs/testing/debugging-log.md) — журнал налагодження
* [**Звіт покриття**](./coverage-report/) — звіт про покриття проекту тестами
* [**tests**](./tests/) — директорія тестів

### Планування тестування та огляди (Лаб 7)
* [**test-plan.md**](./docs/validation/test-plan.md) — план тестування
* [**review-log.md**](./docs/validation/review-log.md) — журнал оглядів

### Рефакторинг та управління релізами (Лаб 8)
* [**sonarcloud-report.md**](./docs/refactoring/sonarcloud-report.md) — Звіт SonarQube Cloud та результати рефакторингу

---

## Статус та запуск проєкту

### Запуск

1. Необхідно мати .NET 10.
2. Секрети (JWT): `Jwt:Key` (мінімум 32 символи) живе **лише** у змінній середовища `Jwt__Key` або .NET User Secrets. Без ключа сервер падає на старті з `InvalidOperationException`; регенерація docs (`--generate-openapi-and-exit`) перевірку пропускає.
- Генерація надійного ключа (≥256 біт; свіжий на кожне середовище:
   ```bash
   # Linux/macOS:
   openssl rand -base64 32
   ```
   ```powershell
   # Windows PowerShell (.NET, без openssl):
   [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
   ```
   ```bash
   # Кросплатформно через Python:
   python3 -c "import secrets; print(secrets.token_urlsafe(32))"
   ```
- Застосувати ключ — локальна розробка:
   ```bash
   dotnet user-secrets set "Jwt:Key" "<вставити згенероване>" --project src/api
   ```
- Застосувати ключ — продакшн / CI:
   ```bash
   # Linux/macOS:
   export Jwt__Key="<вставити згенероване>"
   ```
   ```powershell
   # Windows PowerShell:
   $env:Jwt__Key = "<вставити згенероване>"
   ```
3. Скачати залежності: `dotnet restore src/api/Waterfall.Api.csproj`
4. Забілдити: `dotnet build src/api/Waterfall.Api.csproj`
5. Запустити: `dotnet run --project src/api`

### MCP-сервер
TypeScript MCP-сервер в [`src/mcp/`](./src/mcp/), згенерований зі специфікації
[`openapi-generated.yaml`](./docs/api/openapi-generated.yaml). Надає кожну кінцеву точку API як інструмент MCP.

**Запуск:**
1. Запустіть API: `dotnet run --project src/api` (має слухати `http://localhost:3000`).
2. Встановіть залежності MCP-сервера: `cd src/mcp && npm install`.
3. Запустіть: `npm run build` (`API_BASE_URL=http://localhost:3000` за замовчуванням;
   за потреби скопіюйте `.env.example` у `.env` і змініть `API_BASE_URL` та `PORT`).

**Підключення до агента:**
* *Claude Code* — додайте локальний stdio-сервер:
  `claude mcp add --scope project --transport stdio waterfall -- npm --prefix src/mcp start`.
* *OpenCode* — додайте у конфіг у розділ `mcp` наступний блок:
```json
"waterfall": {
   "type": "local",
   "command": [ "npm", "start" ],
   "cwd": "src/mcp",
   "enabled": true
}
```


## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
|--------|--------|----|---------------|
| весь документ | Денис — початкова версія, редагування | так (коміт a0ba4d6) | обговорено командою на дзвінку; перевірено викладачем (issue #10, README 5/5) |
| посилання на артефакти вимог | Денис | так, додавання посилань | самоперевірка автора (§8) |
| розділ `### Архітектура` (навігація) | Садиков Константін | структурування посилань | самоперевірка автора (§8); рев'ю в PR |
| розділ `### MCP-сервер` | Дмитро | структурування інструкцій | самоперевірка автора (§8) |
| розділ `#### API: запуск, Swagger UI та регенерація специфікації` | Константін | текст інструкцій | самоперевірка автора (§8): скрипт `$ref`/описів/прикладів, HEAD-перевірка CDN, рев'ю в PR |
| розділ `### Клієнт і якість коду (Лаб 5)` | Садиков Константін | так, текст | самоперевірка автора (§8); команди запущено локально |
| примітка про секрети JWT, п. 2 (§ Запуск) | Дмитро (запит) + ШІ-агент (текст) | так | перевірено запуском: fail-fast без `Jwt__Key`, успішний старт і регенерація з ключем |

