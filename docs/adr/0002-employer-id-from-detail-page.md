# 0002. employer_id с детальной страницы вакансии

## Статус

Принято, не реализовано.

## Дата

не проверено.

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
  [VacancyRepository.cs:50-70](../../src/AiRecruiter/Storage/VacancyRepository.cs#L50-L70),
  но во всех местах записи передаётся null —
  [Program.cs:207](../../src/AiRecruiter/Program.cs#L207),
  [Program.cs:336](../../src/AiRecruiter/Program.cs#L336),
  [Program.cs:358](../../src/AiRecruiter/Program.cs#L358),
  [Program.cs:384](../../src/AiRecruiter/Program.cs#L384),
  [Program.cs:404](../../src/AiRecruiter/Program.cs#L404),
  [Program.cs:425](../../src/AiRecruiter/Program.cs#L425);
- из этого же элемента парсер извлекает только название компании —
  [HhParser.cs:380](../../src/AiRecruiter/Parsers/HhParser.cs#L380);
  `employer_id` из него не вычисляется.

## Последствия

Для кода:

- дедуп по работодателю возможен только после заполнения колонки; до тех пор
  дедуп работает по ссылке вакансии.

Что ещё не сделано:

- извлечение `employer_id` в парсере и передача его через все `Upsert` —
  запланировано на 3.2a.
