# Журнал решений

Журнал фиксирует процессные решения для агентов и указывает, где лежит
архитектурное решение. Текущее состояние кода —
[`docs/architecture.md`](architecture.md), план и DoD —
[`docs/roadmap.md`](roadmap.md), архитектурное решение — [ADR](adr/).

Граница: архитектурное решение — ADR; процессное решение без ADR — этот
журнал; план — только roadmap. Записи с ADR и планы сюда не переносятся,
пересказов нет — только указатель.

Формат записи:
`- <дата> — <решение>. Почему: <причина>. Статус: сделано|план|отклонено.`
Запись — 1–2 строки. Журнал append-only: реализованное сжимается до строки, но
не удаляется. Дата — день внесения записи. Объём — около 150 строк.

## Архитектура

Решения: [ADR-0001](adr/0001-sqlite-state.md),
[ADR-0002](adr/0002-employer-id-from-detail-page.md),
[ADR-0006](adr/0006-hybrid-parser-playwright.md).
Текущее состояние — [`docs/architecture.md`](architecture.md).

## Данные и журнал (этап 3.2a)

Решения: [ADR-0012](adr/0012-full-description-prompt.md),
[ADR-0013](adr/0013-network-errors-stop-run.md).
План шагов 4–8 — [`docs/roadmap.md`](roadmap.md), этап 3.2a.

## Оценка и фильтры

Решения: [ADR-0003](adr/0003-gigachat-pro-model.md),
[ADR-0004](adr/0004-max-tokens-1200.md),
[ADR-0005](adr/0005-title-stop-words.md),
[ADR-0007](adr/0007-score-threshold-shadow-mode.md),
[ADR-0008](adr/0008-no-candidate-level-in-code.md),
[ADR-0009](adr/0009-min-score-3.md).
Калибровка порога — план, этап 3.2 в [`docs/roadmap.md`](roadmap.md).

## Письмо и отклики (этапы 3.3–3.4)

Решения: [ADR-0011](adr/0011-contacts-in-cover-letter.md).
План и DoD — [`docs/roadmap.md`](roadmap.md), этапы 3.3–3.4.

## Процесс и инструменты

Решения: [ADR-0010](adr/0010-roadmap-separate-document.md).
Правила ролей, скиллов и MCP — [`AGENTS.md`](../AGENTS.md#скиллы).
