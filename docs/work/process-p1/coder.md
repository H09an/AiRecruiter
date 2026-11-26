# coder.md — процессная настройка по итогам пилота P1 (chore)

ID: chore-p1. Продуктовый код (src/, tests/) не менялся.

## Изменено

- [docs/process.md:22-38](docs/process.md#L22-L38) — чекпойнт владельца
  после планирования переведён на «по исключению»: при проверенном DAG и
  отсутствии blocking-вопросов оркестратор продолжает сам; возврат
  владельцу — при blocking-вопросе, конфликте с roadmap/ADR, расширении
  объёма, исчерпанных лимитах раундов/шагов, недоступной модели.
- [.opencode/skills/orchestration/SKILL.md:21-25](.opencode/skills/orchestration/SKILL.md#L21-L25)
  — краткая выжимка правила со ссылкой на `docs/process.md`, без дубля
  формулировки.
- [.opencode/skills/task-decomposition/SKILL.md:33-46](.opencode/skills/task-decomposition/SKILL.md#L33-L46)
  — новый раздел «Инкрементальная запись»: каркас `breakdown.md` первым
  делом, детали по мере готовности; при возобновлении — чтение своего
  файла, а не перечитывание источников.
- [.opencode/agents/planner.md:5](.opencode/agents/planner.md#L5) — шаги
  25 → 40; в системном промпте — правило инкрементальной записи.
- [docs/decisions.md:69-75](docs/decisions.md#L69-L75) — две записи
  2026-11-27: чекпойнт по исключению; инкрементальная запись артефакта и
  шаги planner 40.
- [CHANGELOG.md:57-63](CHANGELOG.md#L57-L63) — `[Unreleased]` /
  «Изменено», два пункта без метки «Документация:» (меняется
  `.opencode`).

## Тесты

Тесты не добавлялись и не менялись: продуктовый код не тронут.

## Сборка и приёмка

- `bash scripts/check-docs.sh` — exit 0, без WARNING.
- `wc -l docs/process.md` — 124 (лимит 220).
- Лимиты скиллов: orchestration 50, task-decomposition 45 (лимит 80).
- `dotnet build AiRecruiter.slnx -c Release --nologo --no-incremental
  -p:TreatWarningsAsErrors=true` — Предупреждений: 0, Ошибок: 0.
- `dotnet test AiRecruiter.slnx -c Release --no-build` — не пройдено 0,
  пропущено 0, пройдено 159. Число 159 выше базового 92 из-за
  незакоммиченной работы этапа 3.2a в дереве (LlmJournalTests и другие),
  не из-за этого chore.
- `dotnet format AiRecruiter.slnx --verify-no-changes --no-restore` —
  exit 0.

## Риски

- Дерево перед работой было не чистым: в нём уже находились изменения
  этапа 3.2a (VacancyRepository, ADR-0014, CHANGELOG, decisions,
  architecture, тесты). Мои правки поверх не конфликтуют, но коммит
  владельцу лучше делать раздельным (chore отдельно от 3.2a).

Загружены: docs-quality (чек-лист применён: владение фактом —
process.md, без дубля формулировки, CHANGELOG без метки «Документация:»);
stage-runbook (preflight выполнен, расхождение дерева зафиксировано в
рисках); dotnet-acceptance (check-docs exit 0 / build 0-0 / test 0 не
пройдено / format exit 0, все команды и результаты выше).
