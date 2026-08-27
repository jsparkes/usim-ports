// ChaosNetwork.cs - Chaos network manager
// Converted from chunix/chaos.c

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Chaos;

/// <summary>
/// Chaos network manager
/// Handles connections, routing, and packet processing
/// </summary>
public class ChaosNetwork
{
    // Network configuration
    public ushort LocalAddress { get; set; }
    public string? InterfaceName { get; set; }
    
    // Connection table
    private readonly ConcurrentDictionary<ushort, ChaosConnection> _connections = new();
    private ushort _nextConnectionIndex = 1;
    
    // Listener for incoming connections
    private readonly ConcurrentDictionary<string, Func<ChaosConnection, bool>> _listeners = new();
    
    // Packet queues
    private readonly ConcurrentQueue<ChaosPacket> _incomingPackets = new();
    private readonly ConcurrentQueue<ChaosPacket> _outgoingPackets = new();
    
    // Statistics
    public ulong PacketsReceived { get; private set; }
    public ulong PacketsSent { get; private set; }
    public ulong PacketsDropped { get; private set; }
    public ulong BytesReceived { get; private set; }
    public ulong BytesSent { get; private set; }
    
    private bool _running;
    private Thread? _processingThread;
    
    /// <summary>
    /// Initialize Chaos network
    /// </summary>
    public void Initialize(ushort localAddress)
    {
        LocalAddress = localAddress;
        Console.WriteLine($"Chaos network initialized: address {GetSubnet(localAddress)}.{GetHost(localAddress)}");
    }
    
    /// <summary>
    /// Start network processing
    /// </summary>
    public void Start()
    {
        if (_running)
            return;
        
        _running = true;
        _processingThread = new Thread(ProcessingLoop)
        {
            Name = "Chaos Network",
            IsBackground = true
        };
        _processingThread.Start();
        
        Console.WriteLine("Chaos network started");
    }
    
    /// <summary>
    /// Stop network processing
    /// </summary>
    public void Stop()
    {
        _running = false;
        _processingThread?.Join(1000);
        Console.WriteLine("Chaos network stopped");
    }
    
    /// <summary>
    /// Create new connection
    /// </summary>
    public ChaosConnection CreateConnection()
    {
        var conn = new ChaosConnection
        {
            LocalIndex = _nextConnectionIndex++,
            LocalAddress = LocalAddress
        };
        
        _connections[conn.LocalIndex] = conn;
        return conn;
    }
    
    /// <summary>
    /// Close and remove connection
    /// </summary>
    public void CloseConnection(ushort index)
    {
        if (_connections.TryRemove(index, out var conn))
        {
            conn.Close();
        }
    }
    
    /// <summary>
    /// Get connection by index
    /// </summary>
    public ChaosConnection? GetConnection(ushort index)
    {
        _connections.TryGetValue(index, out var conn);
        return conn;
    }
    
    /// <summary>
    /// Register listener for contact name
    /// </summary>
    public void Listen(string contactName, Func<ChaosConnection, bool> handler)
    {
        _listeners[contactName.ToUpperInvariant()] = handler;
        Console.WriteLine($"Listening on contact name: {contactName}");
    }
    
    /// <summary>
    /// Unregister listener
    /// </summary>
    public void Unlisten(string contactName)
    {
        _listeners.TryRemove(contactName.ToUpperInvariant(), out _);
    }
    
    /// <summary>
    /// Send packet
    /// </summary>
    public void SendPacket(ChaosPacket packet)
    {
        _outgoingPackets.Enqueue(packet);
    }
    
    /// <summary>
    /// Receive packet (for external delivery)
    /// </summary>
    public void ReceivePacket(ChaosPacket packet)
    {
        _incomingPackets.Enqueue(packet);
    }
    
    /// <summary>
    /// Main processing loop
    /// </summary>
    private void ProcessingLoop()
    {
        while (_running)
        {
            try
            {
                // Process incoming packets
                while (_incomingPackets.TryDequeue(out var packet))
                {
                    ProcessIncomingPacket(packet);
                }
                
                // Process outgoing packets
                while (_outgoingPackets.TryDequeue(out var packet))
                {
                    ProcessOutgoingPacket(packet);
                }
                
                // Process connection queues
                foreach (var conn in _connections.Values)
                {
                    ProcessConnection(conn);
                }
                
                // Cleanup old connections
                CleanupConnections();
                
                Thread.Sleep(10); // Yield CPU
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Chaos processing loop: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Process incoming packet
    /// </summary>
    private void ProcessIncomingPacket(ChaosPacket packet)
    {
        PacketsReceived++;
        BytesReceived += (ulong)packet.Data.Length;
        
        // Find destination connection
        ChaosConnection? conn = null;
        
        if (packet.DestIndex != 0)
        {
            conn = GetConnection(packet.DestIndex);
        }
        
        switch (packet.Opcode)
        {
            case PacketOpcode.RFCOP:
                HandleRFC(packet);
                break;
                
            case PacketOpcode.OPNOP:
                if (conn != null)
                {
                    conn.State = ConnectionState.CSOPEN;
                    conn.RemoteAddress = packet.SourceAddress;
                    conn.RemoteIndex = packet.SourceIndex;
                }
                break;
                
            case PacketOpcode.CLSOP:
                if (conn != null)
                {
                    conn.State = ConnectionState.CSCLOSED;
                }
                break;
                
            case PacketOpcode.DATOP:
            case PacketOpcode.EOFOP:
                if (conn != null && conn.State == ConnectionState.CSOPEN)
                {
                    conn.ReceiveQueue.Enqueue(packet);
                    conn.PacketsReceived++;
                }
                break;
                
            case PacketOpcode.SNSOP:
                HandleSNS(packet);
                break;
                
            case PacketOpcode.STSOP:
                HandleSTS(packet);
                break;
        }
    }
    
    /// <summary>
    /// Handle RFC (Request For Connection)
    /// </summary>
    private void HandleRFC(ChaosPacket packet)
    {
        // Extract contact name
        string contactName = ExtractContactName(packet.Data);
        
        // Find listener
        if (_listeners.TryGetValue(contactName.ToUpperInvariant(), out var handler))
        {
            // Create new connection
            var conn = CreateConnection();
            conn.State = ConnectionState.CSRFCRCVD;
            conn.RemoteAddress = packet.SourceAddress;
            conn.RemoteIndex = packet.SourceIndex;
            
            // Call handler
            if (handler(conn))
            {
                conn.Accept();
            }
            else
            {
                conn.Reject("Connection refused");
            }
        }
        else
        {
            // No listener - send rejection
            SendRejection(packet, "No such contact name");
        }
    }
    
    /// <summary>
    /// Handle SNS (Sense) packet
    /// </summary>
    private void HandleSNS(ChaosPacket packet)
    {
        // Send status response
        var response = new ChaosPacket
        {
            Opcode = PacketOpcode.STSOP,
            DestAddress = packet.SourceAddress,
            DestIndex = packet.SourceIndex,
            SourceAddress = LocalAddress,
            SourceIndex = 0
        };
        
        SendPacket(response);
    }
    
    /// <summary>
    /// Handle STS (Status) packet
    /// </summary>
    private void HandleSTS(ChaosPacket packet)
    {
        // Process status information
    }
    
    /// <summary>
    /// Send rejection packet
    /// </summary>
    private void SendRejection(ChaosPacket rfcPacket, string reason)
    {
        var response = new ChaosPacket
        {
            Opcode = PacketOpcode.CLSOP,
            DestAddress = rfcPacket.SourceAddress,
            DestIndex = rfcPacket.SourceIndex,
            SourceAddress = LocalAddress,
            SourceIndex = 0,
            Data = System.Text.Encoding.ASCII.GetBytes(reason)
        };
        
        SendPacket(response);
    }
    
    /// <summary>
    /// Process outgoing packet
    /// </summary>
    private void ProcessOutgoingPacket(ChaosPacket packet)
    {
        PacketsSent++;
        BytesSent += (ulong)packet.Data.Length;
        
        // In real implementation, this would transmit on the network
        // For now, just log
        // Console.WriteLine($"TX: {packet.Opcode} {packet.SourceAddress}->{packet.DestAddress}");
    }
    
    /// <summary>
    /// Process connection transmit/retransmit queues
    /// </summary>
    private void ProcessConnection(ChaosConnection conn)
    {
        // Send queued packets
        while (conn.TransmitQueue.Count > 0 && conn.CanSend())
        {
            var packet = conn.TransmitQueue.Dequeue();
            SendPacket(packet);
            conn.PacketsSent++;
        }
    }
    
    /// <summary>
    /// Cleanup old closed connections
    /// </summary>
    private void CleanupConnections()
    {
        var timeout = TimeSpan.FromMinutes(5);
        var now = DateTime.Now;
        
        foreach (var kvp in _connections)
        {
            if (kvp.Value.State == ConnectionState.CSCLOSED &&
                (now - kvp.Value.LastActivity) > timeout)
            {
                _connections.TryRemove(kvp.Key, out _);
            }
        }
    }
    
    /// <summary>
    /// Extract contact name from RFC data
    /// </summary>
    private string ExtractContactName(byte[] data)
    {
        int nullIndex = Array.IndexOf(data, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : data.Length;
        return System.Text.Encoding.ASCII.GetString(data, 0, length);
    }
    
    /// <summary>
    /// Get subnet from address
    /// </summary>
    private static byte GetSubnet(ushort address) => (byte)(address >> 8);
    
    /// <summary>
    /// Get host from address
    /// </summary>
    private static byte GetHost(ushort address) => (byte)(address & 0xFF);
    
    /// <summary>
    /// Print network statistics
    /// </summary>
    public void PrintStatistics()
    {
        Console.WriteLine("Chaos Network Statistics:");
        Console.WriteLine($"  Address:    {GetSubnet(LocalAddress)}.{GetHost(LocalAddress)}");
        Console.WriteLine($"  RX Packets: {PacketsReceived:N0} ({BytesReceived:N0} bytes)");
        Console.WriteLine($"  TX Packets: {PacketsSent:N0} ({BytesSent:N0} bytes)");
        Console.WriteLine($"  Dropped:    {PacketsDropped:N0}");
        Console.WriteLine($"  Active Connections: {_connections.Count}");
    }
}
