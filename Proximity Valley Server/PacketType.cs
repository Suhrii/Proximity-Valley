namespace Proximity_Valley_Server;

public enum PacketType : byte
{
    Audio = 0x01,
    Location = 0x02,
    Connect = 0x03,
    Disconnect = 0x04,
}