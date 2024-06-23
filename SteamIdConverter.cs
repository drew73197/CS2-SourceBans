using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CS2_SourceBans
{
    public static class SteamIdConverter
    {
        public static string ToSteam2(string steam64Id)
        {
            if (string.IsNullOrWhiteSpace(steam64Id) || !ulong.TryParse(steam64Id, out var steamId64))
            {
                throw new ArgumentException("Invalid Steam64 ID.");
            }

            var authServer = steamId64 % 2;
            var authId = (steamId64 - 76561197960265728UL) / 2;

            return $"STEAM_0:{authServer}:{authId}";
        }
    }
}
