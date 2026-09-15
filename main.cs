using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ReportPlugin
{
    public class ReportPlugin : BasePlugin
    {
        public override string ModuleName => "CS2 Player Report";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "Chmonya";
        public override string ModuleDescription => "Player reports, verification, quests, ranks, economy for CS2 servers";

        private const string TAG = "[Report]";

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly List<string> _reasons = new()
        {
            "Cheats: WallHack / WH", "Cheats: Aimbot", "Cheats: SpinBot / Rage",
            "Cheats: TriggerBot", "Cheats: Anti-Aim",
            "Scripts / Skin changer", "Griefing / Teamkill",
            "AFK / Idle", "Blocking teammates",
            "Info leak from Discord", "Map bug abuse",
            "Insults / Toxicity", "Insulting family",
            "Hate speech / Racism", "Spam / Ads",
            "Voice spam / Music", "Bad mic / Voice flood",
            "Suspicious play / Verification", "Ban evasion / Multi-account",
            "Inadequate behavior", "Other / Custom reason"
        };

        private readonly Dictionary<ulong, CheckData> _checkingPlayers = new();
        private readonly Dictionary<ulong, DateTime> _lastReportTime = new();
        private readonly Dictionary<ulong, PlayerSessionData> _playerSessions = new();
        private Dictionary<string, List<QuestDefinition>> _questDefinitions = new();

        private PluginConfig _config = new();
        private string _logFilePath = "";

        public override void Load(bool hotReload)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            _config = LoadConfig();
            _logFilePath = Path.Combine(ModuleDirectory, "logs.txt");
            InitializeQuestsFromConfig();

            AddCommand("css_report", "Open report menu", (p, c) => RunCommand("css_report", p, c, OnReportCommand));
            AddCommand("css_check", "Mark player for verification (admin)", (p, c) => RunCommand("css_check", p, c, OnCheckCommand));
            AddCommand("css_uncheck", "Remove verification (admin)", (p, c) => RunCommand("css_uncheck", p, c, OnUncheckCommand));
            AddCommand("css_contact", "Send your Discord to admins", (p, c) => RunCommand("css_contact", p, c, OnContactCommand));
            AddCommand("css_admins", "Show online admins", (p, c) => RunCommand("css_admins", p, c, OnAdminsCommand));
            AddCommand("css_reload_config", "Reload config (admin)", (p, c) => RunCommand("css_reload_config", p, c, OnReloadConfigCommand));
            AddCommand("css_balance", "Check coin balance", (p, c) => RunCommand("css_balance", p, c, OnBalanceCommand));
            AddCommand("css_quests", "View active quests", (p, c) => RunCommand("css_quests", p, c, OnQuestsCommand));
            AddCommand("css_help", "Show all commands", (p, c) => RunCommand("css_help", p, c, OnHelpCommand));
            AddCommand("css_addskinpool", "Add skin to pool (admin)", (p, c) => RunCommand("css_addskinpool", p, c, OnAddSkinPoolCommand));
            AddCommand("css_skins", "Show remaining skins in pool", (p, c) => RunCommand("css_skins", p, c, OnSkinsCommand));
            AddCommand("css_rank", "Show level and stats", (p, c) => RunCommand("css_rank", p, c, OnRankCommand));
            AddCommand("css_top", "Top-10 players by balance", (p, c) => RunCommand("css_top", p, c, OnTopCommand));
            AddCommand("css_playtime", "Show playtime", (p, c) => RunCommand("css_playtime", p, c, OnPlaytimeCommand));
            AddCommand("css_stats", "Show K/D, headshots", (p, c) => RunCommand("css_stats", p, c, OnStatsCommand));
            AddCommand("css_setlevel", "Set player level (admin)", (p, c) => RunCommand("css_setlevel", p, c, OnSetLevelCommand));
            AddCommand("css_givecoins", "Give coins to player (admin)", (p, c) => RunCommand("css_givecoins", p, c, OnGiveCoinsCommand));
            AddCommand("css_resetplayer", "Reset player stats (admin)", (p, c) => RunCommand("css_resetplayer", p, c, OnResetPlayerCommand));

            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChange);
            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

            AddTimer(60.0f, async () => { await UpdateServerStatus(); }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
            AddTimer(60.0f, CheckTimedOutPlayers, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
            AddTimer(60.0f, SaveSessionsAndCheckPlaytimeQuests, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
            AddTimer(600.0f, async () => { await CheckPlaytimeRewards(); }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

            _ = UpdateServerStatus();
            LogToFile($"{ModuleName} v{ModuleVersion} loaded.");
        }

        public override void Unload(bool hotReload)
        {
            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();
                var sql = "UPDATE server_status SET status = 0, online_players = 0, last_update = NOW() WHERE id = @id;";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@id", _config.ServerConfig.ServerId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logger.LogError(ex, "Offline status error"); }
        }

        // ==================== LOGGING ====================
        private void LogToFile(string message, string level = "INFO")
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logEntry, Encoding.UTF8);
            }
            catch { }
        }

        private void RunCommand(string commandName, CCSPlayerController? player, CommandInfo command,
            Action<CCSPlayerController?, CommandInfo> handler)
        {
            var playerName = player?.PlayerName ?? "CONSOLE";
            var steamId = player?.SteamID.ToString() ?? "-";
            var args = command.ArgCount > 1
                ? string.Join(" ", Enumerable.Range(1, command.ArgCount - 1).Select(command.GetArg))
                : "-";

            LogToFile($"CMD {commandName} | {playerName} ({steamId}) | args: {args}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                handler(player, command);
                sw.Stop();
                LogToFile($"CMD {commandName} done in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogToFile($"ERROR {commandName} (player {playerName}): {ex}", "ERROR");
                Logger.LogError(ex, $"Error in {commandName}");
                player?.PrintToChat($"{ChatColors.Red}{TAG} Error, check logs.");
            }
        }

        // ==================== CONFIG ====================
        private PluginConfig LoadConfig()
        {
            var configPath = Path.Combine(ModuleDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                var defaultConfig = new PluginConfig();
                File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
                return defaultConfig;
            }
            return JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(configPath))!;
        }

        private string GetConnectionString()
        {
            var db = _config.DatabaseConfig;
            return $"Server={db.DatabaseHost};Port={db.DatabasePort};Database={db.DatabaseName};" +
                   $"User={db.DatabaseUser};Password={db.DatabasePassword};SslMode={db.DatabaseSSlMode};";
        }

        private void InitializeQuestsFromConfig()
        {
            _questDefinitions["daily"] = new List<QuestDefinition> {
                new() { Id = 1, Description = "Get 20 kills", Target = 20, Type = "kills", RewardXp = 100, RewardCoins = 50 },
                new() { Id = 2, Description = "Get 5 headshots", Target = 5, Type = "headshots", RewardXp = 150, RewardCoins = 75 },
                new() { Id = 3, Description = "Play for 60 minutes", Target = 60, Type = "playtime", RewardXp = 200, RewardCoins = 100 }
            };
            _questDefinitions["weekly"] = new List<QuestDefinition> {
                new() { Id = 101, Description = "Get 100 kills", Target = 100, Type = "kills", RewardXp = 500, RewardCoins = 300 },
                new() { Id = 102, Description = "Win 15 rounds", Target = 15, Type = "round_win", RewardXp = 400, RewardCoins = 250 }
            };
            _questDefinitions["monthly"] = new List<QuestDefinition> {
                new() { Id = 201, Description = "Play 30 hours (1800 min)", Target = 1800, Type = "playtime", RewardXp = 2000, RewardCoins = 1500 },
                new() { Id = 202, Description = "Get 500 kills", Target = 500, Type = "kills", RewardXp = 1500, RewardCoins = 1000 }
            };
        }

        // ==================== SKIN POOL ====================
        private async Task<bool> AwardSkinFromPool(ulong steamId, string awardedFor = "manual")
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var selectSql = @"SELECT id, defindex, paint_id, wear, skin_name 
                                  FROM skin_pool 
                                  WHERE remaining_quantity > 0 
                                  ORDER BY RAND() 
                                  LIMIT 1";

                await using var selectCmd = new MySqlCommand(selectSql, connection);
                using var reader = await selectCmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return false;

                var poolId = reader.GetInt32("id");
                var defindex = reader.GetInt32("defindex");
                var paintId = reader.GetInt32("paint_id");
                var wear = reader.GetFloat("wear");
                var skinName = reader.GetString("skin_name");

                reader.Close();

                var updateSql = @"UPDATE skin_pool SET remaining_quantity = remaining_quantity - 1 WHERE id = @id AND remaining_quantity > 0";
                await using var updateCmd = new MySqlCommand(updateSql, connection);
                updateCmd.Parameters.AddWithValue("@id", poolId);
                var affected = await updateCmd.ExecuteNonQueryAsync();

                if (affected == 0) return false;

                var insertSql = @"INSERT INTO player_skins (steamid, skin_pool_id, defindex, paint_id, wear, skin_name, awarded_for)
                                  VALUES (@steamid, @poolId, @defindex, @paintId, @wear, @skinName, @awardedFor)";

                await using var insertCmd = new MySqlCommand(insertSql, connection);
                insertCmd.Parameters.AddWithValue("@steamid", steamId.ToString());
                insertCmd.Parameters.AddWithValue("@poolId", poolId);
                insertCmd.Parameters.AddWithValue("@defindex", defindex);
                insertCmd.Parameters.AddWithValue("@paintId", paintId);
                insertCmd.Parameters.AddWithValue("@wear", wear);
                insertCmd.Parameters.AddWithValue("@skinName", skinName);
                insertCmd.Parameters.AddWithValue("@awardedFor", awardedFor);
                await insertCmd.ExecuteNonQueryAsync();

                var player = Utilities.GetPlayerFromSteamId(steamId);
                if (player != null && player.IsValid)
                {
                    Server.NextFrame(() =>
                    {
                        player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}You received a skin: {ChatColors.Gold}{skinName}");
                    });
                }

                await SendSkinNotificationAsync(steamId.ToString(), skinName, defindex, paintId, wear);
                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"Skin award error: {ex}", "ERROR");
                return false;
            }
        }

        private async Task CheckPlaytimeRewards()
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"SELECT steam, playtime FROM lvl_base 
                            WHERE playtime >= 1800 AND playtime % 1800 = 0
                            AND NOT EXISTS (
                                SELECT 1 FROM player_skins WHERE steamid = steam AND awarded_for = CONCAT('playtime_', playtime)
                            )";

                await using var cmd = new MySqlCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var steamIdStr = reader.GetString("steam");
                    var playtime = reader.GetInt32("playtime");

                    if (!ulong.TryParse(steamIdStr, out var steamId64))
                        continue;

                    var hours = playtime / 60;
                    var awarded = await AwardSkinFromPool(steamId64, $"playtime_{playtime}");

                    if (awarded)
                    {
                        var player = Utilities.GetPlayerFromSteamId(steamId64);
                        if (player != null && player.IsValid)
                        {
                            Server.NextFrame(() =>
                            {
                                player.PrintToChat($"{ChatColors.Gold}{TAG} {ChatColors.Default}Skin for {ChatColors.Green}{hours} hours played!");
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Playtime reward error: {ex}", "ERROR");
            }
        }

        private async Task SendSkinNotificationAsync(string steamId, string skinName, int defindex, int paintId, float wear)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_config.DiscordWebhooks.AdminWebhook)) return;
                if (_config.DiscordWebhooks.AdminWebhook.Contains("YOUR_WEBHOOK")) return;

                var player = Utilities.GetPlayerFromSteamId(ulong.Parse(steamId));
                var playerName = player?.PlayerName ?? "Unknown";

                var payload = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = "🎒 NEW SKIN",
                            color = 0x00FF66,
                            fields = new[]
                            {
                                new { name = "👤 Player", value = $"{playerName}\n`{steamId}`", inline = false },
                                new { name = "🔫 Skin", value = skinName, inline = true },
                                new { name = "🎨 Paint ID", value = paintId.ToString(), inline = true },
                                new { name = "🔧 Wear", value = wear.ToString("F3"), inline = true }
                            },
                            footer = new { text = $"{_config.ServerConfig.ServerName} • {DateTime.Now:dd.MM.yyyy HH:mm}" }
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(_config.DiscordWebhooks.AdminWebhook, content);
            }
            catch (Exception ex)
            {
                LogToFile($"Skin notification error: {ex}", "ERROR");
            }
        }

        private async Task SendSimpleWebhookAsync(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                LogToFile("Webhook URL not set.", "WARN");
                return;
            }
            if (webhookUrl.Contains("YOUR_WEBHOOK")) return;

            try
            {
                var payload = new { content = message };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await _httpClient.PostAsync(webhookUrl, content);
                if (!resp.IsSuccessStatusCode)
                    LogToFile($"Webhook code {(int)resp.StatusCode}", "WARN");
            }
            catch (Exception ex)
            {
                LogToFile($"Webhook error: {ex.Message}", "ERROR");
            }
        }

        // ==================== COMMANDS ====================
        private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            player.PrintToChat($"{ChatColors.Gold}=== {ModuleName} — commands ===");
            player.PrintToChat($"{ChatColors.LightBlue}!report {ChatColors.Default}— file a report");
            player.PrintToChat($"{ChatColors.LightBlue}!balance {ChatColors.Default}— coin balance");
            player.PrintToChat($"{ChatColors.LightBlue}!quests {ChatColors.Default}— active quests");
            player.PrintToChat($"{ChatColors.LightBlue}!contact <discord> {ChatColors.Default}— leave contact");
            player.PrintToChat($"{ChatColors.LightBlue}!admins {ChatColors.Default}— admins online");
            player.PrintToChat($"{ChatColors.LightBlue}!rank {ChatColors.Default}— level and XP");
            player.PrintToChat($"{ChatColors.LightBlue}!top {ChatColors.Default}— top-10 by balance");
            player.PrintToChat($"{ChatColors.LightBlue}!playtime {ChatColors.Default}— playtime");
            player.PrintToChat($"{ChatColors.LightBlue}!stats {ChatColors.Default}— K/D, headshots");
            if (HasAdminAccess(player))
            {
                player.PrintToChat($"{ChatColors.Yellow}!check <player> {ChatColors.Default}— mark for verification");
                player.PrintToChat($"{ChatColors.Yellow}!uncheck <player> {ChatColors.Default}— remove verification");
                player.PrintToChat($"{ChatColors.Yellow}!skins {ChatColors.Default}— skin pool list");
                player.PrintToChat($"{ChatColors.Yellow}!setlevel <player> <level> {ChatColors.Default}— set level");
                player.PrintToChat($"{ChatColors.Yellow}!givecoins <player> <amount> {ChatColors.Default}— give coins");
                player.PrintToChat($"{ChatColors.Yellow}!resetplayer <player> {ChatColors.Default}— reset stats");
                player.PrintToChat($"{ChatColors.Yellow}!reload_config {ChatColors.Default}— reload config");
            }
        }

        private void OnReloadConfigCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !HasAdminAccess(player))
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            _config = LoadConfig();
            LogToFile($"Config reloaded ({(player != null ? player.PlayerName : "console")}).");
            command.ReplyToCommand($"{ChatColors.Green}{TAG} Config reloaded.");
        }

        private void OnAdminsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            var onlineAdmins = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsBot && HasAdminAccess(p))
                .Select(p => p.PlayerName)
                .ToList();

            if (onlineAdmins.Count == 0)
            {
                player.PrintToChat($"{ChatColors.Yellow}{TAG} {ChatColors.Default}No admins online.");
                return;
            }

            player.PrintToChat($"{ChatColors.Gold}=== Admins online ({onlineAdmins.Count}) ===");
            foreach (var name in onlineAdmins)
                player.PrintToChat($"{ChatColors.LightBlue}• {name}");
        }

        private void OnContactCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            if (command.ArgCount < 2)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !contact YourDiscord");
                return;
            }

            var discordTag = command.GetArg(1);
            LogToFile($"Contact {player.PlayerName} ({player.SteamID}): {discordTag}");

            _ = SendSimpleWebhookAsync(_config.DiscordWebhooks.AdminWebhook,
                $"📇 **{player.PlayerName}** (`{player.SteamID}`) Discord: `{discordTag}`");

            player.PrintToChat($"{ChatColors.Green}{TAG} Contact sent.");
        }

        private void OnCheckCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            if (command.ArgCount < 2)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !check <name_or_steamid>");
                return;
            }

            var query = command.GetArg(1);
            var target = Utilities.GetPlayers().FirstOrDefault(p =>
                p.IsValid && !p.IsBot &&
                (p.PlayerName.Contains(query, StringComparison.OrdinalIgnoreCase) || p.SteamID.ToString() == query));

            if (target == null)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Player \"{query}\" not found.");
                return;
            }

            _checkingPlayers[target.SteamID] = new CheckData { StartTime = DateTime.Now, AdminSteamID = player.SteamID };
            LogToFile($"Admin {player.PlayerName} marked {target.PlayerName} for verification");

            player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}Player {ChatColors.Gold}{target.PlayerName} {ChatColors.Default}is under verification.");
        }

        private void OnUncheckCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            if (command.ArgCount < 2)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !uncheck <name_or_steamid>");
                return;
            }

            var query = command.GetArg(1);
            var target = Utilities.GetPlayers().FirstOrDefault(p =>
                p.IsValid && !p.IsBot &&
                (p.PlayerName.Contains(query, StringComparison.OrdinalIgnoreCase) || p.SteamID.ToString() == query));

            if (target == null || !_checkingPlayers.Remove(target.SteamID))
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Player is not under verification.");
                return;
            }

            LogToFile($"Admin {player.PlayerName} removed {target.PlayerName} from verification");
            player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}Player {ChatColors.Gold}{target.PlayerName} {ChatColors.Default}removed from verification.");
        }

        private void OnReportCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            if (_lastReportTime.TryGetValue(player.SteamID, out var last))
            {
                var secondsLeft = _config.GameplayConfig.ReportCooldownSeconds - (DateTime.Now - last).TotalSeconds;
                if (secondsLeft > 0)
                {
                    player.PrintToChat($"{ChatColors.Red}{TAG} Wait {(int)secondsLeft} sec.");
                    return;
                }
            }

            var targets = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.SteamID != player.SteamID).ToList();
            if (targets.Count == 0)
            {
                player.PrintToChat($"{ChatColors.Yellow}{TAG} No one to report.");
                return;
            }

            var targetMenu = new ChatMenu("Who to report?");
            foreach (var t in targets)
                targetMenu.AddMenuOption(t.PlayerName, (p, option) => ShowReasonMenu(p, t));

            MenuManager.OpenChatMenu(player, targetMenu);
        }

        private void ShowReasonMenu(CCSPlayerController player, CCSPlayerController target)
        {
            var reasonMenu = new ChatMenu($"Reason ({target.PlayerName})");
            foreach (var reason in _reasons)
                reasonMenu.AddMenuOption(reason, (p, option) => SubmitReport(p, target, reason));

            MenuManager.OpenChatMenu(player, reasonMenu);
        }

        private void SubmitReport(CCSPlayerController reporter, CCSPlayerController target, string reason)
        {
            _lastReportTime[reporter.SteamID] = DateTime.Now;
            LogToFile($"REPORT: {reporter.PlayerName} → {target.PlayerName} | {reason}");

            reporter.PrintToChat($"{ChatColors.Green}{TAG} Report submitted.");

            _ = SendSimpleWebhookAsync(_config.DiscordWebhooks.ReportWebhook,
                $"🚨 **Report**\nFrom: {reporter.PlayerName} (`{reporter.SteamID}`)\nOn: {target.PlayerName} (`{target.SteamID}`)\nReason: {reason}");
        }

        private void OnBalanceCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            _ = ShowBalanceAsync(player);
        }

        private async Task ShowBalanceAsync(CCSPlayerController player)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = "SELECT balance, value, playtime FROM lvl_base WHERE steam = @steam LIMIT 1";
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@steam", player.SteamID.ToString());
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Server.NextFrame(() => player.PrintToChat(
                        $"{ChatColors.Yellow}{TAG} {ChatColors.Default}No data — play a bit."));
                    return;
                }

                var balance = reader.GetInt32("balance");
                var xp = reader.GetInt32("value");
                var playtime = reader.GetInt32("playtime");

                Server.NextFrame(() =>
                {
                    player.PrintToChat($"{ChatColors.Gold}{TAG} {ChatColors.Default}Balance: {ChatColors.Green}{balance} coins");
                    player.PrintToChat($"{ChatColors.Gold}{TAG} {ChatColors.Default}XP: {ChatColors.LightBlue}{xp} {ChatColors.Default}| Playtime: {playtime / 60}h {playtime % 60}m");
                });
            }
            catch (Exception ex)
            {
                LogToFile($"Balance error {player.SteamID}: {ex}", "ERROR");
                Server.NextFrame(() => player.PrintToChat($"{ChatColors.Red}{TAG} Failed to load balance."));
            }
        }

        private void OnQuestsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            if (!_playerSessions.TryGetValue(player.SteamID, out var session))
            {
                session = new PlayerSessionData { SteamId = player.SteamID };
                _playerSessions[player.SteamID] = session;
            }

            player.PrintToChat($"{ChatColors.Gold}=== Daily quests ===");
            PrintQuestProgress(player, "daily", session);
            player.PrintToChat($"{ChatColors.Gold}=== Weekly quests ===");
            PrintQuestProgress(player, "weekly", session);
        }

        private void PrintQuestProgress(CCSPlayerController player, string category, PlayerSessionData session)
        {
            if (!_questDefinitions.TryGetValue(category, out var quests)) return;
            foreach (var q in quests)
            {
                int progress = q.Type switch
                {
                    "kills" => session.Kills,
                    "headshots" => session.Headshots,
                    "round_win" => session.RoundWins,
                    "playtime" => session.PlaytimeMinutes,
                    _ => 0
                };
                var status = progress >= q.Target ? $"{ChatColors.Green}[Done]" : $"{ChatColors.LightBlue}[{progress}/{q.Target}]";
                player.PrintToChat($"{status} {ChatColors.Default}{q.Description} {ChatColors.Gold}(+{q.RewardXp} XP, +{q.RewardCoins} coins)");
            }
        }

        private void OnAddSkinPoolCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }

            if (command.ArgCount < 6)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !addskinpool <defindex> <paintId> <wear> <quantity> <skin_name>");
                return;
            }

            if (!int.TryParse(command.GetArg(1), out int defindex) ||
                !int.TryParse(command.GetArg(2), out int paintId) ||
                !float.TryParse(command.GetArg(3), out float wear) ||
                !int.TryParse(command.GetArg(4), out int quantity))
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Invalid parameters.");
                return;
            }

            var skinName = command.GetArg(5);

            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();

                var sql = @"INSERT INTO skin_pool (defindex, paint_id, wear, skin_name, total_quantity, remaining_quantity)
                            VALUES (@defindex, @paintId, @wear, @skinName, @quantity, @quantity)";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@defindex", defindex);
                cmd.Parameters.AddWithValue("@paintId", paintId);
                cmd.Parameters.AddWithValue("@wear", wear);
                cmd.Parameters.AddWithValue("@skinName", skinName);
                cmd.Parameters.AddWithValue("@quantity", quantity);
                cmd.ExecuteNonQuery();

                LogToFile($"Admin {player.PlayerName} added {skinName} x{quantity}");
                player.PrintToChat($"{ChatColors.Green}{TAG} Skin {ChatColors.Gold}{skinName} {ChatColors.Default}added ({quantity} pcs.)");
            }
            catch (Exception ex)
            {
                LogToFile($"Skin add error: {ex}", "ERROR");
                player.PrintToChat($"{ChatColors.Red}{TAG} Error.");
            }
        }

        private void OnSkinsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();

                var sql = @"SELECT id, skin_name, remaining_quantity FROM skin_pool WHERE remaining_quantity > 0 ORDER BY id";
                using var cmd = new MySqlCommand(sql, connection);
                using var reader = cmd.ExecuteReader();

                if (!reader.HasRows)
                {
                    player.PrintToChat($"{ChatColors.Yellow}{TAG} Skin pool is empty.");
                    return;
                }

                player.PrintToChat($"{ChatColors.Gold}=== Available skins ===");
                while (reader.Read())
                {
                    var name = reader.GetString("skin_name");
                    var remaining = reader.GetInt32("remaining_quantity");
                    player.PrintToChat($"{ChatColors.LightBlue}{name} {ChatColors.Default}— {ChatColors.Green}{remaining} pcs.");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Skin list error: {ex}", "ERROR");
                player.PrintToChat($"{ChatColors.Red}{TAG} Error.");
            }
        }

        private void OnRankCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            _ = ShowRankAsync(player);
        }

        private async Task ShowRankAsync(CCSPlayerController player)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"SELECT value, balance, playtime, 
                                   (SELECT COUNT(*) + 1 FROM lvl_base WHERE value > t.value) as rank
                            FROM lvl_base t
                            WHERE steam = @steam LIMIT 1";

                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@steam", player.SteamID.ToString());
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Server.NextFrame(() => player.PrintToChat(
                        $"{ChatColors.Yellow}{TAG} No data — play a bit."));
                    return;
                }

                var xp = reader.GetInt32("value");
                var balance = reader.GetInt32("balance");
                var playtime = reader.GetInt32("playtime");
                var rank = reader.GetInt32("rank");
                var level = (xp / _config.GameplayConfig.XpPerLevel) + 1;

                Server.NextFrame(() =>
                {
                    player.PrintToChat($"{ChatColors.Gold}=== Your profile ===");
                    player.PrintToChat($"{ChatColors.LightBlue}Level: {ChatColors.Default}{level}");
                    player.PrintToChat($"{ChatColors.LightBlue}XP: {ChatColors.Default}{xp}");
                    player.PrintToChat($"{ChatColors.LightBlue}Balance: {ChatColors.Default}{balance} coins");
                    player.PrintToChat($"{ChatColors.LightBlue}Playtime: {ChatColors.Default}{playtime / 60} h");
                    player.PrintToChat($"{ChatColors.LightBlue}Rank: {ChatColors.Default}#{rank}");
                });
            }
            catch (Exception ex)
            {
                LogToFile($"Rank error {player.SteamID}: {ex}", "ERROR");
            }
        }

        private void OnTopCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            _ = ShowTopAsync(player);
        }

        private async Task ShowTopAsync(CCSPlayerController player)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"SELECT steam, name, balance, value, playtime FROM lvl_base 
                            ORDER BY balance DESC LIMIT 10";

                await using var cmd = new MySqlCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                if (!reader.HasRows)
                {
                    Server.NextFrame(() => player.PrintToChat($"{ChatColors.Yellow}{TAG} No players in top."));
                    return;
                }

                Server.NextFrame(() => player.PrintToChat($"{ChatColors.Gold}=== TOP-10 BY BALANCE ==="));
                int pos = 1;
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString("name");
                    var balance = reader.GetInt32("balance");
                    var xp = reader.GetInt32("value");
                    var playtime = reader.GetInt32("playtime");
                    player.PrintToChat($"{ChatColors.LightBlue}{pos}. {ChatColors.Default}{name} — {ChatColors.Green}{balance} coins {ChatColors.LightBlue}({xp} XP, {playtime / 60} h)");
                    pos++;
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Top error: {ex}", "ERROR");
            }
        }

        private void OnPlaytimeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            _ = ShowPlaytimeAsync(player);
        }

        private async Task ShowPlaytimeAsync(CCSPlayerController player)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = "SELECT playtime FROM lvl_base WHERE steam = @steam LIMIT 1";
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@steam", player.SteamID.ToString());
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Server.NextFrame(() => player.PrintToChat($"{ChatColors.Yellow}{TAG} No data."));
                    return;
                }

                var playtime = reader.GetInt32("playtime");
                Server.NextFrame(() => player.PrintToChat($"{ChatColors.Gold}{TAG} {ChatColors.Default}Playtime: {ChatColors.LightBlue}{playtime / 60}h {playtime % 60}m"));
            }
            catch (Exception ex)
            {
                LogToFile($"Playtime error {player.SteamID}: {ex}", "ERROR");
            }
        }

        private void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            _ = ShowStatsAsync(player);
        }

        private async Task ShowStatsAsync(CCSPlayerController player)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"SELECT kills, deaths, headshots, round_win, round_lose FROM lvl_base WHERE steam = @steam LIMIT 1";
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@steam", player.SteamID.ToString());
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Server.NextFrame(() => player.PrintToChat($"{ChatColors.Yellow}{TAG} No data."));
                    return;
                }

                var kills = reader.GetInt32("kills");
                var deaths = reader.GetInt32("deaths");
                var headshots = reader.GetInt32("headshots");
                var wins = reader.GetInt32("round_win");
                var losses = reader.GetInt32("round_lose");
                var kd = deaths > 0 ? (float)kills / deaths : kills;
                var hsPct = kills > 0 ? (float)headshots / kills * 100 : 0;
                var winPct = (wins + losses) > 0 ? (float)wins / (wins + losses) * 100 : 0;

                Server.NextFrame(() =>
                {
                    player.PrintToChat($"{ChatColors.Gold}=== Stats ===");
                    player.PrintToChat($"{ChatColors.LightBlue}Kills: {ChatColors.Default}{kills}");
                    player.PrintToChat($"{ChatColors.LightBlue}Deaths: {ChatColors.Default}{deaths}");
                    player.PrintToChat($"{ChatColors.LightBlue}K/D: {ChatColors.Default}{kd:F2}");
                    player.PrintToChat($"{ChatColors.LightBlue}Headshots: {ChatColors.Default}{headshots} ({hsPct:F1}%)");
                    player.PrintToChat($"{ChatColors.LightBlue}Wins/Losses: {ChatColors.Default}{wins}/{losses} ({winPct:F1}%)");
                });
            }
            catch (Exception ex)
            {
                LogToFile($"Stats error {player.SteamID}: {ex}", "ERROR");
            }
        }

        private void OnSetLevelCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            if (command.ArgCount < 3)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !setlevel <player> <level>");
                return;
            }

            var targetName = command.GetArg(1);
            var target = Utilities.GetPlayers().FirstOrDefault(p =>
                p.IsValid && !p.IsBot && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Player not found.");
                return;
            }

            if (!int.TryParse(command.GetArg(2), out int level) || level < 1)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Invalid level.");
                return;
            }

            var xp = (level - 1) * _config.GameplayConfig.XpPerLevel;

            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();

                var sql = "UPDATE lvl_base SET value = @xp WHERE steam = @steam";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@xp", xp);
                cmd.Parameters.AddWithValue("@steam", target.SteamID.ToString());
                cmd.ExecuteNonQuery();

                LogToFile($"Admin {player.PlayerName} set {target.PlayerName} level {level}");
                player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}Level {ChatColors.Gold}{target.PlayerName} {ChatColors.Default}set to {ChatColors.Green}{level}");
            }
            catch (Exception ex)
            {
                LogToFile($"Setlevel error: {ex}", "ERROR");
                player.PrintToChat($"{ChatColors.Red}{TAG} Error.");
            }
        }

        private void OnGiveCoinsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            if (command.ArgCount < 3)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !givecoins <player> <amount>");
                return;
            }

            var targetName = command.GetArg(1);
            var target = Utilities.GetPlayers().FirstOrDefault(p =>
                p.IsValid && !p.IsBot && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Player not found.");
                return;
            }

            if (!int.TryParse(command.GetArg(2), out int amount) || amount <= 0)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Invalid amount.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();

                var sql = "UPDATE lvl_base SET balance = balance + @amount WHERE steam = @steam";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@amount", amount);
                cmd.Parameters.AddWithValue("@steam", target.SteamID.ToString());
                cmd.ExecuteNonQuery();

                LogToFile($"Admin {player.PlayerName} gave {target.PlayerName} {amount} coins");
                player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}Gave {ChatColors.Gold}{amount} coins {ChatColors.Default}to {ChatColors.LightBlue}{target.PlayerName}");
            }
            catch (Exception ex)
            {
                LogToFile($"Givecoins error: {ex}", "ERROR");
                player.PrintToChat($"{ChatColors.Red}{TAG} Error.");
            }
        }

        private void OnResetPlayerCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !HasAdminAccess(player))
            {
                player?.PrintToChat($"{ChatColors.Red}{TAG} Access denied.");
                return;
            }
            if (command.ArgCount < 2)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Usage: !resetplayer <player>");
                return;
            }

            var targetName = command.GetArg(1);
            var target = Utilities.GetPlayers().FirstOrDefault(p =>
                p.IsValid && !p.IsBot && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} Player not found.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(GetConnectionString());
                connection.Open();

                var sql = @"UPDATE lvl_base SET 
                            value = 0, kills = 0, deaths = 0, headshots = 0, 
                            round_win = 0, round_lose = 0, playtime = 0, balance = 0 
                            WHERE steam = @steam";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@steam", target.SteamID.ToString());
                cmd.ExecuteNonQuery();

                LogToFile($"Admin {player.PlayerName} reset stats for {target.PlayerName}");
                player.PrintToChat($"{ChatColors.Green}{TAG} {ChatColors.Default}Stats of {ChatColors.Gold}{target.PlayerName} {ChatColors.Default}reset.");
            }
            catch (Exception ex)
            {
                LogToFile($"Resetplayer error: {ex}", "ERROR");
                player.PrintToChat($"{ChatColors.Red}{TAG} Error.");
            }
        }

        // ==================== UTILS ====================
        private bool HasAdminAccess(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return false;
            return AdminManager.PlayerHasPermissions(player, "@css/generic")
                || AdminManager.PlayerHasPermissions(player, "@css/root");
        }

        private string SteamId64ToSteam2(string steamId64)
        {
            if (!ulong.TryParse(steamId64, out var id64)) return "STEAM_0:0:0";
            const ulong steamIdBase = 76561197960265728UL;
            if (id64 < steamIdBase) return "STEAM_0:0:0";
            var accountId = id64 - steamIdBase;
            return $"STEAM_1:{accountId % 2}:{accountId / 2}";
        }

        // ==================== VERIFICATION TIMEOUT ====================
        private void CheckTimedOutPlayers()
        {
            var now = DateTime.Now;
            var timeoutMinutes = _config.GameplayConfig.CheckTimeoutMinutes;
            var expired = _checkingPlayers
                .Where(kv => (now - kv.Value.StartTime).TotalMinutes >= timeoutMinutes)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var steamId in expired)
            {
                _checkingPlayers.Remove(steamId);
                LogToFile($"Verification timeout for {steamId} ({timeoutMinutes} min).");
            }
        }

        // ==================== SESSIONS ====================
        private void SaveSessionsAndCheckPlaytimeQuests()
        {
            foreach (var session in _playerSessions.Values)
                session.PlaytimeMinutes += 1;
        }

        // ==================== EVENTS ====================
        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            _ = UpdateServerStatus();
            var player = @event.Userid;
            if (player != null && player.IsValid && !player.IsBot)
            {
                _playerSessions[player.SteamID] = new PlayerSessionData { SteamId = player.SteamID };
                LogToFile($"Connected: {player.PlayerName} ({player.SteamID})");
            }
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && player.IsValid)
            {
                LogToFile($"Disconnected: {player.PlayerName} ({player.SteamID})");
                _playerSessions.Remove(player.SteamID);
                _checkingPlayers.Remove(player.SteamID);
            }
            return HookResult.Continue;
        }

        private HookResult OnPlayerTeamChange(EventPlayerTeam @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && player.IsValid && player.SteamID > 0 && _checkingPlayers.ContainsKey(player.SteamID))
            {
                player.PrintToChat($"{ChatColors.Red}{TAG} You are under verification, team change blocked.");
                return HookResult.Handled;
            }
            return HookResult.Continue;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;

            if (victim != null && victim.IsValid && _playerSessions.TryGetValue(victim.SteamID, out var victimSession))
            {
                victimSession.Deaths++;
                victimSession.CoinsEarnedThisSession -= _config.EconomyConfig.CoinsLostPerDeath;
            }

            if (attacker != null && attacker.IsValid && attacker.SteamID != victim?.SteamID &&
                _playerSessions.TryGetValue(attacker.SteamID, out var attackerSession))
            {
                attackerSession.Kills++;
                attackerSession.CoinsEarnedThisSession += _config.EconomyConfig.CoinsPerKill;

                if (@event.Headshot)
                {
                    attackerSession.Headshots++;
                    attackerSession.CoinsEarnedThisSession += _config.EconomyConfig.CoinsPerHeadshot;
                }

                _ = UpdateQuestProgressAsync(attacker.SteamID, "kills", 1);
                if (@event.Headshot)
                    _ = UpdateQuestProgressAsync(attacker.SteamID, "headshots", 1);
            }
            return HookResult.Continue;
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            var winnerTeam = (CsTeam)@event.Winner;
            if (winnerTeam == CsTeam.Spectator || winnerTeam == CsTeam.None) return HookResult.Continue;

            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
            {
                if (!_playerSessions.TryGetValue(player.SteamID, out var session)) continue;

                if (player.Team == winnerTeam)
                {
                    session.RoundWins++;
                    session.CoinsEarnedThisSession += _config.EconomyConfig.CoinsPerRoundWin;
                    _ = UpdateQuestProgressAsync(player.SteamID, "round_win", 1);
                }
                else if (player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist)
                {
                    session.RoundLosses++;
                    session.CoinsEarnedThisSession -= _config.EconomyConfig.CoinsLostPerRoundLoss;
                }
            }
            return HookResult.Continue;
        }

        private async Task UpdateQuestProgressAsync(ulong steamId, string statType, int amount)
        {
            try
            {
                await using var connection = new MySqlConnection(GetConnectionString());
                await connection.OpenAsync();

                foreach (var kvp in _questDefinitions)
                {
                    string qType = kvp.Key;
                    foreach (var quest in kvp.Value)
                    {
                        if (quest.Type != statType) continue;

                        var resetInterval = qType switch { "daily" => 1, "weekly" => 7, "monthly" => 30, _ => 1 };

                        var sqlCheck = @"SELECT progress, completed, last_reset FROM player_quests 
                                         WHERE steam = @steam AND quest_type = @qType AND quest_id = @qId";

                        int progress = 0; bool completed = false; DateTime lastReset = DateTime.Now;
                        await using (var cmdCheck = new MySqlCommand(sqlCheck, connection))
                        {
                            cmdCheck.Parameters.AddWithValue("@steam", SteamId64ToSteam2(steamId.ToString()));
                            cmdCheck.Parameters.AddWithValue("@qType", qType);
                            cmdCheck.Parameters.AddWithValue("@qId", quest.Id);
                            using var reader = await cmdCheck.ExecuteReaderAsync();
                            if (await reader.ReadAsync())
                            {
                                progress = reader.GetInt32("progress");
                                completed = reader.GetBoolean("completed");
                                lastReset = reader.GetDateTime("last_reset");
                            }
                        }

                        if ((DateTime.Now - lastReset).TotalDays >= resetInterval)
                        {
                            progress = 0; completed = false;
                            await ResetQuestAsync(connection, steamId, qType, quest.Id);
                        }

                        if (completed) continue;

                        progress += amount;
                        if (progress >= quest.Target)
                        {
                            progress = quest.Target; completed = true;
                            await RewardQuestAsync(connection, steamId, quest, qType);

                            var player = Utilities.GetPlayerFromSteamId(steamId);
                            if (player != null && player.IsValid)
                            {
                                Server.NextFrame(() =>
                                {
                                    player.PrintToChat($"{ChatColors.Green}{TAG} Quest completed: {ChatColors.Gold}{quest.Description}");
                                    player.PrintToChat($"{ChatColors.LightBlue}+{quest.RewardXp} XP | +{quest.RewardCoins} coins");
                                });
                            }
                        }

                        var sqlUpdate = @"INSERT INTO player_quests (steam, quest_type, quest_id, progress, completed, last_reset)
                                          VALUES (@steam, @qType, @qId, @progress, @completed, NOW())
                                          ON DUPLICATE KEY UPDATE progress = VALUES(progress), completed = VALUES(completed), last_reset = VALUES(last_reset);";

                        await using var cmdUpdate = new MySqlCommand(sqlUpdate, connection);
                        cmdUpdate.Parameters.AddWithValue("@steam", SteamId64ToSteam2(steamId.ToString()));
                        cmdUpdate.Parameters.AddWithValue("@qType", qType);
                        cmdUpdate.Parameters.AddWithValue("@qId", quest.Id);
                        cmdUpdate.Parameters.AddWithValue("@progress", progress);
                        cmdUpdate.Parameters.AddWithValue("@completed", completed);
                        await cmdUpdate.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Quest error: {ex}", "ERROR");
            }
        }

        private async Task ResetQuestAsync(MySqlConnection conn, ulong steamId, string qType, int qId)
        {
            var sql = @"INSERT INTO player_quests (steam, quest_type, quest_id, progress, completed, last_reset)
                        VALUES (@steam, @qType, @qId, 0, 0, NOW()) 
                        ON DUPLICATE KEY UPDATE progress = 0, completed = 0, last_reset = NOW();";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@steam", SteamId64ToSteam2(steamId.ToString()));
            cmd.Parameters.AddWithValue("@qType", qType);
            cmd.Parameters.AddWithValue("@qId", qId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task RewardQuestAsync(MySqlConnection conn, ulong steamId, QuestDefinition quest, string qType)
        {
            var sql = @"UPDATE lvl_base SET value = value + @xp, balance = balance + @coins WHERE steam = @steam;";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@steam", SteamId64ToSteam2(steamId.ToString()));
            cmd.Parameters.AddWithValue("@xp", quest.RewardXp);
            cmd.Parameters.AddWithValue("@coins", quest.RewardCoins);
            await cmd.ExecuteNonQueryAsync();

            var skinAwarded = await AwardSkinFromPool(steamId, $"quest_{qType}");
            if (!skinAwarded)
            {
                var player = Utilities.GetPlayerFromSteamId(steamId);
                if (player != null && player.IsValid)
                {
                    Server.NextFrame(() =>
                    {
                        player.PrintToChat($"{ChatColors.Yellow}{TAG} No skins in pool, but quest rewards granted.");
                    });
                }
            }
        }

        // ==================== SERVER STATUS ====================
        private async Task UpdateServerStatus()
        {
            try
            {
                if (Server.MapName == null || Server.MapName == "") return;

                Server.NextFrame(() =>
                {
                    try
                    {
                        var players = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot);

                        using var connection = new MySqlConnection(GetConnectionString());
                        connection.Open();

                        var sql = @"INSERT INTO server_status (id, server_name, ip_address, port, status, current_map, current_mode, online_players, max_players, last_update)
                                    VALUES (@id, @name, @ip, @port, 1, @map, @mode, @players, @max, NOW())
                                    ON DUPLICATE KEY UPDATE server_name = VALUES(server_name), ip_address = VALUES(ip_address), port = VALUES(port),
                                    status = VALUES(status), current_map = VALUES(current_map), current_mode = VALUES(current_mode),
                                    online_players = VALUES(online_players), max_players = VALUES(max_players), last_update = NOW();";

                        using var cmd = new MySqlCommand(sql, connection);
                        cmd.Parameters.AddWithValue("@id", _config.ServerConfig.ServerId);
                        cmd.Parameters.AddWithValue("@name", _config.ServerConfig.ServerName);
                        cmd.Parameters.AddWithValue("@ip", _config.ServerConfig.ServerIP);
                        cmd.Parameters.AddWithValue("@port", _config.ServerConfig.ServerPort);
                        cmd.Parameters.AddWithValue("@map", Server.MapName ?? "unknown");
                        cmd.Parameters.AddWithValue("@mode", _config.ServerConfig.GameMode);
                        cmd.Parameters.AddWithValue("@players", players);
                        cmd.Parameters.AddWithValue("@max", Server.MaxPlayers);
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Server status error");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "UpdateServerStatus error");
            }
        }
    }

    // ==================== MODELS ====================
    public class CheckData { public DateTime StartTime { get; set; } public ulong AdminSteamID { get; set; } }
    public class PlayerSessionData { public ulong SteamId { get; set; } public int Kills { get; set; } public int Deaths { get; set; } public int Headshots { get; set; } public int RoundWins { get; set; } public int RoundLosses { get; set; } public int PlaytimeMinutes { get; set; } public int CoinsEarnedThisSession { get; set; } public int XpEarnedThisSession { get; set; } }
    public class QuestDefinition { public int Id { get; set; } public string Description { get; set; } = ""; public int Target { get; set; } public string Type { get; set; } = ""; public int RewardXp { get; set; } public int RewardCoins { get; set; } }

    public class PluginConfig
    {
        public DatabaseConfigData DatabaseConfig { get; set; } = new();
        public ServerConfigData ServerConfig { get; set; } = new();
        public DiscordWebhooksData DiscordWebhooks { get; set; } = new();
        public GameplayConfigData GameplayConfig { get; set; } = new();
        public EconomyConfigData EconomyConfig { get; set; } = new();
    }

    public class DatabaseConfigData
    {
        public string DatabaseHost { get; set; } = "127.0.0.1";
        public int DatabasePort { get; set; } = 3306;
        public string DatabaseUser { get; set; } = "root";
        public string DatabasePassword { get; set; } = "";
        public string DatabaseName { get; set; } = "cs2_server";
        public string DatabaseSSlMode { get; set; } = "preferred";
    }

    public class ServerConfigData
    {
        public int ServerId { get; set; } = 1;
        public string ServerName { get; set; } = "My CS2 Server #1";
        public string ServerIP { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 27015;
        public string GameMode { get; set; } = "PUBLIC";
    }

    public class DiscordWebhooksData
    {
        public string ReportWebhook { get; set; } = "";
        public string AdminWebhook { get; set; } = "";
    }

    public class GameplayConfigData
    {
        public int XpPerLevel { get; set; } = 500;
        public int MaxLevel { get; set; } = 35;
        public int ReportCooldownSeconds { get; set; } = 60;
        public int AutoBanThreshold { get; set; } = 3;
        public int AutoBanMinutes { get; set; } = 60;
        public int CheckTimeoutMinutes { get; set; } = 10;
    }

    public class EconomyConfigData
    {
        public int CoinsPerKill { get; set; } = 15;
        public int CoinsPerHeadshot { get; set; } = 10;
        public int CoinsPerRoundWin { get; set; } = 50;
        public int CoinsLostPerDeath { get; set; } = 2;
        public int CoinsLostPerRoundLoss { get; set; } = 5;
        public int CoinsPerMinutePlayed { get; set; } = 2;
        public int XpPerMinutePlayed { get; set; } = 5;
    }
}
