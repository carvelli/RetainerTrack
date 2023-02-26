using System.IO;
using System.Text;

namespace RetainerTrack.Handlers
{
    internal sealed class ContentIdToName
    {
        public ulong ContentId { get; init; }
        public string PlayerName { get; init; } = string.Empty;

        public static unsafe ContentIdToName ReadFromNetworkPacket(nint dataPtr)
        {
            using UnmanagedMemoryStream input = new UnmanagedMemoryStream((byte*)dataPtr.ToPointer(), 40);
            using BinaryReader binaryReader = new BinaryReader(input);

            return new ContentIdToName
            {
                ContentId = binaryReader.ReadUInt64(),
                PlayerName = Encoding.UTF8.GetString(binaryReader.ReadBytes(32)).TrimEnd(char.MinValue)
            };
        }
    }
}
