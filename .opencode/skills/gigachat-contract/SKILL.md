---
name: GigaChat contract
description: Work on the AiRecruiter GigaChat prompt, JSON validation, repair, call limits and llm_calls journal without leaking candidate data or burning tokens.
---

# Контракт GigaChat

Использовать при изменении `JobAnalyzer`, system/user-промпта, JSON-схемы,
repair, лимитов вызовов или журнала `llm_calls`.

## Промпт

- System-промпт — статический; версия хранится в `prompt_versions` по
  `sha256-<16 hex>`, вставка `INSERT OR IGNORE`.
- User-сообщение собирается из блоков; полное описание:
  - `Description.Length <= 6000` — один блок `ПОЛНОЕ ОПИСАНИЕ`;
  - длиннее — `ТРЕБОВАНИЯ` и непустые секции;
  - пустой `Description` при непустых `Requirements` / `Responsibilities` /
    `Conditions` — сборка из секций;
  - без описания и секций — GigaChat не вызывать, `analysis_failed`.
- `НАВЫКИ` и метаданные страницы — всегда.
- `main_text_source` — `description` / `requirements`, только для
  `purpose = analysis`.

## JSON и валидация

- `score` — целое 1..10.
- `comment` — строка 10..800.
- `weighted` — число 0..10; расхождение с `score` больше 1 сейчас логируется,
  политика отклонения/пересчёта — отдельное решение.
- `ReclassifyDesirableMissing` определяет обязательность по пункту/секции, где
  найден навык, а не по словам во всём описании.
- Repair: инструкция в system, предыдущий ответ — в user; один repair.

## Лимиты и журнал

- 4 логических вызова: 3 анализа + 1 repair.
- `http_attempts` — HTTP-повторы внутри вызова; в лимит не входят.
- `llm_calls`: `purpose`, `attempt_no`, `call_no`, токены, `response_model`,
  `http_attempts`, `main_text_source`.
- Ключ: `(link, purpose, attempt_no, call_no)`.
- `raw_response` — только неудачные, брошенные и повторные вызовы, ≤ 64 КБ.
- Письмо — отдельный вызов `purpose = 'letter'`, `attempt_no = 1`,
  `call_no = 1`, свой лимит.

## Приватность

- Не логировать `prompt_text`, `raw_response`, `candidate_summary.md`,
  `resume.txt`, `profile.md`, контакты.
- В тестах не вызывать GigaChat и не ходить в сеть.
- `candidate_hash` — только от реальной сводки; сводка обязательна.

## Проверки

- Для risky code — test-first: тест из DoD пишется до кода (правило —
  `docs/process.md`, «TDD для risky code»).
- `JobAnalyzerDtoTests`, тесты промпта и repair.
- `--replay-failed failed_responses` — офлайн-разбор.
- `--parse-samples tests/Fixtures` — если менялись блоки описания.
