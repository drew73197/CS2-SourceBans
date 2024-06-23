using System.Text;
using CounterStrikeSharp.API;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2_SimpleAdmin;

internal class BanManager
{
    private readonly Database.Database _database;
    private readonly CS2_SimpleAdminConfig _config;

    public BanManager(Database.Database database, CS2_SimpleAdminConfig config)
    {
        _database = database;
        _config = config;
    }

    public async Task BanPlayer(PlayerInfo player, PlayerInfo issuer, string reason, int time = 0)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var futureTime = now + (time * 60);

        await using var connection = await _database.GetConnectionAsync();
        try
        {
            var adminId = await GetAdminIdBySteamId(issuer.SteamId32, connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            const string sql =
                "INSERT INTO `sb_bans` (`authid`, `name`, `ip`, `reason`, `length`, `ends`, `created`, `aid`, `sid`, `adminIp`) " +
                "VALUES (@playerSteamid, @playerName, @playerIp, @banReason, @length, @ends, @created, @adminId, @serverid, @adminIp)";

            await connection.ExecuteAsync(sql, new
            {
                playerSteamid = player.SteamId32,
                playerName = player.Name,
                playerIp = player.IpAddress,
                banReason = reason,
                length = time * 60,
                ends = futureTime,
                created = now,
                adminId = adminId.Value,
                serverid = -1,
                adminIp = issuer.IpAddress // Assuming issuer has an IpAddress property
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error banning player: {ex.Message}");
        }
    }

    private async Task<int?> GetAdminIdBySteamId(string? steamId, MySqlConnection connection)
    {
        if (string.IsNullOrEmpty(steamId))
        {
            return null;
        }
        const string sql = "SELECT aid FROM `sb_admins` WHERE authid = @SteamId";
        return await connection.ExecuteScalarAsync<int?>(sql, new { SteamId = steamId });
    }

    public async Task AddBanBySteamid(string playerSteamId, PlayerInfo issuer, string reason, int time = 0)
    {
        if (string.IsNullOrEmpty(playerSteamId)) return;
        playerSteamId = Helper.ToSteam2(playerSteamId);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var futureTime = now + (time * 60);

        await using var connection = await _database.GetConnectionAsync();
        try
        {
            var adminId = await GetAdminIdBySteamId(issuer.SteamId32, connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            const string sql =
                "INSERT INTO `sb_bans` (`authid`, `reason`, `length`, `ends`, `created`, `aid`, `sid`, `adminIp`) " +
                "VALUES (@playerSteamid, @banReason, @length, @ends, @created, @adminId, @serverid, @adminIp)";

            await connection.ExecuteAsync(sql, new
            {
                playerSteamid = playerSteamId,
                banReason = reason,
                length = time * 60,
                ends = futureTime,
                created = now,
                adminId = adminId.Value,
                serverid = CS2_SimpleAdmin.ServerId,
                adminIp = issuer.IpAddress // Assuming issuer has an IpAddress property
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error adding ban by SteamID: {ex.Message}");
        }
    }

    public async Task<bool> IsPlayerBanned(PlayerInfo player)
    {
        if (player.SteamId32 == null && player.IpAddress == null)
        {
            return false;
        }

#if DEBUG
        if (CS2_SimpleAdmin._logger != null)
            CS2_SimpleAdmin._logger.LogCritical($"IsPlayerBanned for {player.Name}");
#endif

        int banCount;
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            const string sql = @"
                SELECT COUNT(*) FROM sb_bans
                WHERE (authid = @PlayerSteamID OR ip = @PlayerIP)
                AND RemovedOn IS NULL
                AND (length = 0 OR ends > @CurrentTime);";

            await using var connection = await _database.GetConnectionAsync();

            var parameters = new
            {
                PlayerSteamID = player.SteamId32,
                PlayerIP = _config.BanType == 0 || string.IsNullOrEmpty(player.IpAddress) ? null : player.IpAddress,
                CurrentTime = currentTime
            };

            banCount = await connection.ExecuteScalarAsync<int>(sql, parameters);
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error checking if player is banned: {ex.Message}");
            return false;
        }

        return banCount > 0;
    }

    public async Task<int> GetPlayerBans(PlayerInfo player)
    {
        try
        {
            const string sql = "SELECT COUNT(*) FROM sb_bans WHERE (authid = @PlayerSteamID OR ip = @PlayerIP)";

            int banCount;

            await using var connection = await _database.GetConnectionAsync();

            if (_config.BanType > 0 && !string.IsNullOrEmpty(player.IpAddress))
            {
                banCount = await connection.ExecuteScalarAsync<int>(sql,
                    new
                    {
                        PlayerSteamID = player.SteamId32,
                        PlayerIP = player.IpAddress
                    });
            }
            else
            {
                banCount = await connection.ExecuteScalarAsync<int>(sql,
                    new
                    {
                        PlayerSteamID = player.SteamId32,
                        PlayerIP = DBNull.Value
                    });
            }

            return banCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task UnbanPlayer(string playerPattern, string adminSteamId, string reason)
    {
        if (playerPattern is not { Length: > 1 })
        {
            return;
        }
        try
        {
            await using var connection = await _database.GetConnectionAsync();

            const string sqlRetrieveBans = "SELECT bid FROM sb_bans WHERE (authid = @pattern OR name = @pattern OR ip = @pattern) AND RemoveType IS NULL";

            var bans = await connection.QueryAsync(sqlRetrieveBans, new { pattern = playerPattern });

            var bansList = bans as dynamic[] ?? bans.ToArray();
            if (bansList.Length == 0)
                return;

            var adminId = await GetAdminIdBySteamId(Helper.ToSteam2(adminSteamId), connection);
            if (adminId == null)
            {
                throw new Exception("Admin not found");
            }

            foreach (var ban in bansList)
            {
                int banId = ban.bid;

                // Update sb_bans to set RemovedBy, RemoveType, RemovedOn, and ureason
                const string sqlUpdateBan = "UPDATE sb_bans SET RemovedBy = @adminId, RemoveType = 'U', RemovedOn = @currentTime, ureason = @reason WHERE bid = @banId";
                await connection.ExecuteAsync(sqlUpdateBan, new { banId, adminId = adminId.Value, reason, currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }

        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error unbanning player: {ex.Message}");
        }
    }

    public async Task CheckOnlinePlayers(List<(string? IpAddress, ulong SteamID, int? UserId, int Slot)> players)
    {
        try
        {
            await using var connection = await _database.GetConnectionAsync();
            bool checkIpBans = _config.BanType > 0;

            var filteredPlayers = players.Where(p => p.UserId.HasValue).ToList();

            var steamIds = filteredPlayers.Select(p => Helper.ToSteam2(p.SteamID.ToString())).Distinct().ToList();
            var ipAddresses = filteredPlayers
                .Where(p => !string.IsNullOrEmpty(p.IpAddress))
                .Select(p => p.IpAddress)
                .Distinct()
                .ToList();

            var sql = new StringBuilder();
            sql.Append("SELECT `authid`, `ip` FROM `sb_bans` WHERE RemoveType IS NULL AND (authid IN @SteamIDs");

            if (checkIpBans && ipAddresses.Count != 0)
            {
                sql.Append(" OR ip IN @IpAddresses");
            }
            sql.Append(')');

            var bannedPlayers = await connection.QueryAsync<(string AuthID, string IP)>(
                sql.ToString(),
                new
                {
                    SteamIDs = steamIds,
                    IpAddresses = checkIpBans ? ipAddresses : new List<string>()
                });

            var bannedSteamIds = bannedPlayers.Select(b => b.AuthID).ToHashSet();
            var bannedIps = bannedPlayers.Select(b => b.IP).ToHashSet();

            foreach (var player in filteredPlayers.Where(player => bannedSteamIds.Contains(Helper.ToSteam2(player.SteamID.ToString())) ||
                                                                   (checkIpBans && bannedIps.Contains(player.IpAddress ?? ""))))
            {
                if (!player.UserId.HasValue) continue;

                await Server.NextFrameAsync(() =>
                {
                    Helper.KickPlayer(player.UserId.Value, "Banned");
                });
            }
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error checking online players: {ex.Message}");
        }
    }
}
