# AiRecruiter

Консольное приложение на .NET 10: парсит hh.ru через AngleSharp (без официального API), оценивает вакансии через GigaChat и отправляет подходящие в Telegram.

[![CI](https://github.com/H09an/AiRecruiter/actions/workflows/ci.yml/badge.svg)](https://github.com/H09an/AiRecruiter/actions/workflows/ci.yml)

## Что делает

1. Парсит hh.ru через AngleSharp (без официального API).
2. Анализирует вакансии через GigaChat с учётом резюме и профиля кандидата.
3. Отправляет подходящие вакансии в Telegram с оценкой соответствия.

## Требования

- .NET SDK 10 (версия зафиксирована в `global.json`).
- Скачать: https://dotnet.microsoft.com/download/dotnet/10.0
- Ubuntu/Debian: `sudo apt install dotnet-sdk-10.0`

## Быстрый старт

```sh
# 1. Клонируйте репозиторий и перейдите в его корень
git clone <url>
cd AiRecruiter

# 2. Конфигурация: заполните Search:Query, Search:AreaCode и Contacts:*
cp appsettings.example.json appsettings.json

# 3. Входные файлы в корне репозитория:
#    resume.txt          — текст резюме (необязателен; иначе заглушка)
#    profile.md          — профиль кандидата и критерии (необязателен; иначе заглушка)
#    candidate_summary.md — сводка навыков (необязательна; иначе пустая сводка)
#    Контакты Contacts:Fio/Tg/Phone/Email — обязательны

# 4. Секреты
export GIGACHAT_AUTH_KEY=...
export TELEGRAM_BOT_TOKEN=...
export TELEGRAM_CHAT_ID=...

# 5. Запуск
dotnet run --project src/AiRecruiter/AiRecruiter.csproj
```

Запускайте приложение из корня репозитория: `appsettings.json` и `app.log` читаются и пишутся относительно текущей рабочей директории. Состояние вакансий — в SQLite-базе `vacancies.db`, она создаётся автоматически ([Program.cs:191-192](src/AiRecruiter/Program.cs#L191-L192)).

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
| `resume.txt` | текст резюме; если отсутствует, подставляется заглушка — [Program.cs:146-147](src/AiRecruiter/Program.cs#L146-L147) |
| `profile.md` | профиль кандидата и критерии; если отсутствует, подставляется заглушка — [Program.cs:146-147](src/AiRecruiter/Program.cs#L146-L147) |
| `candidate_summary.md` | сводка навыков; читает анализатор ([JobAnalyzer.cs:98](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L98)), при отсутствии — пустая сводка |
| `vacancies.db` | состояние вакансий (SQLite), создаётся автоматически — [Program.cs:191-192](src/AiRecruiter/Program.cs#L191-L192), не коммитится |

Если `resume.txt` или `profile.md` отсутствуют, приложение не остановится, но подставит заглушки — оценка вакансий будет бессмысленной. Контакты `Contacts:Fio/Tg/Phone/Email` обязательны: без всех четырёх приложение завершает работу ([Program.cs:138-142](src/AiRecruiter/Program.cs#L138-L142)).

Ctrl+C — остановиться после текущей вакансии, повторный Ctrl+C — немедленный выход.

## Конфигурация

Секреты — только через переменные окружения:

| Переменная | Обязательна | Описание |
|---|---|---|
| `GIGACHAT_AUTH_KEY` | да | ключ авторизации GigaChat |
| `TELEGRAM_BOT_TOKEN` | да | токен бота |
| `TELEGRAM_CHAT_ID` | да | id чата (целое число) |
| `TELEGRAM_PROXY_URL`, `TELEGRAM_PROXY_USER`, `TELEGRAM_PROXY_PASSWORD` | нет | прокси для Telegram |

Остальное — `appsettings.json` (в корне репозитория) или переменные окружения с `__` вместо `:` (например `Contacts__Email`):

| Ключ | По умолчанию | Описание |
|---|---|---|
| `Search:Query`, `Search:AreaCode` | — | поисковый запрос и регион hh.ru (`113` — Россия, только удалёнка; `4` — Новосибирск, любой формат) |
| `Contacts:Fio`, `Tg`, `Phone`, `Email` | — | контакты для сопроводительного письма; все четыре обязательны |
| `GigaChat:Model` | `GigaChat` | название модели; код по умолчанию `GigaChat` ([JobAnalyzer.cs:99](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L99)), в [appsettings.example.json](appsettings.example.json) — `GigaChat-2-Pro` |
| `GigaChat:Temperature` / `TopP` / `MaxTokens` | 0.15 / 0.9 / 800 | параметры модели; `MaxTokens` по умолчанию в коде 800 ([JobAnalyzer.cs:102](src/AiRecruiter/Analyzers/JobAnalyzer.cs#L102)), в [appsettings.example.json](appsettings.example.json) — 1200 |
| `HhParser:Thresholds:*` | см. `src/AiRecruiter/Program.cs` | пороги алерта о смене вёрстки hh.ru |
| `Filters:MinSalary` | `0` | минимальная зарплата в рублях; 0 — фильтр выключен; вакансии без зарплаты и в других валютах проходят |
| `Filters:ExcludeTitleWords` | `[]` | слова в заголовке, при которых вакансия пропускается до GigaChat |
| `Analyzer:MinScore` | `3` | минимальная оценка для отправки в Telegram |

Политика отправки: отправка при `score >= Analyzer:MinScore` (по умолчанию 3). Метка «🔥 откликаться» при score ≥ 7, «💭 на подумать» при 3–6. Вакансии ниже порога — только в лог (и в БД со статусом `analyzed_low`); в Telegram не отправляются.

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

`--parse-samples` читает любую папку с HTML-страницами hh.ru. Реальные страницы в
репозиторий не входят: сохраните нужные вакансии в `samples/` — папка указана в
`.gitignore`. Быстрая проверка на синтетических фикстурах:

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

- `HhParserTests` — регрессия парсера на синтетических фикстурах из `tests/Fixtures/`. Реальные страницы hh.ru проверяйте локально через `--parse-samples` и обновляйте ожидания при смене вёрстки.
- `JobAnalyzerDtoTests` — контракт разбора JSON-ответа GigaChat.
- `IntConfigReaderTests` — чтение целых чисел из конфигурации.
- `ConfigListTests` — чтение списков строк из конфигурации.
- `SalaryFilterTests` — разбор строки зарплаты и предфильтр по порогу.
- `TitleFilterTests` — поиск стоп-слов в заголовке вакансии.
- `VacancyRepositoryTests` — SQLite-хранилище вакансий.

Всего 90 тестов.

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

История изменений — в [CHANGELOG.md](CHANGELOG.md).

### Локальные хуки (необязательно)

Хуки lefthook не устанавливаются по умолчанию. CI выполняет те же проверки.

```sh
# установите lefthook по инструкции: https://github.com/evilmartians/lefthook#install
lefthook install
```

## Ограничения и этика

- hh.ru — только для личного некоммерческого использования; соблюдайте `robots.txt`.
- Задержка 1–2 секунды между запросами к странице вакансии; 4–8 секунд между страницами поиска.
- До 11 страниц поиска за один прогон (page 0–10); выход раньше — по лимиту новых вакансий.
- Не используйте для массового сбора данных или коммерческих целей без разрешения hh.ru.
- Проект не связан с hh.ru и Сбером. hh.ru, GigaChat и Telegram — товарные знаки соответствующих правообладателей.

## Безопасность

Как сообщить об уязвимости — см. [SECURITY.md](SECURITY.md).

## Лицензия

MIT License. Подробнее в [LICENSE](LICENSE).
