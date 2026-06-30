# MarkStudio

> Кроссплатформенный редактор кода с AI-помощником, подсветкой синтаксиса и Git-интеграцией. Сделан для Меня.

Редактор кода как PyCharm.

## Возможности

- Тёмная тема (5 вариантов: DarkPlus, Monokai, OneDark, Dracula, SolarizedDark)
- AI-помощник (Cloudflare Workers AI) — спроси, проверь, объясни, исправь код
- Подсветка синтаксиса 16+ языков (Python, JS, TS, C#, Rust, Go, Java...)
- Проектное дерево с Git-статусами файлов
- Встроенный линтер (ловит ошибки в коде)
- Чат с AI прямо в редакторе
- Горячие клавиши как в IDE

## Требования

- .NET 10 SDK

## Сборка

```bash
dotnet build
dotnet run
```

## Настройка AI

1. Зайди на [Cloudflare Workers AI](https://developers.cloudflare.com/workers-ai/)
2. Создай API токен
3. В MarkStudio: Настройки → введи Account ID и Token

## Горячие клавиши

| Комбинация | Действие |
|---|---|
| `Ctrl+N` | Новый файл |
| `Ctrl+O` | Открыть файл |
| `Ctrl+S` | Сохранить |
| `Ctrl+W` | Закрыть вкладку |
| `Ctrl+Z` / `Ctrl+Y` | Отмена / Повтор |
| `Alt+1` | Скрыть/показать дерево проекта |
| `Alt+F12` | Скрыть/показать нижнюю панель |
| `Alt+A` | Открыть AI-чат |

## Стек

- [Avalonia 12](https://avaloniaui.net/) — UI-фреймворк
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) — редактор кода
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) — Git-интеграция
- [Cloudflare Workers AI](https://developers.cloudflare.com/workers-ai/) — AI-бэкенд

## Лицензия

MIT
