# AiRecruiter

Консольное приложение на .NET 10: парсит hh.ru через AngleSharp (без
официального API), оценивает вакансии через GigaChat и отправляет подходящие в
Telegram.

[![CI](https://github.com/H09an/AiRecruiter/actions/workflows/ci.yml/badge.svg)](https://github.com/H09an/AiRecruiter/actions/workflows/ci.yml)

## Что делает

1. Парсит hh.ru через AngleSharp (без официального API).
2. Анализирует вакансии через GigaChat с учётом `candidate_summary.md` —
   единственного источника для оценки
   ([JobAnalyzer.cs:392](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L392)).
3. Отправляет подходящие вакансии в Telegram с оценкой соответствия.

## Требования

- .NET SDK 10 (версия зафиксирована в `global.json`).
- Скачать: https://dotnet.microsoft.com/download/dotnet/10.0
- Ubuntu/Debian: `sudo apt install dotnet-sdk-10.0`

## Установка и быстрый старт

```sh
# 1. Клонируйте репозиторий и перейдите в его корень
git clone <url>
cd AiRecruiter

# 2. Конфигурация: заполните Search:Query, Search:AreaCode и Contacts:*
cp appsettings.example.json appsettings.json

# 3. Входные файлы в корне репозитория:
#    candidate_summary.md — сводка навыков; единственный источник для оценки
#                           GigaChat; обязательна: без неё (или если файл пуст)
#                           программа останавливается до парсинга
#    resume.txt          — не читается
#    profile.md          — не читается
#    Контакты Contacts:Fio/Tg/Phone/Email — обязательны; в оценке не
#                           участвуют (ADR-0011)

# 4. Секреты
export GIGACHAT_AUTH_KEY=...
export TELEGRAM_BOT_TOKEN=...
export TELEGRAM_CHAT_ID=...

# 5. Запуск
dotnet run --project src/AiRecruiter/AiRecruiter.csproj
```

Запускайте приложение из корня репозитория: `appsettings.json` и `app.log`
читаются и пишутся относительно текущей рабочей директории. Состояние вакансий —
в SQLite-базе `vacancies.db`, она создаётся автоматически
([Program.cs:198-199](src/AiRecruiter/Program.cs#L198-L199)).

### Где взять секреты

| Переменная | Где получить |
|---|---|
| `GIGACHAT_AUTH_KEY` | личный кабинет GigaChat API (Сбер): авторизационные данные проекта |
| `TELEGRAM_BOT_TOKEN` | [@BotFather](https://t.me/BotFather): `/newbot` → токен вида `123456789:AA…` |
| `TELEGRAM_CHAT_ID` | id чата или канала (целое число); удобно узнать через `https://api.telegram.org/bot<TOKEN>/getUpdates` |

## Файлы рабочей директории

Все файлы читаются из **текущей рабочей директории**, а не из папки проекта:

| Файл | Назначение |
|---|---|
| `resume.txt` | не читается: в оценке не участвует, в промпт не попадает |
| `profile.md` | не читается: в оценке не участвует, в промпт не попадает |
| `candidate_summary.md` | сводка навыков; единственный источник для оценки; читается один раз в `Program` и обязательна — [CandidateSummaryValidator.cs](src/AiRecruiter/Analyzers/CandidateSummaryValidator.cs); без неё (или если пуста) — остановка до парсинга |
| `vacancies.db` | состояние вакансий (SQLite), создаётся автоматически — [Program.cs:198-199](src/AiRecruiter/Program.cs#L198-L199), не коммитится |

`resume.txt` и `profile.md` не читаются. Если `candidate_summary.md` отсутствует
или пуст, программа завершает работу до парсинга, анализа и отправки
([Program.cs:145-159](src/AiRecruiter/Program.cs#L145-L159)). Контакты
`Contacts:Fio/Tg/Phone/Email` обязательны: без всех четырёх приложение
завершает работу ([Program.cs:139-143](src/AiRecruiter/Program.cs#L139-L143));
в оценке они не участвуют ([ADR-0011](docs/adr/0011-contacts-in-cover-letter.md)).

Ctrl+C — остановиться после текущей вакансии, повторный Ctrl+C — немедленный
выход.

## Конфигурация

Секреты — только через переменные окружения:

| Переменная | Обязательна | Описание |
|---|---|---|
| `GIGACHAT_AUTH_KEY` | да | ключ авторизации GigaChat |
| `TELEGRAM_BOT_TOKEN` | да | токен бота |
| `TELEGRAM_CHAT_ID` | да | id чата (целое число) |
| `TELEGRAM_PROXY_URL`, `TELEGRAM_PROXY_USER`, `TELEGRAM_PROXY_PASSWORD` | нет | прокси для Telegram |

Остальное — `appsettings.json` (в корне репозитория) или переменные окружения с
`__` вместо `:` (например `Contacts__Email`):

| Ключ | По умолчанию | Описание |
|---|---|---|
| `Search:Query`, `Search:AreaCode` | — | поисковый запрос и регион hh.ru (`113` — Россия, только удалёнка; `4` — Новосибирск, любой формат) |
| `Contacts:Fio`, `Tg`, `Phone`, `Email` | — | контакты кандидата; все четыре обязательны и проверяются на старте ([Program.cs:113-143](src/AiRecruiter/Program.cs#L113-L143)); в оценке не участвуют ([ADR-0011](docs/adr/0011-contacts-in-cover-letter.md)) |
| `GigaChat:Model` | `GigaChat-2-Pro` | название модели; код-дефолт совпадает с решением ([JobAnalyzer.cs:93](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L93)), [ADR-0003](docs/adr/0003-gigachat-pro-model.md) |
| `GigaChat:Temperature` / `TopP` / `MaxTokens` | 0.15 / 0.9 / 1200 | параметры модели; `MaxTokens` код-дефолт 1200 ([JobAnalyzer.cs:96](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L96)), [ADR-0004](docs/adr/0004-max-tokens-1200.md) |
| `HhParser:Thresholds:*` | 5 вакансий; 80 / 80 / 10 / 10 / 10 % | пороги алерта о смене вёрстки hh.ru: `MinVacanciesForAlert`, `RequirementsPercent`, `DescriptionPercent`, `ResponsibilitiesPercent`, `KeySkillsPercent`, `ConditionsPercent` — [Program.cs:213-218](src/AiRecruiter/Program.cs#L213-L218) |
| `Filters:MinSalary` | `0` | минимальная зарплата в рублях; 0 — фильтр выключен; вакансии без зарплаты и в других валютах проходят |
| `Filters:ExcludeTitleWords` | `[]` | слова в заголовке, при которых вакансия пропускается до GigaChat |
| `Analyzer:MinScore` | `3` | минимальная оценка для отправки в Telegram |

Политика отправки: отправка при `score >= Analyzer:MinScore` (по умолчанию 3).
Метка «🔥 откликаться» при score ≥ 7, «💭 на подумать» при 3–6. Вакансии ниже
порога — только в лог (и в БД со статусом `analyzed_low`); в Telegram не
отправляются.

## Структура репозитория

```
AiRecruiter/
├── src/AiRecruiter/          # код приложения
│   ├── Analyzers/            # оценка вакансий через GigaChat
│   ├── Configuration/        # чтение настроек
│   ├── Filters/              # предфильтры (зарплата, стоп-слова)
│   ├── Models/               # модели данных
│   ├── Notifiers/            # отправка в Telegram
│   ├── Storage/              # SQLite-хранилище вакансий
│   └── Parsers/              # парсинг страниц hh.ru
├── docs/                     # документация
│   ├── architecture.md       # текущее состояние кода
│   ├── roadmap.md            # этапы и критерии готовности
│   └── adr/                  # архитектурные решения (ADR)
├── scripts/                  # вспомогательные скрипты (check-docs.sh)
├── tests/AiRecruiter.Tests/  # тесты xUnit
├── tests/Fixtures/           # синтетические фикстуры парсера
├── samples/                  # ваши HTML-страницы hh.ru (не коммитится)
├── .github/workflows/        # CI (GitHub Actions)
├── appsettings.json          # конфигурация (не коммитится)
├── appsettings.example.json  # пример конфигурации
├── candidate_summary.md      # сводка навыков (не коммитится)
├── resume.txt                # резюме (не коммитится)
├── profile.md                # профиль кандидата (не коммитится)
├── vacancies.db              # состояние вакансий, SQLite (не коммитится)
├── app.log                   # лог приложения
├── Directory.Build.props     # общие свойства сборки
├── Directory.Packages.props  # версии NuGet-пакетов
├── global.json               # версия .NET SDK
├── lefthook.yml              # необязательные git-хуки
├── CHANGELOG.md              # история изменений
├── LICENSE                   # лицензия MIT
├── SECURITY.md               # политика безопасности
└── README.md                 # этот файл
```

## Диагностика

```sh
# Парсер на сохранённых HTML-страницах
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -- --parse-samples samples

# Повторный разбор ответов GigaChat
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -- --replay-failed failed_responses
```

`--parse-samples` читает любую папку с HTML-страницами hh.ru. Реальные страницы
в репозиторий не входят: сохраните нужные вакансии в `samples/` — папка указана
в `.gitignore`. Быстрая проверка на синтетических фикстурах:

```sh
dotnet run --project src/AiRecruiter/AiRecruiter.csproj -- --parse-samples tests/Fixtures
```

Лог — `app.log`; при превышении 5 МБ файл очищается.

## Решение проблем

| Симптом | Что делать |
|---|---|
| `GIGACHAT_AUTH_KEY` не установлена | экспортировать переменную в текущей сессии |
| `Ошибка: не заданы контакты: …` | заполнить `Contacts:Fio`, `Tg`, `Phone`, `Email` |
| `TELEGRAM_CHAT_ID` должен быть целым числом | указать число без `@` и кавычек |
| `401` от GigaChat | проверить ключ, срок его действия и доступ к `gigachat.devices.sberbank.ru` |
| `403` или пустая выдача от hh.ru | снизить частоту запусков, проверить сеть и прокси |
| `⚠️ HhParser: подозрение на смену вёрстки` | прогнать `--parse-samples samples` и обновить селекторы/сэмплы |
| Telegram молчит | проверить `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` и прокси-переменные |

## Тесты

```sh
dotnet test AiRecruiter.slnx
```

- `HhParserTests` — регрессия парсера на синтетических фикстурах из
  `tests/Fixtures/`. Реальные страницы hh.ru проверяйте локально через
  `--parse-samples` и обновляйте ожидания при смене вёрстки.
- `JobAnalyzerDtoTests` — контракт разбора JSON-ответа GigaChat.
- `IntConfigReaderTests` — чтение целых чисел из конфигурации.
- `ConfigListTests` — чтение списков строк из конфигурации.
- `SalaryFilterTests` — разбор строки зарплаты и предфильтр по порогу.
- `TitleFilterTests` — поиск стоп-слов в заголовке вакансии.
- `VacancyRepositoryTests` — SQLite-хранилище вакансий.

Всего 123 теста.

## Разработка

```sh
# Проверка форматирования
dotnet format AiRecruiter.slnx --verify-no-changes --no-restore

# Проверка документации: ссылки на код, обычные markdown-ссылки,
# голые записи вида «путь:строка». Печатает предупреждения, не ошибки;
# код возврата всегда 0.
bash scripts/check-docs.sh
```

Для PR: 0 предупреждений при сборке.

История изменений — в [CHANGELOG.md](CHANGELOG.md). План этапов — в
[docs/roadmap.md](docs/roadmap.md).

### Локальные хуки (необязательно)

Хуки lefthook не устанавливаются по умолчанию. CI выполняет те же проверки.

```sh
# установите lefthook по инструкции: https://github.com/evilmartians/lefthook#install
lefthook install
```

## Ограничения и этика

- hh.ru — только для личного некоммерческого использования; соблюдайте
  `robots.txt`.
- Задержка 1–2 секунды между запросами к странице вакансии; 4–8 секунд между
  страницами поиска.
- До 11 страниц поиска за один прогон (page 0–10); выход раньше — по лимиту
  новых вакансий.
- Не используйте для массового сбора данных или коммерческих целей без
  разрешения hh.ru.
- Проект не связан с hh.ru и Сбером. hh.ru, GigaChat и Telegram — товарные знаки
  соответствующих правообладателей.

## Безопасность

Как сообщить об уязвимости — см. [SECURITY.md](SECURITY.md).

## Лицензия

MIT License. Подробнее в [LICENSE](LICENSE).
