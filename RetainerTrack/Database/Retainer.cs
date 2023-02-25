namespace RetainerTrack.Database
{
    internal sealed class Retainer
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public ushort WorldId { get; set; }
        public ulong OwnerContentId { get; set; }
    }
}
