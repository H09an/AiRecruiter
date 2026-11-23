# 0002. employer_id с детальной страницы вакансии

## Статус

Принято, реализовано.

## Дата

2026-11-18 (коммит
[5369608](https://github.com/H09an/AiRecruiter/commit/5369608)).

## Контекст

Для дедупа по работодателю нужен идентификатор работодателя, а не только ссылка
на вакансию: один работодатель публикует несколько вакансий. Было решено брать
`employer_id` с детальной страницы по селектору
`[data-qa='vacancy-company-name']`. Ссылка на решение —
[AGENTS.md, раздел «Архитектура»](../../AGENTS.md#архитектура).

## Решение

При разборе детальной страницы извлекать `employer_id` из элемента
`[data-qa='vacancy-company-name']` и сохранять в колонку `employer_id` таблицы
`vacancies`.

Текущее состояние кода:

- колонка `employer_id` существует в схеме —
  [VacancyRepository.cs:51-110](../../src/AiRecruiter/Storage/VacancyRepository.cs#L51-L110);
- `employer_id` извлекается из ссылки элемента
  `[data-qa='vacancy-company-name']` —
  [HhParser.cs:391](../../src/AiRecruiter/Parsers/HhParser.cs#L391),
  извлечение —
  [HhParser.cs:1576-1607](../../src/AiRecruiter/Parsers/HhParser.cs#L1576-L1607)
  — и передаётся во все `Upsert` —
  [Program.cs:309](../../src/AiRecruiter/Program.cs#L309),
  [Program.cs:335](../../src/AiRecruiter/Program.cs#L335),
  [Program.cs:360](../../src/AiRecruiter/Program.cs#L360),
  [Program.cs:386](../../src/AiRecruiter/Program.cs#L386),
  [Program.cs:435](../../src/AiRecruiter/Program.cs#L435),
  [Program.cs:410](../../src/AiRecruiter/Program.cs#L410).

## Последствия

Для кода:

- дедуп по работодателю возможен после заполнения колонки; до включения дедупа
  в 3.4 дедуп работает по ссылке вакансии.

Что ещё не сделано:

- дедуп по работодателю — этап 3.4.
