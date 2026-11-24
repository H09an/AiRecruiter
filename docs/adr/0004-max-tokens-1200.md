# 0004. MaxTokens = 1200

## Статус

Принято, реализовано частично.

## Дата

2026-11-17 для значения 800 (коммит
[67a4baa](https://github.com/H09an/AiRecruiter/commit/67a4baa)); 2026-11-18 для
значения 1200 (коммит
[5369608](https://github.com/H09an/AiRecruiter/commit/5369608)).

## Контекст

Комментарий анализа ограничен 800 символами. Было решено поднять `MaxTokens` до
1200, чтобы усечённый JSON-ответ не ломал разбор. Критерий пересмотра: если
`completion_tokens` стабильно ниже 600, значение пересматривается.

## Решение

`GigaChat:MaxTokens = 1200`; при `completion_tokens` ниже 600 — пересмотреть
значение.

Текущее состояние кода:

- код-дефолт `MaxTokens = 1200` —
  [JobAnalyzer.cs:102](../../src/AiRecruiter/Analyzers/JobAnalyzer.cs#L102);
  конфигурация переопределяет значение;
- `appsettings.example.json` —
  [appsettings.example.json](../../appsettings.example.json);
- поля `usage` и `completion_tokens` из ответа GigaChat не читаются, критерий
  пересмотра не измеряется.

## Последствия

Для кода:

- без конфигурации работает 1200 — совпадает с решением; усечённый JSON
  возможен при длинном описании — правило блоков описания —
  [ADR-0012](0012-full-description-prompt.md).

Что ещё не сделано:

- чтение `usage` / `completion_tokens` и измерение критерия пересмотра.
