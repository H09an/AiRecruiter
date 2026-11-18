# Changelog

Все заметные изменения проекта AiRecruiter.
Формат основан на [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/).
Версии соответствуют этапам проекта; SemVer не используется.

## [Unreleased]
<!-- Следующий этап: добавьте записи сюда до тега. -->

### Документация
- Добавлен `docs/architecture.md`: текущее состояние кода — поток одного прогона,
  модули, статусы, схема БД, известные расхождения с решениями.
- Добавлены `docs/adr/0001`–`0008`: решения задним числом в формате MADR-lite
  (SQLite, `employer_id`, модель GigaChat, `MaxTokens`, стоп-слова, гибридный
  парсер, порог и shadow mode, уровень кандидата).
- `AGENTS.md`: раздел «Документация» с правилом «документ в том же коммите, что
  и код», формат ссылок на код и чеклист перед коммитом; в журнале решений —
  подраздел «Данные и журнал (этап 3.2a)».
- `README.md` приведён к коду: состояние в `vacancies.db` вместо
  `seen_vacancies.txt`, значения по умолчанию модели и `MaxTokens`,
  фактическое число тестов.
- Добавлен `scripts/check-docs.sh`: предупреждения о необновлённой документации,
  битых ссылках на код, битых обычных ссылках и голых записях пути со строками;
  вызов добавлен в `lefthook.yml` (pre-commit) и в CI (шаг «Docs check»).

## [3.1] - 2026-11-17

### Добавлено
- Предфильтр вакансий по зарплате (`Filters:MinSalary`).
- Предфильтр по стоп-словам в заголовке до GigaChat (`Filters:ExcludeTitleWords`).
- Порог оценки `Analyzer:MinScore` и калибровочная статистика (гистограмма в `[ИТОГ]`).
- Состояние вакансий в SQLite (`vacancies.db`) с миграцией из `seen_vacancies.txt`.
- Повтор отправки из сохранённого результата (`send_failed`, `analyzed_ok`) без повторного анализа.
- Колонки `comment`, `cover_letter`, `tech_stack` в таблице `vacancies`.

### Изменено
- Фильтр стажировок перенесён из `TelegramNotifier` в предфильтр по заголовку.
- `analysis_failed` не блокирует вакансию: она переанализируется при следующем появлении в выдаче.
- Правило в промпте оценки: не выводить уровень кандидата из общего стажа.
- Лимит сводки кандидата `candidate_summary.md` поднят до 4000 символов.
- Ужесточён разбор чисел в `SalaryFilter`: группы по три цифры, без склейки соседних чисел.
- Добавлен `AGENTS.md`: рамка процесса для агентов и журнал решений.

## [2.7] - 2026-11-16

### Добавлено
- `CHANGELOG.md`.
- Каноническая раскладка `src/AiRecruiter/`.
- Центральное управление версиями пакетов (`Directory.Packages.props`).
- `LICENSE` (MIT), `.gitattributes`, `lefthook.yml`.
- Тесты расширены до 44.

### Изменено
- Сборка приведена к 0 предупреждений (было 124).
- README переписан для публичного репозитория.
- CI усилен: проверка форматирования и TRX-артефакт.
- `HhParser` стал статическим; `TelegramNotifier` реализует `IDisposable`.
- Чтение порогов вынесено в `IntConfigReader`.
- Регрессионные тесты парсера переведены на синтетические фикстуры
  `tests/Fixtures/`.

### Удалено
- `samples/archive/`; сэмплы `sample_2`, `sample_4`, `sample_6`, `sample_7`.
- Сэмплы hh.ru исключены из репозитория.

### Исправлено
- Устранены предупреждения CA1051, CA1001, CA1310, CA1305, CA1866, CA1861,
  CA1869, CA1304, CA1311.
- Восстановлен DTO-тест после регрессии.

## [2.6] - 2026-11-16

### Добавлено
- Тесты xunit.
- CI.
- `Directory.Build.props`.
- `AiRecruiter.slnx`.
- `.editorconfig`.

### Исправлено
- Try-фиксы.

## [2.5.1] - 2026-11-15

### Добавлено
- TechStack.
- `sample_10`.

### Изменено
- Косметические правки.

### Исправлено
- Согласованность `comment` и `must_have_missing`.
- Смягчение проверки «Противоречие».

## [2.5] - 2026-11-14

### Добавлено
- Пороги по группам.
- Фильтр стажировок.
- Алиас «Что надо делать».

## [2.0] - 2026-11-13

### Добавлено
- Парсер hh.ru.
- Промпт GigaChat.
- Каскады.
- Логи.
- Перестраховка на смену вёрстки.

## [1.0] - 2026-11-13

### Добавлено
- Первая рабочая версия: парсер hh.ru, оценка через GigaChat,
  отправка в Telegram.

## Ссылки

- [Unreleased]: https://github.com/H09an/AiRecruiter/compare/stage-3.1-core...HEAD
- [3.1]: https://github.com/H09an/AiRecruiter/compare/stage-2.7-canon...stage-3.1-core
- [2.7]: https://github.com/H09an/AiRecruiter/compare/stage-2.6-infra...stage-2.7-canon
- [2.6]: https://github.com/H09an/AiRecruiter/compare/stage-2.5.1-done...stage-2.6-infra
- [2.5.1]: https://github.com/H09an/AiRecruiter/compare/stage-2.5-done...stage-2.5.1-done
- [2.5]: https://github.com/H09an/AiRecruiter/compare/stage-2-done...stage-2.5-done
- [2.0]: https://github.com/H09an/AiRecruiter/compare/4cabd92...stage-2-done
- [1.0]: https://github.com/H09an/AiRecruiter/commit/4cabd92

Правило: в каждый следующий этап CHANGELOG дополняется в том коммите,
что и тег.
