// Chaos.cs - Main Chaos definitions and constants
// Converted from chaos.h

namespace Chaos;

/// <summary>
/// Chaos network protocol constants and definitions
/// </summary>
public static class ChaosConstants
{
    // Maximum data per packet
    public const int CHMAXDATA = 488;
    
    // Length of node name in STATUS protocol
    public const int CHSTATNAME = 32;
    
    // Special characters
    public const int CHSP = 040;
    public const int CHNL = 0200 | '\r';
    public const int CHTAB = 0200 | '\t';
    public const int CHFF = 0200 | '\f';
    public const int CHBS = 0200 | '\b';
    public const int CHLF = 0200 | '\n';
    
    // Maximum data length in packet
    public const int CHMAXPKT = 488;
    
    // Maximum length of a RFC string
    public const int CHMAXRFC = CHMAXPKT;
    
    // Maximum number of words in a RFC
    public const int CHMAXARGS = 50;
    
    // Path names
    public const string CHAOSDEV = "/dev/chaos";
    public const string CHURFCDEV = "/dev/churfc";
    
    // Major/minor device numbers
    public const int CHAOS_MAJOR = 80;
    public const int CHURFCMIN = 120;
    public const int CHAOSMIN = 247;
}

/// <summary>
/// Connection states
/// </summary>
public enum ConnectionState
{
    CSCLOSED = 0,   // Closed
    CSLISTEN = 1,   // Listening
    CSRFCRCVD = 2,  // RFC received
    CSRFCSENT = 3,  // RFC sent
    CSOPEN = 4,     // Open
    CSLOST = 5,     // Broken by receipt of a LOS
    CSINCT = 6      // Broken by incomplete transmission
}

/// <summary>
/// Packet opcode types
/// </summary>
public enum PacketOpcode : byte
{
    RFCOP = 001,    // Request for connection
    OPNOP = 002,    // Open connection
    CLSOP = 003,    // Close connection
    FWDOP = 004,    // Forward this packet
    ANSOP = 005,    // Answer packet
    SNSOP = 006,    // Sense packet
    STSOP = 007,    // Status packet
    RUTOP = 8,      // 010 octal - Routing information packet
    LOSOP = 9,      // 011 octal - Losing connection packet
    LSNOP = 10,     // 012 octal - Listen packet (never transmitted)
    MNTOP = 11,     // 013 octal - Maintenance packet
    EOFOP = 12,     // 014 octal - End of File packet
    UNCOP = 13,     // 015 octal - Uncontrolled data packet
    BRDOP = 14,     // 016 octal - Broadcast RFC opcode
    DATOP = 128,    // 0200 octal - Ordinary character data
    DWDOP = 192     // 0300 octal - 16 bit word data
}

/// <summary>
/// Modes available in connection mode setting
/// </summary>
public enum ConnectionMode
{
    CHTTY = 1,
    CHSTREAM = 2,
    CHRECORD = 3
}

/// <summary>
/// Known contact names
/// </summary>
public static class ChaosContactNames
{
    public const string FILE = "FILE";
    public const string SUPDUP = "SUPDUP";
    public const string TELNET = "TELNET";
    public const string STATUS = "STATUS";
    public const string TIME = "TIME";
    public const string ARPA = "ARPA";
    public const string SEND = "SEND";
    public const string RTAPE = "RTAPE";
    public const string MAIL = "MAIL";
    public const string ULOGIN = "ulogin";
    public const string UREAD = "uread";
    public const string UWRITE = "uwrite";
    public const string UCSH = "ucsh";
    public const string USEND = "usend";
}

/// <summary>
/// Connection status information
/// </summary>
public struct ChStatus
{
    public short ForeignHost;      // Remote host
    public short ChannelNumber;    // Local channel number
    public short ReceiveWindowSize;   // Receive window size
    public short TransmitWindowSize;  // Transmit window size
    public ConnectionState State;     // Connection state
    public PacketOpcode PacketType;   // Opcode of next packet to read
    public short PacketLength;     // Length of next packet to read
    public ConnectionMode Mode;       // Mode of connection
    public short OutputRoom;       // Output window space left
}

/// <summary>
/// Record mode packet structure
/// </summary>
public struct ChPacket
{
    public byte Opcode;
    public byte[] Data;
    
    public ChPacket()
    {
        Opcode = 0;
        Data = new byte[ChaosConstants.CHMAXDATA];
    }
}

/// <summary>
/// FILE server login record structure
/// </summary>
public struct ChLogin
{
    public int ProcessId;          // Process id of server
    public short ChannelNumber;    // Chaos channel number of server
    public short HostAddress;      // Host address of other end
    public long LoginTime;         // Login time
    public long LastAccessTime;    // Last time used
    public string UserName;        // User name (8 chars)
    
    public ChLogin()
    {
        ProcessId = 0;
        ChannelNumber = 0;
        HostAddress = 0;
        LoginTime = 0;
        LastAccessTime = 0;
        UserName = string.Empty;
    }
}

/// <summary>
/// Structure for connection open
/// </summary>
public struct ChOpen
{
    public string? ContactString;  // Contact string
    public byte[]? Data;           // RFC data if not NULL
    public short Host;             // Host address to contact or zero for listen
    public short Async;            // If non zero don't wait
    public short ContactLength;    // Length of contact string
    public short Length;           // Length of RFC data
    public short ReceiveWindowSize;   // Receive window size
}

/// <summary>
/// Structure for connection rejection
/// </summary>
public struct ChReject
{
    public string? Reason;
    public int Length;
}

/// <summary>
/// Structure for Ethernet configuration
/// </summary>
public struct ChEther
{
    public const int CHIFNAMSIZ = 16;
    
    public string Name;  // Interface name (max 16 chars)
    public short Address;
    
    public ChEther()
    {
        Name = string.Empty;
        Address = 0;
    }
}
