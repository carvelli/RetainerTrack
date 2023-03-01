namespace RetainerTrack.Handlers
{
    internal sealed class ContentIdToName
    {
        public ulong ContentId { get; init; }
        public string PlayerName { get; init; } = string.Empty;
    }
}
