// ChaosConnection.cs - Chaos network connection management
// Converted from chncp/chncp.h and chaos.c

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Chaos;

/// <summary>
/// Chaos network connection
/// </summary>
public class ChaosConnection
{
    // Connection identification
    public ushort LocalIndex { get; set; }
    public ushort RemoteIndex { get; set; }
    public ushort LocalAddress { get; set; }
    public ushort RemoteAddress { get; set; }
    
    // Connection state
    public ConnectionState State { get; set; }
    public ConnectionMode Mode { get; set; }
    
    // Window management
    public ushort ReceiveWindowSize { get; set; }
    public ushort TransmitWindowSize { get; set; }
    public ushort OutputRoom { get; set; }
    
    // Packet numbers
    public ushort LocalPacketNumber { get; set; }
    public ushort RemotePacketNumber { get; set; }
    public ushort AcknowledgedPacketNumber { get; set; }
    
    // Queues
    public Queue<ChaosPacket> ReceiveQueue { get; } = new();
    public Queue<ChaosPacket> TransmitQueue { get; } = new();
    public Queue<ChaosPacket> RetransmitQueue { get; } = new();
    
    // Timing
    public DateTime LastActivity { get; set; }
    public DateTime CreatedTime { get; set; }
    
    // Statistics
    public ulong PacketsSent { get; set; }
    public ulong PacketsReceived { get; set; }
    public ulong BytesSent { get; set; }
    public ulong BytesReceived { get; set; }
    public ulong RetransmitCount { get; set; }
    
    public ChaosConnection()
    {
        CreatedTime = DateTime.Now;
        LastActivity = DateTime.Now;
        State = ConnectionState.CSCLOSED;
        Mode = ConnectionMode.CHSTREAM;
        ReceiveWindowSize = 8;
        TransmitWindowSize = 8;
    }
    
    /// <summary>
    /// Open connection to remote host
    /// </summary>
    public void Open(ushort remoteHost, string contactName, byte[]? data = null)
    {
        RemoteAddress = remoteHost;
        State = ConnectionState.CSRFCSENT;
        
        // Create RFC packet
        var packet = new ChaosPacket
        {
            Opcode = PacketOpcode.RFCOP,
            DestAddress = remoteHost,
            DestIndex = 0,
            SourceAddress = LocalAddress,
            SourceIndex = LocalIndex,
            PacketNumber = LocalPacketNumber++,
            AckNumber = 0
        };
        
        // Set contact name and data
        var contactBytes = System.Text.Encoding.ASCII.GetBytes(contactName);
        if (data != null)
        {
            packet.Data = new byte[contactBytes.Length + data.Length + 1];
            Array.Copy(contactBytes, packet.Data, contactBytes.Length);
            packet.Data[contactBytes.Length] = 0; // Null separator
            Array.Copy(data, 0, packet.Data, contactBytes.Length + 1, data.Length);
        }
        else
        {
            packet.Data = contactBytes;
        }
        
        TransmitQueue.Enqueue(packet);
        LastActivity = DateTime.Now;
    }
    
    /// <summary>
    /// Accept incoming connection
    /// </summary>
    public void Accept()
    {
        if (State != ConnectionState.CSRFCRCVD)
            return;
        
        State = ConnectionState.CSOPEN;
        
        // Send OPN packet
        var packet = new ChaosPacket
        {
            Opcode = PacketOpcode.OPNOP,
            DestAddress = RemoteAddress,
            DestIndex = RemoteIndex,
            SourceAddress = LocalAddress,
            SourceIndex = LocalIndex,
            PacketNumber = LocalPacketNumber++,
            AckNumber = RemotePacketNumber
        };
        
        TransmitQueue.Enqueue(packet);
        LastActivity = DateTime.Now;
    }
    
    /// <summary>
    /// Reject incoming connection
    /// </summary>
    public void Reject(string reason)
    {
        if (State != ConnectionState.CSRFCRCVD)
            return;
        
        // Send CLS packet with reason
        var packet = new ChaosPacket
        {
            Opcode = PacketOpcode.CLSOP,
            DestAddress = RemoteAddress,
            DestIndex = RemoteIndex,
            SourceAddress = LocalAddress,
            SourceIndex = LocalIndex,
            PacketNumber = LocalPacketNumber++,
            AckNumber = RemotePacketNumber,
            Data = System.Text.Encoding.ASCII.GetBytes(reason)
        };
        
        TransmitQueue.Enqueue(packet);
        State = ConnectionState.CSCLOSED;
        LastActivity = DateTime.Now;
    }
    
    /// <summary>
    /// Close connection
    /// </summary>
    public void Close(string? reason = null)
    {
        if (State == ConnectionState.CSCLOSED)
            return;
        
        // Send CLS packet
        var packet = new ChaosPacket
        {
            Opcode = PacketOpcode.CLSOP,
            DestAddress = RemoteAddress,
            DestIndex = RemoteIndex,
            SourceAddress = LocalAddress,
            SourceIndex = LocalIndex,
            PacketNumber = LocalPacketNumber++,
            AckNumber = RemotePacketNumber,
            Data = reason != null ? System.Text.Encoding.ASCII.GetBytes(reason) : Array.Empty<byte>()
        };
        
        TransmitQueue.Enqueue(packet);
        State = ConnectionState.CSCLOSED;
        LastActivity = DateTime.Now;
    }
    
    /// <summary>
    /// Send data on connection
    /// </summary>
    public int Send(byte[] data)
    {
        if (State != ConnectionState.CSOPEN)
            return -1;
        
        // Create data packet
        var packet = new ChaosPacket
        {
            Opcode = PacketOpcode.DATOP,
            DestAddress = RemoteAddress,
            DestIndex = RemoteIndex,
            SourceAddress = LocalAddress,
            SourceIndex = LocalIndex,
            PacketNumber = LocalPacketNumber++,
            AckNumber = RemotePacketNumber,
            Data = data
        };
        
        TransmitQueue.Enqueue(packet);
        BytesSent += (ulong)data.Length;
        LastActivity = DateTime.Now;
        
        return data.Length;
    }
    
    /// <summary>
    /// Receive data from connection
    /// </summary>
    public byte[]? Receive()
    {
        if (ReceiveQueue.Count == 0)
            return null;
        
        var packet = ReceiveQueue.Dequeue();
        BytesReceived += (ulong)packet.Data.Length;
        LastActivity = DateTime.Now;
        
        return packet.Data;
    }
    
    /// <summary>
    /// Check if data is available to read
    /// </summary>
    public bool HasDataAvailable()
    {
        return ReceiveQueue.Count > 0;
    }
    
    /// <summary>
    /// Check if connection can send data
    /// </summary>
    public bool CanSend()
    {
        return State == ConnectionState.CSOPEN && 
               TransmitQueue.Count < TransmitWindowSize;
    }
}

/// <summary>
/// Chaos network packet
/// </summary>
public class ChaosPacket
{
    public PacketOpcode Opcode { get; set; }
    public ushort DestAddress { get; set; }
    public ushort DestIndex { get; set; }
    public ushort SourceAddress { get; set; }
    public ushort SourceIndex { get; set; }
    public ushort PacketNumber { get; set; }
    public ushort AckNumber { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int ForwardCount { get; set; }
    
    /// <summary>
    /// Serialize packet to bytes
    /// </summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        writer.Write((byte)Opcode);
        writer.Write((byte)0); // Reserved
        writer.Write((ushort)Data.Length);
        writer.Write(DestAddress);
        writer.Write(DestIndex);
        writer.Write(SourceAddress);
        writer.Write(SourceIndex);
        writer.Write(PacketNumber);
        writer.Write(AckNumber);
        writer.Write(Data);
        
        return ms.ToArray();
    }
    
    /// <summary>
    /// Deserialize packet from bytes
    /// </summary>
    public static ChaosPacket? FromBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);
            
            var packet = new ChaosPacket
            {
                Opcode = (PacketOpcode)reader.ReadByte()
            };
            
            reader.ReadByte(); // Reserved
            ushort length = reader.ReadUInt16();
            packet.DestAddress = reader.ReadUInt16();
            packet.DestIndex = reader.ReadUInt16();
            packet.SourceAddress = reader.ReadUInt16();
            packet.SourceIndex = reader.ReadUInt16();
            packet.PacketNumber = reader.ReadUInt16();
            packet.AckNumber = reader.ReadUInt16();
            
            if (length > 0)
            {
                packet.Data = reader.ReadBytes(length);
            }
            
            return packet;
        }
        catch
        {
            return null;
        }
    }
}
