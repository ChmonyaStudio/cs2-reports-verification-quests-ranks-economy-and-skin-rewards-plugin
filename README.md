# CS2 Player Report

**🌍 Language / Язык:** **[🇬🇧 English](#-english)** · **[🇷🇺 Русский](#-русский)**

![CS2](https://img.shields.io/badge/CS2-CounterStrikeSharp-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![MySQL](https://img.shields.io/badge/MySQL-8.0+-orange)
![Release](https://img.shields.io/github/v/release/ChmonyaStudio/cs2-reports-verification-quests-ranks-economy-and-skin-rewards-plugin)
![License](https://img.shields.io/badge/license-MIT-green)

---

## 🇬🇧 English

A complete player management plugin for CS2 servers: **reports, verification, quests, ranks, economy, and skin rewards** — all in one package.

### 📦 Download

**[⬇️ Download the latest release](https://github.com/ChmonyaStudiocs2-reports-verification-quests-ranks-economy-and-skin-rewards-plugin/releases/latest)**

No compilation required — the release contains the compiled `.dll` and config template.

### ✨ Features

#### 🚨 Reports & Verification
- **!report** — in-game menu with 21 reasons (cheats, griefing, toxicity, etc.)3
- Report cooldown to prevent spam
- Discord webhook notifications for every report
- **!check / !uncheck** — admin marks players for verification
- Team-change lock during verification
- Auto-timeout for verification after N minutes

#### 💰 Economy & Ranks
- Coin-based economy (`kills`, `headshots`, `round wins`)
- XP and level system
- **!rank** — level, XP, balance, playtime, leaderboard position
- **!top** — top-10 by balance
- **!balance** — coin balance
- **!stats** — K/D, headshot %, win %
- **!playtime** — total playtime

#### 🎯 Quest System
- **Daily / weekly / monthly quests**
- Auto-tracking (kills, headshots, round wins, playtime)
- Auto-reset on schedule
- Rewards: XP + coins + random skin
- **!quests** — view progress

#### 🎁 Skin Pool & Rewards
- Add skins to a **random pool** (`!addskinpool`)
- Auto-award on quest completion
- Auto-award on playtime milestones (every 30 hours)
- **!skins** — view remaining skins in pool

#### 👮 Admin Commands
- **!setlevel** — set player level
- **!givecoins** — give coins to a player
- **!resetplayer** — reset stats
- **!reload_config** — reload config without restart
- **!admins** — list online admins

#### 🌐 Server Integration
- Server status in MySQL (`server_status` table)
- Online players count reported to a central dashboard
- Discord webhook for reports and admin actions

### 🚀 Quick Start

#### 1. Requirements
- **CounterStrikeSharp** 1.0.300+
- **.NET 8.0** runtime
- **MySQL 8.0+** database
- Windows or Linux server

#### 2. Database setup

Create a database and run the schema (see [schema.sql](schema.sql)). Tables required:
- `lvl_base` — player stats (from your levels/ranks plugin)
- `skin_pool` — available skins
- `player_skins` — awarded skins
- `player_quests` — quest progress
- `server_status` — server dashboard

#### 3. Install plugin

Extract the release `.zip` to:
```
game/csgo/addons/counterstrikesharp/plugins/PlayerReport/
```

Restart the server.

#### 4. Configure

On first launch, `config.json` is created. Edit:

```json
{
  "DatabaseConfig": {
    "DatabaseHost": "127.0.0.1",
    "DatabasePort": 3306,
    "DatabaseUser": "root",
    "DatabasePassword": "your_password",
    "DatabaseName": "cs2_server"
  },
  "ServerConfig": {
    "ServerId": 1,
    "ServerName": "My CS2 Server",
    "ServerIP": "127.0.0.1",
    "ServerPort": 27015,
    "GameMode": "PUBLIC"
  },
  "DiscordWebhooks": {
    "ReportWebhook": "https://discord.com/api/webhooks/...",
    "AdminWebhook": "https://discord.com/api/webhooks/..."
  }
}
```

#### 5. Test

Join server, type `!help` in chat.

### 🎮 Commands

| Command | Access | Description |
|---|---|---|
| `!report` | Everyone | File a report |
| `!balance` | Everyone | Check coin balance |
| `!quests` | Everyone | View active quests |
| `!rank` | Everyone | Level, XP, rank |
| `!top` | Everyone | Top-10 by balance |
| `!playtime` | Everyone | Total playtime |
| `!stats` | Everyone | K/D, headshots |
| `!contact <discord>` | Everyone | Leave contact for admin |
| `!admins` | Everyone | Online admins |
| `!check <player>` | Admin | Mark for verification |
| `!uncheck <player>` | Admin | Remove verification |
| `!setlevel <player> <level>` | Admin | Set level |
| `!givecoins <player> <amount>` | Admin | Give coins |
| `!resetplayer <player>` | Admin | Reset stats |
| `!addskinpool <defidx> <paint> <wear> <qty> <name>` | Admin | Add skin to pool |
| `!skins` | Everyone | Remaining skins |
| `!reload_config` | Admin | Reload config |

### 🛠️ Requirements

- CounterStrikeSharp 1.0.300+
- .NET 8.0
- MySqlConnector 2.3.5+
- MySQL 8.0+

### 📋 Changelog

See [CHANGELOG.md](CHANGELOG.md).

### ⚠️ License

MIT — see [LICENSE](LICENSE).

### 💬 Contact

- **GitHub Issues:** [report a bug](https://github.com/ChmonyaStudio/cs2-reports-verification-quests-ranks-economy-and-skin-rewards-plugin/issues)
- **Telegram:** [@YOUR_TELEGRAM](https://t.me/p1zdabol4ik)

---

## 🇷🇺 Русский

Полный плагин управления игроками для CS2 серверов: **жалобы, проверки, квесты, ранги, экономика и скины** — всё в одном пакете.

### 📦 Скачать

**[⬇️ Скачать последний релиз](https://github.com/ChmonyaStudio/cs2-reports-verification-quests-ranks-economy-and-skin-rewards-plugin/releases/latest)**

Сборка не нужна — в релизе готовый `.dll` и шаблон конфига.

### ✨ Возможности

#### 🚨 Жалобы и проверки
- **!report** — внутриигровое меню с 21 причиной (читы, гриферство, токсичность и т.д.)
- Кулдаун между жалобами
- Discord-вебхук на каждую жалобу
- **!check / !uncheck** — админ помечает игрока на проверку
- Блок смены команды во время проверки
- Авто-таймаут проверки через N минут

#### 💰 Экономика и ранги
- Экономика на монетах (`киллы`, `хедшоты`, `победа в раунде`)
- Система опыта и уровней
- **!rank** — уровень, XP, баланс, время игры, позиция в топе
- **!top** — топ-10 по балансу
- **!balance** — баланс монет
- **!stats** — K/D, % хедшотов, % побед
- **!playtime** — общее время игры

#### 🎯 Система квестов
- **Ежедневные / недельные / месячные квесты**
- Авто-трекинг (киллы, хедшоты, победы, время)
- Авто-сброс по расписанию
- Награды: XP + монеты + случайный скин
- **!quests** — прогресс

#### 🎁 Пул скинов и награды
- Добавление скинов в **случайный пул** (`!addskinpool`)
- Авто-выдача за квесты
- Авто-выдача за время игры (каждые 30 часов)
- **!skins** — оставшиеся скины в пуле

#### 👮 Админ-команды
- **!setlevel** — установить уровень
- **!givecoins** — выдать монеты
- **!resetplayer** — сбросить статистику
- **!reload_config** — перезагрузить конфиг без рестарта
- **!admins** — список админов онлайн

#### 🌐 Интеграция с сервером
- Статус сервера в MySQL (таблица `server_status`)
- Онлайн-игроки для центральной панели
- Discord-вебхук для жалоб и действий админов

### 🚀 Быстрый старт

#### 1. Требования
- **CounterStrikeSharp** 1.0.300+
- **.NET 8.0** runtime
- **MySQL 8.0+** база данных
- Windows или Linux сервер

#### 2. Настройка БД

Создай базу и примени схему (см. [schema.sql](schema.sql)). Нужны таблицы:
- `lvl_base` — статистика игроков
- `skin_pool` — доступные скины
- `player_skins` — выданные скины
- `player_quests` — прогресс квестов
- `server_status` — панель серверов

#### 3. Установка

Распакуй релиз в:
```
game/csgo/addons/counterstrikesharp/plugins/PlayerReport/
```

Перезапусти сервер.

#### 4. Настройка

При первом запуске создаётся `config.json`. Открой и настрой:

```json
{
  "DatabaseConfig": {
    "DatabaseHost": "127.0.0.1",
    "DatabasePort": 3306,
    "DatabaseUser": "root",
    "DatabasePassword": "твой_пароль",
    "DatabaseName": "cs2_server"
  },
  "ServerConfig": {
    "ServerId": 1,
    "ServerName": "Мой CS2 сервер",
    "ServerIP": "127.0.0.1",
    "ServerPort": 27015,
    "GameMode": "PUBLIC"
  },
  "DiscordWebhooks": {
    "ReportWebhook": "https://discord.com/api/webhooks/...",
    "AdminWebhook": "https://discord.com/api/webhooks/..."
  }
}
```

#### 5. Проверка

Зайди на сервер, напиши `!help`.

### 🎮 Команды

| Команда | Доступ | Описание |
|---|---|---|
| `!report` | Все | Подать жалобу |
| `!balance` | Все | Проверить баланс |
| `!quests` | Все | Активные квесты |
| `!rank` | Все | Уровень, XP, место |
| `!top` | Все | Топ-10 по балансу |
| `!playtime` | Все | Время в игре |
| `!stats` | Все | K/D, хедшоты |
| `!contact <дискорд>` | Все | Оставить контакт |
| `!admins` | Все | Админы онлайн |
| `!check <игрок>` | Админ | Пометить на проверку |
| `!uncheck <игрок>` | Админ | Снять с проверки |
| `!setlevel <игрок> <уровень>` | Админ | Установить уровень |
| `!givecoins <игрок> <кол-во>` | Админ | Выдать монеты |
| `!resetplayer <игрок>` | Админ | Сбросить статистику |
| `!addskinpool <defidx> <paint> <wear> <кол-во> <имя>` | Админ | Добавить скин в пул |
| `!skins` | Все | Оставшиеся скины |
| `!reload_config` | Админ | Перезагрузить конфиг |

### 🛠️ Требования

- CounterStrikeSharp 1.0.300+
- .NET 8.0
- MySqlConnector 2.3.5+
- MySQL 8.0+

### 📋 Changelog

См. [CHANGELOG.md](CHANGELOG.md).

### ⚠️ Лицензия

MIT — см. [LICENSE](LICENSE).

### 💬 Контакты

- **GitHub Issues:** [сообщить о баге](https://github.com/ChmonyaStudio/cs2-reports-verification-quests-ranks-economy-and-skin-rewards-plugin/issues)
- **Telegram:** [@YOUR_TELEGRAM](https://t.me/p1zdabol4ik)

---

**Made with ❤️ for the CS2 community. / Сделано с ❤️ для CS2-сообщества.**
