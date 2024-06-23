using CounterStrikeSharp.API.Core;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2_SimpleAdmin;

internal class MuteManager
{
    private readonly Database.Database _database;

    public MuteManager(Database.Database database)
    {
        _database = database;
    }

    public async Task MutePlayer(PlayerInfo player, PlayerInfo issuer, string reason, int time = 0, int type = 0)
    {
        if (player.SteamId == null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var futureTime = now + (time * 60);

        var muteType = type == 1 ? 1 : 2;

        try
        {
            await using var connection = await _database.GetConnectionAsync();
            var adminId = await GetAdminIdBySteamId(issuer.SteamId32, connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            const string sql =
                "INSERT INTO `sb_comms` (`authid`, `name`, `reason`, `length`, `ends`, `created`, `aid`, `type`) " +
                "VALUES (@playerSteamid, @playerName, @muteReason, @duration, @ends, @created, @adminId, @type)";

            await connection.ExecuteAsync(sql, new
            {
                playerSteamid = player.SteamId32,
                playerName = player.Name,
                muteReason = reason,
                duration = time * 60,
                ends = futureTime,
                created = now,
                adminId = adminId.Value,
                type = muteType
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error muting player: {ex.Message}");
        }
    }

    private async Task<int?> GetAdminIdBySteamId(string? steamId, MySqlConnection connection)
    {
        if (string.IsNullOrEmpty(steamId))
        {
            return null;
        }
        steamId = Helper.ToSteam2(steamId);

        const string sql = "SELECT aid FROM `sb_admins` WHERE authid = @SteamId";
        return await connection.ExecuteScalarAsync<int?>(sql, new { SteamId = steamId });
    }

    public async Task AddMuteBySteamid(string playerSteamId, PlayerInfo issuer, string reason, int time = 0, int type = 0)
    {
        if (string.IsNullOrEmpty(playerSteamId)) return;
        playerSteamId = Helper.ToSteam2(playerSteamId);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var futureTime = now + (time * 60);

        var muteType = type == 1 ? 1 : 2;

        try
        {
            await using var connection = await _database.GetConnectionAsync();
            var adminId = await GetAdminIdBySteamId(issuer.SteamId32, connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            const string sql =
                "INSERT INTO `sb_comms` (`authid`, `reason`, `length`, `ends`, `created`, `aid`, `type`) " +
                "VALUES (@playerSteamid, @muteReason, @duration, @ends, @created, @adminId, @type)";

            await connection.ExecuteAsync(sql, new
            {
                playerSteamid = playerSteamId,
                muteReason = reason,
                duration = time * 60,
                ends = futureTime,
                created = now,
                adminId = adminId.Value,
                type = muteType
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error adding mute by SteamID: {ex.Message}");
        }
    }

    public async Task<List<dynamic>> IsPlayerMuted(string steamId)
    {
        if (string.IsNullOrEmpty(steamId))
        {
            return new List<dynamic>();
        }

#if DEBUG
        if (CS2_SimpleAdmin._logger != null)
            CS2_SimpleAdmin._logger.LogCritical($"IsPlayerMuted for {steamId}");
#endif

        try
        {
            await using var connection = await _database.GetConnectionAsync();
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sql = "SELECT * FROM sb_comms WHERE authid = @PlayerSteamID AND RemoveType IS NULL AND (length = 0 OR ends > @CurrentTime)";

            var parameters = new { PlayerSteamID = steamId, CurrentTime = currentTime };
            var activeMutes = (await connection.QueryAsync(sql, parameters)).ToList();
            return activeMutes;
        }
        catch (Exception)
        {
            return new List<dynamic>();
        }
    }

    public async Task<int> GetPlayerMutes(string steamId)
    {
        try
        {
            await using var connection = await _database.GetConnectionAsync();

            var sql = "SELECT COUNT(*) FROM sb_comms WHERE authid = @PlayerSteamID";

            var muteCount = await connection.ExecuteScalarAsync<int>(sql, new { PlayerSteamID = steamId });
            return muteCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task CheckOnlineModeMutes(List<(string? IpAddress, ulong SteamID, int? UserId, int Slot)> players)
    {
        try
        {
            int batchSize = 10;
            await using var connection = await _database.GetConnectionAsync();

            var sql = "UPDATE `sb_comms` SET passed = COALESCE(passed, 0) + 1 WHERE (authid = @PlayerSteamID) AND length > 0 AND RemoveType IS NULL";

            for (var i = 0; i < players.Count; i += batchSize)
            {
                var batch = players.Skip(i).Take(batchSize);
                var parametersList = new List<object>();

                foreach (var (IpAddress, SteamID, UserId, Slot) in batch)
                {
                    parametersList.Add(new { PlayerSteamID = Helper.ToSteam2(SteamID.ToString()) });
                }

                await connection.ExecuteAsync(sql, parametersList);
            }

            sql = "SELECT * FROM `sb_comms` WHERE authid = @PlayerSteamID AND passed >= length AND length > 0 AND RemoveType IS NULL";

            foreach (var (IpAddress, SteamID, UserId, Slot) in players)
            {
                var muteRecords = await connection.QueryAsync(sql, new { PlayerSteamID = Helper.ToSteam2(SteamID.ToString()) });

                foreach (var muteRecord in muteRecords)
                {
                    DateTime endDateTime = DateTimeOffset.FromUnixTimeSeconds(muteRecord.ends).DateTime;
                    PlayerPenaltyManager.RemovePenaltiesByDateTime(Slot, endDateTime);
                }
            }
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error checking online mode mutes: {ex.Message}");
        }
    }

    public async Task UnmutePlayer(string playerPattern, string adminSteamId, string reason, int type = 0)
    {
        if (playerPattern.Length <= 1)
        {
            return;
        }

        try
        {
            await using var connection = await _database.GetConnectionAsync();

            var muteType = type == 1 ? 1 : 2;

            var sqlRetrieveMutes = "SELECT bid FROM sb_comms WHERE (authid = @pattern OR name = @pattern) AND type = @muteType AND RemoveType IS NULL";

            var mutes = await connection.QueryAsync(sqlRetrieveMutes, new { pattern = playerPattern, muteType });

            var mutesList = mutes as dynamic[] ?? mutes.ToArray();
            if (mutesList.Length == 0)
                return;

            var adminId = await GetAdminIdBySteamId(adminSteamId, connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            foreach (var mute in mutesList)
            {
                int muteId = mute.bid;

                // Update sb_comms to set RemovedBy, RemoveType, RemovedOn, and ureason
                const string sqlUpdateMute = "UPDATE sb_comms SET RemovedBy = @adminId, RemoveType = 'U', RemovedOn = @currentTime, ureason = @reason WHERE bid = @muteId";
                await connection.ExecuteAsync(sqlUpdateMute, new { muteId, adminId = adminId.Value, reason, currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error unmuting player: {ex.Message}");
        }
    }

    public async Task ExpireOldMutes()
    {
        try
        {
            await using var connection = await _database.GetConnectionAsync();
            var sql = "UPDATE sb_comms SET RemoveType = 'E', RemovedOn = @CurrentTime WHERE length > 0 AND ends <= @CurrentTime";

            await connection.ExecuteAsync(sql, new { CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogCritical($"Unable to remove expired mutes: {ex.Message}");
        }
    }
}
