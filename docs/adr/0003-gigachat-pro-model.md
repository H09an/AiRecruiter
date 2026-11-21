# 0003. Модель GigaChat-2-Pro

## Статус

Принято, реализовано частично.

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

- код читает `GigaChat:Model`, значение по умолчанию — `GigaChat` —
  [JobAnalyzer.cs:99](../../src/AiRecruiter/Analyzers/JobAnalyzer.cs#L99);
- `GigaChat-2-Pro` задан только в локальном `appsettings.json`, которого нет в
  git; в репозитории есть `appsettings.example.json` с тем же значением —
  [appsettings.example.json](../../appsettings.example.json).

## Последствия

Для кода:

- модель настраивается без правки кода; при отсутствии конфигурации используется
  `GigaChat` по умолчанию, что не совпадает с принятым решением.

Что ещё не сделано:

- код-дефолт модели — `GigaChat`; поднять до `GigaChat-2-Pro` в 3.2a (шаг 5).
