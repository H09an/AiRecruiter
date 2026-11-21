---
name: .NET acceptance
description: Run AiRecruiter acceptance checks (build, tests, format, docs, parser fixtures, replay) and report exact results without hiding failures.
---

# Приёмка AiRecruiter

Использовать перед отчётом о готовности, перед коммитом и после изменений в
коде, тестах, схеме БД или документации.

## Правила

- Работать из корня репозитория.
- Не подавлять предупреждения через `!`, `#pragma`, `NoWarn`.
- Не менять существующие тесты ради зелёной приёмки.
- Тесты не ходят в сеть и не вызывают GigaChat.
- Если проверка упала — исправить причину, а не обойти проверку.

## Команды

```sh
dotnet build AiRecruiter.slnx -c Release --nologo --no-incremental -p:TreatWarningsAsErrors=true
dotnet test AiRecruiter.slnx -c Release --no-build --nologo
dotnet format AiRecruiter.slnx --verify-no-changes --no-restore
bash scripts/check-docs.sh
```

Если менялись парсер, фикстуры или формат ответа GigaChat:

```sh
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -c Release --no-build -- --parse-samples tests/Fixtures
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -c Release --no-build -- --replay-failed failed_responses
```

## Что считать успехом

- сборка: `Предупреждений: 0`, `Ошибок: 0`;
- тесты: `не пройдено 0`, `пропущено 0`; базовое число — 90, если оно
  изменилось, это должно быть объяснено в отчёте;
- `dotnet format`: `exit=0`;
- `check-docs.sh`: ни одного `WARNING`;
- `--parse-samples tests/Fixtures`: 6 фикстур, без исключений;
- `--replay-failed failed_responses`: 4 ответа `OK` (8, 8, 4, 7).

Если каталога `failed_responses/` нет, команда не должна создавать его и не
должна падать.

## Если упало

- build: сначала прочитать первое предупреждение/ошибку, не маскировать.
- test: воспроизвести один тест, проверить, не тест ли сломан; существующие
  тесты не переписывать без обоснования.
- format: запустить `dotnet format` без `--verify-no-changes` только если
  владелец разрешил правку форматирования; затем повторить проверку.
- check-docs: исправить ссылку/путь/голую запись `путь:строка`, а не отключать
  проверку.
- parse-samples: сравнить поля с эталоном, обновлять фикстуры только
  синтетическими страницами.

## Отчёт

Указывать команду и фактический результат, а не «всё зелёное». Если проверка не
запускалась — писать «не проверено» и почему.
