using System.Text;
using CounterStrikeSharp.API;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Entities;
using System.Collections.Generic;
using CS2_SimpleAdmin.Database;

namespace CS2_SimpleAdmin;

public class PermissionManager
{
    private readonly Database.Database _database;
    public static readonly ConcurrentDictionary<SteamID, DateTime?> AdminCache = new();

    public PermissionManager(Database.Database database)
    {
        _database = database;
    }

    private async Task<List<(string, string, List<string>, int, string)>> GetAllPlayersFlags()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            await using var connection = await _database.GetConnectionAsync();

            const string sql = @"
        SELECT sb_admins.authid AS player_steamid, 
               sb_admins.user AS player_name, 
               sb_srvgroups.flags AS flags, 
               sb_admins.immunity,
               sb_srvgroups.name AS group_name
        FROM sb_admins
        JOIN sb_srvgroups ON sb_admins.srv_group = sb_srvgroups.name
        WHERE sb_admins.immunity IS NOT NULL";

            CS2_SimpleAdmin._logger?.LogInformation($"Executing SQL: {sql}");
            var activeFlags = (await connection.QueryAsync<dynamic>(sql)).ToList();

            var filteredFlagsWithImmunity = new List<(string, string, List<string>, int, string)>();

            foreach (var flagInfo in activeFlags)
            {
                var steamId = Helper.ToSteam64((string)flagInfo.player_steamid);
                var playerName = (string)flagInfo.player_name;
                var flags = ((string)flagInfo.flags).ToCharArray()
                    .Select(c => MapFlagToCssRole(c))
                    .Where(role => role != null)
                    .ToList();
                var immunityValue = (int)flagInfo.immunity;
                var groupName = (string)flagInfo.group_name;

                filteredFlagsWithImmunity.Add((steamId, playerName, flags!, immunityValue, groupName));
            }

            CS2_SimpleAdmin._logger?.LogInformation($"Retrieved {filteredFlagsWithImmunity.Count} records.");
            return filteredFlagsWithImmunity;
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(ex.ToString());
            return new List<(string, string, List<string>, int, string)>();
        }
    }

    private string? MapFlagToCssRole(char flag)
    {
        return flag switch
        {
            'a' => "@css/vip",
            'b' => "@css/generic",
            'c' => "@css/kick",
            'd' => "@css/permban",
            'f' => "@css/slay",
            'g' => "@css/changemap",
            'h' => "@css/cvar",
            'j' => "@css/chat",
            'm' => "@css/rcon",
            'z' => "@css/root",
            _ => null
        };
    }

    private async Task<Dictionary<string, (List<string>, int)>> GetAllGroupsData()
    {
        await using MySqlConnection connection = await _database.GetConnectionAsync();
        try
        {
            var sql = """
          SELECT sg.id AS group_id, sg.name AS group_name, sg.immunity, sg.flags
          FROM sb_srvgroups sg
          """;

            var groupData = (await connection.QueryAsync<dynamic>(sql)).ToList();

            if (groupData.Count == 0)
            {
                return new Dictionary<string, (List<string>, int)>();
            }

            var groupInfoDictionary = new Dictionary<string, (List<string>, int)>();

            foreach (var row in groupData)
            {
                var groupName = (string)row.group_name;
                var flags = ((string)row.flags).ToCharArray()
                    .Select(c => MapFlagToCssRole(c))
                    .Where(role => role != null)
                    .ToList();
                var immunity = (int)row.immunity;

                // Only add groups with at least one valid flag
                if (flags.Count > 0)
                {
                    groupInfoDictionary[groupName] = (flags!, immunity);
                }
            }

            return groupInfoDictionary;
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(ex.ToString());
        }

        return new Dictionary<string, (List<string>, int)>();
    }

    public async Task CrateGroupsJsonFile()
    {
        var groupsData = await GetAllGroupsData();

        var jsonStructure = new Dictionary<string, object>();

        foreach (var kvp in groupsData)
        {
            var groupData = new Dictionary<string, object>
            {
                ["flags"] = kvp.Value.Item1,
                ["immunity"] = kvp.Value.Item2
            };

            // Prepend '#' to the group name
            jsonStructure[$"#{kvp.Key}"] = groupData;
        }

        var json = JsonConvert.SerializeObject(jsonStructure, Formatting.Indented);
        await File.WriteAllTextAsync(CS2_SimpleAdmin.Instance.ModuleDirectory + "/data/groups.json", json);
    }

    public async Task CreateAdminsJsonFile()
    {
        var allPlayers = await GetAllPlayersFlags();

        var jsonData = allPlayers
            .Where(player => player.Item3.Count > 0) // Filter out players with no matching flags
            .Select(player =>
            {
                SteamID? steamId = null;

                if (!string.IsNullOrEmpty(player.Item1) && SteamID.TryParse(player.Item1, out var id) && id != null)
                {
                    steamId = id;
                }

                if (steamId != null && !AdminCache.ContainsKey(steamId))
                {
                    AdminCache.TryAdd(steamId, null);  // No end date handling in this case
                }

                return new
                {
                    playerName = player.Item2.ToLower(), // Ensure the player name is lowercase
                    playerData = new
                    {
                        identity = player.Item1,
                        immunity = player.Item4,
                        flags = player.Item3,
                        groups = new List<string> { $"#{player.Item5}" } // Ensure groups is a list and prepend '#'
                    }
                };
            })
            .ToDictionary(item => item.playerName, item => item.playerData);

        var json = JsonConvert.SerializeObject(jsonData, Formatting.Indented);
        await File.WriteAllTextAsync(CS2_SimpleAdmin.Instance.ModuleDirectory + "/data/admins.json", json);
    }

    public async Task DeleteAdminBySteamId(string playerSteamId)
    {
        if (string.IsNullOrEmpty(playerSteamId)) return;

        try
        {
            playerSteamId = Helper.ToSteam2(playerSteamId);
            await using var connection = await _database.GetConnectionAsync();

            const string sql = "DELETE FROM sb_admins WHERE authid = @PlayerSteamID";
            await connection.ExecuteAsync(sql, new { PlayerSteamID = playerSteamId });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(ex.ToString());
        }
    }

    public async Task AddAdminBySteamId(string playerSteamId, string playerName, string groupName, int immunity = 0)
    {
        if (string.IsNullOrEmpty(playerSteamId) || string.IsNullOrEmpty(groupName)) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            playerSteamId = Helper.ToSteam2(playerSteamId);
            await using var connection = await _database.GetConnectionAsync();
            var gid = -1;
            var email = GenerateRandomEmail();
            var password = "$2y$10$" + GenerateRandomPassword(52);

            const string insertAdminSql = "INSERT INTO `sb_admins` (`authid`, `user`, `srv_group`, `immunity`, `gid`, `email`, `password`) " +
                                          "VALUES (@playerSteamId, @playerName, @groupName, @immunity, @gid, @email, @password);";

            await connection.ExecuteAsync(insertAdminSql, new
            {
                playerSteamId,
                playerName,
                groupName,
                immunity,
                gid,
                email,
                password
            });

            await Server.NextFrameAsync(() =>
            {
                CS2_SimpleAdmin.Instance.ReloadAdmins(null);
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(ex.ToString());
        }
    }

    private string GenerateRandomPassword(int length)
    {
        string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();

        return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private string GenerateRandomEmail()
    {
        string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();

        string randomString(int length) => new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());

        return $"{randomString(8)}@{randomString(8)}.com";
    }

    public async Task DeleteGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;

        await using var connection = await _database.GetConnectionAsync();
        try
        {
            const string sql = "DELETE FROM `sb_srvgroups` WHERE name = @groupName";
            await connection.ExecuteAsync(sql, new { groupName });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(ex.ToString());
        }
    }
}
