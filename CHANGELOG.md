# Changelog

Все заметные изменения проекта AiRecruiter.
Формат основан на [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/).
Версии соответствуют этапам проекта; SemVer не используется.

## [Unreleased]
<!-- Следующий этап: добавьте записи сюда до тега. -->

### Добавлено
- Предфильтр вакансий по зарплате (`Filters:MinSalary`).
- Предфильтр по стоп-словам в заголовке до GigaChat.
- Порог оценки `Analyzer:MinScore` и калибровочная статистика.
- Состояние вакансий в SQLite (`vacancies.db`).

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

- [Unreleased]: https://github.com/H09an/AiRecruiter/compare/stage-2.7-canon...HEAD
- [2.7]: https://github.com/H09an/AiRecruiter/compare/stage-2.6-infra...stage-2.7-canon
- [2.6]: https://github.com/H09an/AiRecruiter/compare/stage-2.5.1-done...stage-2.6-infra
- [2.5.1]: https://github.com/H09an/AiRecruiter/compare/stage-2.5-done...stage-2.5.1-done
- [2.5]: https://github.com/H09an/AiRecruiter/compare/stage-2-done...stage-2.5-done
- [2.0]: https://github.com/H09an/AiRecruiter/compare/36630af...stage-2-done
- [1.0]: https://github.com/H09an/AiRecruiter/commit/36630af

Правило: в каждый следующий этап CHANGELOG дополняется в том коммите,
что и тег.
