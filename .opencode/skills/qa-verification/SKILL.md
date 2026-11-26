---
name: QA verification
description: "AiRecruiter tester checklist: full acceptance command set with PASS/FAIL, bug report format, \"not verified\" instead of guesses, read-only for code and tests."
---

# Проверка качества AiRecruiter

Чек-лист тестировщика. Код и тесты не править. Полный результат —
`docs/work/<этап>/qa.md`; ответ оркестратору — не больше 10 строк: ID,
вердикт, путь, blocking, next; не пересказывать файл.

## Команды приёмки

Из корня репозитория, по порядку:

```sh
dotnet build AiRecruiter.slnx -c Release --nologo --no-incremental -p:TreatWarningsAsErrors=true
dotnet test AiRecruiter.slnx -c Release --no-build --nologo
dotnet format AiRecruiter.slnx --verify-no-changes --no-restore
bash scripts/check-docs.sh
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -c Release --no-build -- --parse-samples tests/Fixtures
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -c Release --no-build -- --replay-failed failed_responses
```

- Сборка: 0 предупреждений, 0 ошибок; тесты: 0 не пройдено, 0 пропущено
  (базовое число — 92, изменение объясняется); format: exit 0; check-docs:
  ни одного WARNING; фикстуры: 6 без исключений; replay: 4 ответа OK.
- Каждая команда получает PASS или FAIL с фактическим результатом;
  «всё зелёное» без цифр не принимается.

## Баг-репорт

Поля: задача (ID), шаг воспроизведения, ожидание, факт, улика (вывод
команды или ссылка на код без PII), минимальный фикс. Один баг — одна
запись; уровень blocking ставится, если пункт DoD не закрыт.

## Честность проверок

- Вместо догадки — «не проверено» и причина.
- Нет `failed_responses/` или `samples/` — соответствующая проверка
  «не проверено», не PASS; каталоги не создавать.
- Упавшую проверку не обходить: фикс — работа кодера, не тестировщика.
- Содержимое `candidate_summary.md`, `resume.txt`, `profile.md`, токены и
  персональные данные в отчёт не выводить.