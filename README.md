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
* [**openapi-generated.yaml**](./docs/api/openapi-generated.yaml) — згенерована специфікація OpenAPI 3.0
* [**Swagger UI (Інтерактивна документація)**](./docs/api/index.html) — автономний веб-інтерфейс Swagger UI для тестування API
* [**Код бекенду Web API**](./src/api/) — реалізація на C# (.NET 10 / ASP.NET Core)

---

## Статус та запуск проєкту

> [!NOTE]
> Наразі проєкт перебуває на етапі проектування архітектури.


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
