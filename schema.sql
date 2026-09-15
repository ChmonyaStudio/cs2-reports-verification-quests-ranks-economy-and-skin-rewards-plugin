-- ============================================================
-- CS2 Player Report — Database Schema
-- MySQL 8.0+
-- ============================================================

-- ============================================================
-- 1. lvl_base — player stats
-- ============================================================
-- This table is SHARED with Levels Ranks plugin.
-- If you already use Levels Ranks — skip this block.
-- Columns below match the schema expected by PlayerReport.
-- ============================================================

CREATE TABLE IF NOT EXISTS `lvl_base` (
    `steam`       VARCHAR(32)  NOT NULL,
    `name`        VARCHAR(128) NOT NULL DEFAULT 'Player',
    `value`       INT          NOT NULL DEFAULT 0,   -- XP
    `rank`        INT          NOT NULL DEFAULT 0,   -- legacy field
    `kills`       INT          NOT NULL DEFAULT 0,
    `deaths`      INT          NOT NULL DEFAULT 0,
    `headshots`   INT          NOT NULL DEFAULT 0,
    `assists`     INT          NOT NULL DEFAULT 0,
    `round_win`   INT          NOT NULL DEFAULT 0,
    `round_lose`  INT          NOT NULL DEFAULT 0,
    `playtime`    INT          NOT NULL DEFAULT 0,   -- in MINUTES
    `balance`     INT          NOT NULL DEFAULT 0,   -- DEB Coins / server coins
    `lastconnect` INT          NOT NULL DEFAULT 0,   -- unix timestamp
    PRIMARY KEY (`steam`),
    INDEX `idx_balance` (`balance` DESC),
    INDEX `idx_value`   (`value` DESC),
    INDEX `idx_playtime`(`playtime` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- 2. skin_pool — available skins to award
-- ============================================================
-- Admin fills this table via !addskinpool command.
-- Random skin is awarded on quest completion / playtime milestone.
-- ============================================================

CREATE TABLE IF NOT EXISTS `skin_pool` (
    `id`                 INT NOT NULL AUTO_INCREMENT,
    `defindex`           INT NOT NULL,               -- weapon defindex (e.g. 7 = AK-47)
    `paint_id`           INT NOT NULL,               -- paint kit ID
    `wear`               FLOAT NOT NULL DEFAULT 0.0, -- float wear (0.0 - 1.0)
    `skin_name`          VARCHAR(128) NOT NULL,      -- display name
    `total_quantity`     INT NOT NULL DEFAULT 1,     -- original quantity
    `remaining_quantity` INT NOT NULL DEFAULT 1,     -- currently available
    `created_at`         TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_remaining` (`remaining_quantity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- 3. player_skins — awarded skins
-- ============================================================
-- Every awarded skin is logged here.
-- "awarded_for" holds reason: 'quest_daily', 'playtime_1800', 'manual', etc.
-- ============================================================

CREATE TABLE IF NOT EXISTS `player_skins` (
    `id`            INT NOT NULL AUTO_INCREMENT,
    `steamid`       VARCHAR(32) NOT NULL,
    `skin_pool_id`  INT NOT NULL,
    `defindex`      INT NOT NULL,
    `paint_id`      INT NOT NULL,
    `wear`          FLOAT NOT NULL DEFAULT 0.0,
    `skin_name`     VARCHAR(128) NOT NULL,
    `awarded_for`   VARCHAR(64) NOT NULL DEFAULT 'manual',
    `awarded_at`    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_steam`     (`steamid`),
    INDEX `idx_awarded`   (`awarded_for`),
    INDEX `idx_pool`      (`skin_pool_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- 4. player_quests — quest progress
-- ============================================================
-- One row per player per quest.
-- Reset automatically when `last_reset` is older than quest interval.
-- ============================================================

CREATE TABLE IF NOT EXISTS `player_quests` (
    `id`          INT NOT NULL AUTO_INCREMENT,
    `steam`       VARCHAR(32) NOT NULL,           -- STEAM_1:X:Y format
    `quest_type`  VARCHAR(16) NOT NULL,           -- 'daily' | 'weekly' | 'monthly'
    `quest_id`    INT NOT NULL,                   -- quest ID from config
    `progress`    INT NOT NULL DEFAULT 0,
    `completed`   TINYINT(1) NOT NULL DEFAULT 0,
    `last_reset`  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uniq_player_quest` (`steam`, `quest_type`, `quest_id`),
    INDEX `idx_steam` (`steam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- 5. server_status — server dashboard
-- ============================================================
-- Each server updates its own row every 60 seconds.
-- Status 1 = online, 0 = offline.
-- ============================================================

CREATE TABLE IF NOT EXISTS `server_status` (
    `id`              INT NOT NULL,               -- server ID (1, 2, 3...)
    `server_name`     VARCHAR(128) NOT NULL,
    `ip_address`      VARCHAR(64)  NOT NULL,
    `port`            INT NOT NULL,
    `status`          TINYINT(1) NOT NULL DEFAULT 0,
    `current_map`     VARCHAR(64)  NOT NULL DEFAULT 'unknown',
    `current_mode`    VARCHAR(32)  NOT NULL DEFAULT 'PUBLIC',
    `online_players`  INT NOT NULL DEFAULT 0,
    `max_players`     INT NOT NULL DEFAULT 0,
    `last_update`     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- EXAMPLE DATA (optional)
-- ============================================================
-- Uncomment to seed the skin pool with a few starter skins.
-- ============================================================

-- INSERT INTO `skin_pool` (`defindex`, `paint_id`, `wear`, `skin_name`, `total_quantity`, `remaining_quantity`) VALUES
-- (7,  282, 0.15, 'AK-47 | Redline',     10, 10),   -- defindex 7 = AK-47
-- (1,  279, 0.10, 'Desert Eagle | Blaze', 5,  5),   -- defindex 1 = Deagle
-- (9,  344, 0.20, 'AWP | Dragon Lore',    1,  1),   -- defindex 9 = AWP
-- (60, 600, 0.05, 'M4A1-S | Printstream', 5,  5);   -- defindex 60 = M4A1-S

-- ============================================================
-- DONE
-- ============================================================
-- After running this schema, restart the server.
-- Check logs for any errors related to missing tables.
-- ============================================================
