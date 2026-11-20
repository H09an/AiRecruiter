---
name: hh.ru fixtures
description: Change the hh.ru parser safely: synthetic fixtures, expected fields, employer_id scope, Experience whitelist, description sections and parse-samples acceptance.
---

# Парсер hh.ru и фикстуры

Использовать при изменении `HhParser`, селекторов, полей вакансии или
`tests/Fixtures`.

## Источники

- `tests/Fixtures/` — синтетические HTML-страницы, коммитятся.
- `samples/` — реальные страницы, локальные, в git не попадают.
- `--parse-samples` — офлайн-разбор папки HTML.

## Рабочий порядок

1. Воспроизвести поведение на фикстуре:

```sh
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -c Release --no-build -- --parse-samples tests/Fixtures
```

2. Сравнить поля с эталоном: Title, Company, Salary, Experience, WorkFormat,
   Schedule, Employment, Location, HiringFormats, PublishedAt, KeySkills,
   Requirements / Responsibilities / Conditions / Description, TechStack,
   EmployerId.

3. Менять селектор только вместе с фикстурой и тестом.

## Жёсткие правила

- `employer_id` брать только у
  `[data-qa='vacancy-company-name']` на детальной странице.
- Извлекать `/employer/(\d+)`; поддерживать относительные и абсолютные
  ссылки.
- Не брать первый `/employer/` из HTML: на странице бывают decoy-ссылки.
- `Experience` — только белый список: `не требуется`, `без опыта`,
  `1–3 года`, `3–6 лет`, `более 6 лет`, `от N лет`.
- Fallback по тексту не должен давать мусор вроде
  «с системами управления тестированием»; при несовпадении — пусто и лог.
- `DescriptionNotLoaded` — не описание; `HasDescription` его исключает.
- `Requirements`/`Responsibilities`/`Conditions` — секции; `TechStack` —
  отдельный блок, не замена описанию.
- Одна битая карточка не должна срывать обработку всей страницы.

## Тесты

- `HhParserTests` на синтетических фикстурах.
- Новые случаи: `employer_id` из относительной и абсолютной ссылки,
  decoy-ссылки, `Experience` на `only_requirements.html` пустой, поля
  вакансии заполнены.
- Существующие тесты не удалять; менять только с обоснованием.

## Запрещено

- Коммитить `samples/`, реальные страницы и персональные данные.
- Подгонять фикстуру под сломанный селектор без объяснения.
- Добавлять сетевые тесты.
