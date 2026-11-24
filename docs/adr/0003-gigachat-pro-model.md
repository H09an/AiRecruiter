# 0003. Модель GigaChat-2-Pro

## Статус

Принято, реализовано.

## Дата

2026-11-18 (коммит
[5369608](https://github.com/H09an/AiRecruiter/commit/5369608)).

## Контекст

Качество оценки зависит от модели. Было решено использовать `GigaChat-2-Pro`.
Первый след в git — `appsettings.example.json` в коммите 5369608: до него
раздела `GigaChat` в примере не было.

## Решение

Оценка вакансий выполняется моделью `GigaChat-2-Pro`, заданной в конфигурации
`GigaChat:Model`.

Текущее состояние кода:

- код-дефолт модели — `GigaChat-2-Pro` —
  [JobAnalyzer.cs:93](../../src/AiRecruiter/Analyzers/JobAnalyzer.cs#L93);
  конфигурация переопределяет значение;
- `GigaChat-2-Pro` задан в `appsettings.example.json` —
  [appsettings.example.json](../../appsettings.example.json).

## Последствия

Для кода:

- модель настраивается без правки кода; без конфигурации работает
  `GigaChat-2-Pro` — совпадает с решением.

Что ещё не сделано:

- «Модель `GigaChat`» (базовый вариант) не используется по умолчанию.
