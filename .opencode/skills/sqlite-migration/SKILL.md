---
name: SQLite migration
description: Change the AiRecruiter SQLite schema safely: backup, additive migrations, transactions, idempotent Initialize, status invariants and temp-DB tests.
---

# Миграции SQLite в AiRecruiter

Использовать при любом изменении `VacancyRepository.Initialize`, схемы
`vacancies`, таблиц `llm_calls` / `prompt_versions` / `candidate_profiles` или
списка статусов.

## Перед началом

1. Прочитать `docs/architecture.md` (схема, статусы, известные расхождения).
2. Проверить `docs/roadmap.md` — что именно входит в текущий шаг.
3. Если `vacancies.db` существует и не пуста:

```sh
cp vacancies.db vacancies.db.bak
```

`vacancies.db` — локальный файл, в git не попадает.

## Правила миграции

- Только аддитивные изменения: `CREATE TABLE IF NOT EXISTS`,
  `ALTER TABLE ... ADD COLUMN`.
- Проверять наличие колонки через `pragma_table_info('vacancies')`.
- DDL и миграции — в транзакции.
- `Initialize` идемпотентен: повторный вызов не меняет схему и данные.
- Не удалять legacy-миграцию `UPDATE ... analysis_failed` без разрешения на
  правку существующего теста.
- Удаление `meta` — `DROP TABLE IF EXISTS meta`, убрать `CREATE`.
- `Upsert` и `SetStatus` не трогают `attempts`; счётчик меняет только
  `SaveAnalysisAttempt` в одной транзакции с результатом.
- `new` не входит в `GetLinksToSkip`; терминальные статусы — входят.
- `description` хранится только у `analysis_failed` / `analysis_abandoned`; при
  успехе очищается.

## Тесты

На временной БД:

```csharp
Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db")
```

Проверить:

- создание схемы с нуля;
- миграцию старой схемы без потери строк;
- повторный `Initialize`;
- отсутствие `meta` после 3.2a;
- `attempts` не сбрасывается `Upsert` / `SetStatus`;
- после трёх неудач — `analysis_abandoned`;
- `GetLinksToSkip` включает `analysis_abandoned` и не включает
  `analysis_failed`;
- `description` есть только у неудачных/брошенных;
- удаление `.db`, `-wal`, `-shm`, `-journal` после теста.

## SQL-инварианты

```sql
SELECT status, attempts, COUNT(*) FROM vacancies GROUP BY status, attempts;
SELECT COUNT(*) FROM vacancies WHERE status = 'analysis_abandoned' AND attempts < 3;
SELECT COUNT(*) FROM vacancies WHERE status = 'analysis_failed' AND attempts >= 3;
SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta';
```

Ожидаемо: вторые два запроса — 0; `meta` — 0 строк.

## Запрещено

- Менять существующие тесты без обоснования.
- Писать `!`, `#pragma`, `NoWarn`.
- Очищать `vacancies.db` без backup и разрешения.
- Добавлять таблицы/колонки, которых нет в текущем шаге roadmap.
