# AiRecruiter

Консольное приложение: собирает новые вакансии с hh.ru, оценивает их соответствие резюме через GigaChat и отправляет подходящие в Telegram.

## Требования

- .NET SDK 10 (версия зафиксирована в `global.json`).
  Arch Linux: `sudo pacman -S dotnet-sdk dotnet-runtime dotnet-targeting-pack`

## Запуск

```sh
cp appsettings.example.json appsettings.json   # заполнить Search и Contacts
export GIGACHAT_AUTH_KEY=...
export TELEGRAM_BOT_TOKEN=...
export TELEGRAM_CHAT_ID=...
dotnet run --project AiRecruiter.csproj
```

Рядом с рабочей директорией ожидаются файлы (не коммитятся):

| Файл | Назначение |
|---|---|
| `resume.txt` | текст резюме |
| `profile.md` | профиль кандидата и критерии |
| `candidate_summary.md` | сводка навыков (опционально) |
| `seen_vacancies.txt` | уже обработанные ссылки; ведётся автоматически |

Ctrl+C — остановиться после текущей вакансии, повторный Ctrl+C — немедленный выход.

## Конфигурация

Секреты — только через переменные окружения:

| Переменная | Обязательна | Описание |
|---|---|---|
| `GIGACHAT_AUTH_KEY` | да | ключ авторизации GigaChat |
| `TELEGRAM_BOT_TOKEN` | да | токен бота |
| `TELEGRAM_CHAT_ID` | да | id чата (целое число) |
| `TELEGRAM_PROXY_URL`, `TELEGRAM_PROXY_USER`, `TELEGRAM_PROXY_PASSWORD` | нет | прокси для Telegram |

Остальное — `appsettings.json` или переменные окружения с `__` вместо `:` (например `Contacts__Email`):

| Ключ | По умолчанию | Описание |
|---|---|---|
| `Search:Query`, `Search:AreaCode` | — | поисковый запрос и регион hh.ru (`113` — Россия, только удалёнка; `4` — Новосибирск, любой формат) |
| `Contacts:Fio`, `Tg`, `Phone`, `Email` | — | контакты для сопроводительного письма |
| `GigaChat:Temperature` / `TopP` / `MaxTokens` | 0.15 / 0.9 / 800 | параметры модели |
| `HhParser:Thresholds:*` | см. `Program.cs` | пороги алерта о смене вёрстки hh.ru |

Политика отправки: оценка ≥ 7 — «🔥 откликаться», 3–6 — «💭 на подумать», ниже — только в лог.

## Диагностика

```sh
dotnet run --project AiRecruiter.csproj -- --parse-samples samples           # парсер на сохранённых HTML
dotnet run --project AiRecruiter.csproj -- --replay-failed failed_responses  # повторный разбор ответов GigaChat
```

Лог — `app.log` (ротация при 5 МБ).

## Тесты

```sh
dotnet test AiRecruiter.slnx
```

- `HhParserTests` — регрессия парсера на страницах из `samples/`. После смены вёрстки hh.ru добавьте свежий sample и обновите ожидания.
- `JobAnalyzerDtoTests` — контракт разбора JSON-ответа GigaChat.
