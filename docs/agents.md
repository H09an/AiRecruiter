# Агенты: ростер, модели, права

Ростер агентов AiRecruiter. Процесс (граф, профили, раунды, артефакты) —
[`docs/process.md`](process.md); вход и запреты — [`AGENTS.md`](../AGENTS.md).
Точные права каждой роли — frontmatter `permissions` в
`.opencode/agents/<роль>.md`; здесь выжимка, при расхождении приоритет у
конфига. Handoff через артефакт — правило в [`docs/process.md`](process.md).

## Ростер

| Роль | Режим | Модель и уровень | Шаги | Артефакт |
|---|---|---|---|---|
| orchestrator | primary | `opencode-go/qwen4-max#high` | 40 | — (не пишет файлы) |
| planner | subagent | `openrouter/deepseek/deepseek-v4.1-flash-1017#max` | 25 | `docs/work/<этап>/breakdown.md` |
| coder | subagent | `opencode-go/glm-5.3-flash#high` | 40 | `docs/work/<этап>/coder.md` |
| coder-deep | subagent | `opencode-go/glm-5.3-flash#max` | 60 | `docs/work/<этап>/coder.md` |
| tester | subagent | `opencode-go/gpt-6-luna#high` | 30 | `docs/work/<этап>/qa.md` |
| reviewer | subagent | `opencode-go/kimi-k3.1-pro#high` | 20 | `docs/work/<этап>/review.md` |
| security | subagent | `opencode-go/grok-4.7#xhigh` | 25 | `docs/work/<этап>/security.md` |

`explore` — встроенный read-only поиск, вызывается оркестратором; своего
артефакта не имеет.

## Обоснование моделей и уровней

- orchestrator: делегирование и проверка по DoD требуют длинного контекста
  и устойчивого следования протоколу; уровень high, без максимума —
  оркестратор не решает сложные задачи сам.
- planner: декомпозиция и трассировка DoD — рассуждение, а не код; уровень
  max, модель через OpenRouter — дешёвый токеновый бюджет планирования.
- coder: потоковая реализация задач, уровень high; coder-deep — та же
  модель с уровнем max и большим лимитом шагов для сложных задач (схема,
  промпт, многослойные правки).
- tester: приёмка независима от кодера — другая модель и уровень high,
  чтобы не наследовать его слепые зоны.
- reviewer: ревью diff — ещё одна независимая модель, уровень high.
- security: модель угроз и поиск неочевидных утечек — уровень xhigh.

Правило: одна модель — одна роль, кроме coder/coder-deep (одна модель,
разные уровни и лимиты). Смена модели или уровня — только решением
владельца; температура не используется, варианты задаются суффиксом
`#уровень` в поле `model`. Модель primary задаётся при старте сессии;
конфиг оркестратора — дефолт, а не принудительная замена.

Ключ OpenRouter — только планировщик. Прокси задаётся переменными
окружения `HTTPS_PROXY` / `HTTP_PROXY` / `NO_PROXY`; адрес и учётные данные
не хранятся в репозитории и не выводятся. При сетевой ошибке модели
планировщика: проверить переменные окружения, затем сообщить владельцу.

## Права (выжимка)

Правила `permissions` упорядочены: последнее совпавшее правило выигрывает,
широкое правило стоит первым, исключения — после.

| Роль | edit | shell | skill | subagent / question |
|---|---|---|---|---|
| orchestrator | deny | только `git status/diff/log/show` | orchestration | subagent: ростер + explore; question — нет |
| planner | только `breakdown.md` | только `git status/diff/log/show` | своя матрица | deny / deny |
| coder, coder-deep | всё, кроме чужих журналов | всё, кроме `git push/commit/tag`, `git reset --hard` | своя матрица | deny / deny |
| tester | только `qa.md` | всё, кроме `git push/commit/tag` | своя матрица | deny / deny |
| reviewer | только `review.md` | только `git status/diff/log/show` | своя матрица | deny / deny |
| security | только `security.md` | только `git status/diff/log/show` | своя матрица | deny / deny |

Глобально в `.opencode/opencode.jsonc` запрещён `read` локальных файлов с
PII и секретами: `candidate_summary.md`, `resume.txt`, `profile.md`,
`samples/*`, `failed_responses/*`, `appsettings.json`.

## Матрица скиллов

Роль загружает только скиллы своей матрицы; лишний скилл не загружается,
расхождение сообщается владельцу до начала работы.

| Роль | Скиллы |
|---|---|
| orchestrator | `orchestration` |
| planner | `stage-runbook`, `task-decomposition`, `docs-quality`, `git-workflow`, `audit-triage` |
| coder, coder-deep | `stage-runbook`, `sqlite-migration`, `hh-fixtures`, `gigachat-contract`, `docs-quality`, `dotnet-acceptance` |
| tester | `qa-verification`, `dotnet-acceptance` |
| reviewer | `code-review`, `docs-quality` |
| security | `security-audit` |

Точки загрузки: `stage-runbook` — на старте этапа; `sqlite-migration`,
`hh-fixtures`, `gigachat-contract` — по области изменений (загрузить все
затронутые); `docs-quality` — при изменении документов; `dotnet-acceptance`
— перед отчётом; `audit-triage` — при разборе внешнего аудита;
`git-workflow` — при commit/tag/push по явной команде владельца.

## Эскалация и fallback

- Не больше 3 раундов доработки задачи и 2 перепланирований этапа; дальше
  — остановка и решение владельца. Раунды и эскалация —
  [`docs/process.md`](process.md).
- Один повтор субагента на задачу; второй провал — эскалация.
- Недоступная модель: остановка и сообщение владельцу. Молчаливая замена
  модели запрещена; fallback — только явное решение владельца о смене
  модели или уровня, зафиксированное в `docs/decisions.md`.