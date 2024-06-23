namespace CS2_SimpleAdmin
{
    public class PlayerInfo
    {
        public int UserId { get; init; }
        public int Slot { get; init; }
        public string? SteamId { get; init; }
        public string? Name { get; init; }
        public string? IpAddress { get; init; }

        private string? _steamId32;
        public string? SteamId32
        {
            get => _steamId32 ?? (SteamId != null ? Helper.ToSteam2(SteamId) : null);
            init => _steamId32 = value;
        }
    }
}