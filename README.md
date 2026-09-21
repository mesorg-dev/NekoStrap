# NekoStrap — кастомный лаунчер Roblox

[![Release](https://img.shields.io/github/v/release/mesorg-dev/NekoStrap?style=flat-square)](https://github.com/mesorg-dev/NekoStrap/releases)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square)
![WPF](https://img.shields.io/badge/UI-WPF-000000?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)

Лёгкий лаунчер Roblox на C# (.NET 8, WPF, один exe). Безрамочное окно,
строгая чёрно-белая тема, жидкое стекло поверх своих обоев (фото и гифки),
свои звуки интерфейса. Вся логика запуска/версий/модов/флагов — рабочая.

Telegram-канал: https://t.me/mesorgdev

## Скачать

Раздел [Releases](https://github.com/mesorg-dev/NekoStrap/releases) → `NekoStrap.exe`
(Windows 10/11 x64, ничего ставить не надо). Дальше лаунчер обновляется сам:
при старте тихо проверяет релизы, новая версия — раздел «О программе».

## Функции

**Главная** — установка/проверка клиента, кнопка «Играть» с прогрессом,
статус, статы (моды/флаги/часы), последняя игра + «Играть снова», карточка
сервера во время игры (IP, гео, пинг).

**Моды** — файлы из папки `Mods`, вкл/выкл/удалить, Fleasion (скачивание
с GitHub, автозапуск).

**FastFlags** — таблица флагов активной версии, живой поиск, пресеты
(FPS, свет, MSAA, Vulkan…), импорт/экспорт JSON, удаление по одному и все сразу.

**Версии** — установленные клиенты (активация/удаление/откат), Studio.

**Аккаунты** — мультиаккаунты по куке (DPAPI-шифр, только этот ПК),
запуск под выбранным, бэкап/импорт.

**История** — наигранное по плейсам, лог заходов с JobId: «на тот же сервер»,
«на плейс», копии ссылок.

**Настройки** — тумблеры без кнопки «сохранить», путь Roblox, CDN-фикс для РФ,
обои (фото/гифки, блюр, затемнение), стекло (прозрачность, блюр подложки,
затемнение элементов), свои звуки интерфейса (wav + громкость на каждый звук).

**Трей и фон** — один экземпляр, сворачивание в трей, музыка из папки `Music`,
цветокоррекция экрана (Magnification API, без инъекций) с тюнером, сервер-хоп.

## Сборка из исходников

Нужны **.NET 8 SDK** и **Windows**.

```bash
dotnet restore
dotnet build NekoStrap.sln -c Release
```

Одиночный self-contained exe:

```bash
dotnet publish NekoStrap.Wpf/NekoStrap.Wpf.csproj -c Release -r win-x64 \
  --self-contained true /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true -o build-wpf
```

Дымовой тест всего UI (без показа окон, код возврата = число провалов):

```bash
dotnet run --project NekoStrap.Smoke -c Release
```

## Релизы

1. Подними версию, если надо (`<Version>` в `NekoStrap.Wpf.csproj`).
2. Запуш тега: `git tag v1.1.0 && git push origin v1.1.0`.
3. Workflow `.github/workflows/release.yml` соберёт `NekoStrap.exe` и приложит
   к GitHub-релизу. Лаунчер подхватит его сам (тег `vX.Y.Z`, ассет `.exe`).

Соглашение для апдейтера: тег вида `v1.2.3`, в ассетах релиза — один
`NekoStrap.exe` (можно с `win-x64` в имени).

## Структура

```
NekoStrap/
├── NekoStrap.sln
├── NekoStrap.Wpf/        # приложение: окно, страницы, тема, трей
│   ├── Theme/Theme.xaml  # палитра, стили, иконки
│   ├── Pages/            # 8 разделов + диалоги
│   ├── Glass.cs          # жидкое стекло (блюр обоев под панелями)
│   ├── GifPlayer.cs      # анимированные GIF-обои
│   └── MainWindow.*      # хром окна + весь бэкенд
├── NekoStrap.Core/       # общая логика без UI (библиотека):
│   ├── Roblox/           #   установщик, запуск, версии, моды, флаги,
│   │                     #   аккаунты, сессии, Discord, CDN, апдейтер
│   ├── Media/            #   музыка (NAudio)
│   ├── Utils/            #   звуки интерфейса
│   ├── Display/          #   цветокоррекция (Magnification API)
│   └── Assets/           #   встроенные wav
├── NekoStrap.Smoke/      # дымовой тест UI
└── .github/workflows/    # CI: сборка релизного exe по тегу
```

## Данные

Всё лежит в `%LocalAppData%\NekoStrap` (или в своей папке, если задан
свой путь Roblox): `config.json`, версии клиента, `Mods/`, `Music/`,
флаги, аккаунты (DPAPI-шифр), история. Реестр лаунчер не трогает.

## Лицензия

MIT — см. [LICENSE](LICENSE). Установка, FastFlags, Discord, Fleasion
и плеер — по мотивам Voidstrap (MIT).
