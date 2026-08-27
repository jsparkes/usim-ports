// ChaosFileServer.cs - FILE protocol server for Chaosnet
// Implements basic FILE server protocol

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Chaos;

/// <summary>
/// FILE server operations
/// </summary>
public enum FileOperation
{
    Open,
    Close,
    Read,
    Write,
    Delete,
    Rename,
    Directory,
    Properties,
    Unknown
}

/// <summary>
/// FILE server implementation
/// Provides file access over Chaosnet
/// </summary>
public class ChaosFileServer
{
    private readonly ChaosNetwork _network;
    private readonly string _rootDirectory;
    private readonly Dictionary<ushort, FileSession> _sessions = new();
    
    public ChaosFileServer(ChaosNetwork network, string rootDirectory)
    {
        _network = network;
        _rootDirectory = Path.GetFullPath(rootDirectory);
        
        if (!Directory.Exists(_rootDirectory))
        {
            Directory.CreateDirectory(_rootDirectory);
        }
        
        // Register FILE listener
        _network.Listen(ChaosContactNames.FILE, HandleConnection);
        
        Console.WriteLine($"FILE server started, root: {_rootDirectory}");
    }
    
    /// <summary>
    /// Handle incoming FILE connection
    /// </summary>
    private bool HandleConnection(ChaosConnection conn)
    {
        Console.WriteLine($"FILE: Connection from {conn.RemoteAddress:X4}");
        
        // Create session
        var session = new FileSession
        {
            Connection = conn,
            RootDirectory = _rootDirectory
        };
        
        _sessions[conn.LocalIndex] = session;
        
        // Start session handler
        Task.Run(() => HandleSession(session));
        
        return true; // Accept connection
    }
    
    /// <summary>
    /// Handle FILE session
    /// </summary>
    private async Task HandleSession(FileSession session)
    {
        try
        {
            while (session.Connection.State == ConnectionState.CSOPEN)
            {
                if (session.Connection.HasDataAvailable())
                {
                    byte[]? data = session.Connection.Receive();
                    if (data != null)
                    {
                        ProcessCommand(session, data);
                    }
                }
                
                await Task.Delay(10);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FILE session error: {ex.Message}");
        }
        finally
        {
            CloseSession(session);
        }
    }
    
    /// <summary>
    /// Process FILE command
    /// </summary>
    private void ProcessCommand(FileSession session, byte[] data)
    {
        string command = Encoding.ASCII.GetString(data).TrimEnd('\0', '\r', '\n');
        Console.WriteLine($"FILE command: {command}");
        
        string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        
        string cmd = parts[0].ToUpperInvariant();
        
        try
        {
            switch (cmd)
            {
                case "OPEN":
                    HandleOpen(session, parts);
                    break;
                    
                case "CLOSE":
                    HandleClose(session, parts);
                    break;
                    
                case "READ":
                    HandleRead(session, parts);
                    break;
                    
                case "WRITE":
                    HandleWrite(session, parts);
                    break;
                    
                case "DELETE":
                    HandleDelete(session, parts);
                    break;
                    
                case "DIRECTORY":
                case "DIR":
                    HandleDirectory(session, parts);
                    break;
                    
                default:
                    SendError(session, $"Unknown command: {cmd}");
                    break;
            }
        }
        catch (Exception ex)
        {
            SendError(session, $"Error: {ex.Message}");
        }
    }
    
    private void HandleOpen(FileSession session, string[] parts)
    {
        if (parts.Length < 2)
        {
            SendError(session, "OPEN requires filename");
            return;
        }
        
        string filename = parts[1];
        string fullPath = GetFullPath(filename);
        
        if (!IsPathAllowed(fullPath))
        {
            SendError(session, "Access denied");
            return;
        }
        
        if (session.CurrentFile != null)
        {
            session.CurrentFile.Close();
        }
        
        FileMode mode = parts.Length > 2 && parts[2].ToUpperInvariant() == "WRITE" 
            ? FileMode.OpenOrCreate 
            : FileMode.Open;
        
        session.CurrentFile = File.Open(fullPath, mode, FileAccess.ReadWrite);
        session.CurrentFilename = filename;
        
        SendResponse(session, "OK");
    }
    
    private void HandleClose(FileSession session, string[] parts)
    {
        if (session.CurrentFile != null)
        {
            session.CurrentFile.Close();
            session.CurrentFile = null;
            session.CurrentFilename = null;
        }
        
        SendResponse(session, "OK");
    }
    
    private void HandleRead(FileSession session, string[] parts)
    {
        if (session.CurrentFile == null)
        {
            SendError(session, "No file open");
            return;
        }
        
        int count = 256; // Default block size
        if (parts.Length > 1 && int.TryParse(parts[1], out int requestedCount))
        {
            count = requestedCount;
        }
        
        byte[] buffer = new byte[count];
        int bytesRead = session.CurrentFile.Read(buffer, 0, count);
        
        if (bytesRead > 0)
        {
            byte[] data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);
            session.Connection.Send(data);
        }
        else
        {
            SendResponse(session, "EOF");
        }
    }
    
    private void HandleWrite(FileSession session, string[] parts)
    {
        if (session.CurrentFile == null)
        {
            SendError(session, "No file open");
            return;
        }
        
        // Next packet should contain data to write
        // This is simplified - real implementation would handle binary data
        SendResponse(session, "READY");
    }
    
    private void HandleDelete(FileSession session, string[] parts)
    {
        if (parts.Length < 2)
        {
            SendError(session, "DELETE requires filename");
            return;
        }
        
        string filename = parts[1];
        string fullPath = GetFullPath(filename);
        
        if (!IsPathAllowed(fullPath))
        {
            SendError(session, "Access denied");
            return;
        }
        
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            SendResponse(session, "OK");
        }
        else
        {
            SendError(session, "File not found");
        }
    }
    
    private void HandleDirectory(FileSession session, string[] parts)
    {
        string pattern = parts.Length > 1 ? parts[1] : "*";
        string searchPath = GetFullPath(pattern);
        string directory = Path.GetDirectoryName(searchPath) ?? _rootDirectory;
        string searchPattern = Path.GetFileName(searchPath);
        
        if (!IsPathAllowed(directory))
        {
            SendError(session, "Access denied");
            return;
        }
        
        var files = Directory.GetFiles(directory, searchPattern);
        var sb = new StringBuilder();
        
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            string relativePath = Path.GetRelativePath(_rootDirectory, file);
            sb.AppendLine($"{relativePath,-40} {info.Length,10} {info.LastWriteTime:yyyy-MM-dd HH:mm}");
        }
        
        if (sb.Length == 0)
        {
            SendResponse(session, "No files found");
        }
        else
        {
            session.Connection.Send(Encoding.ASCII.GetBytes(sb.ToString()));
        }
    }
    
    private void SendResponse(FileSession session, string message)
    {
        byte[] data = Encoding.ASCII.GetBytes(message + "\r\n");
        session.Connection.Send(data);
    }
    
    private void SendError(FileSession session, string message)
    {
        SendResponse(session, $"ERROR: {message}");
    }
    
    private string GetFullPath(string filename)
    {
        // Convert filename to safe path
        string safeName = filename.Replace(':', Path.DirectorySeparatorChar);
        safeName = safeName.Replace(';', Path.DirectorySeparatorChar);
        
        return Path.GetFullPath(Path.Combine(_rootDirectory, safeName));
    }
    
    private bool IsPathAllowed(string fullPath)
    {
        // Ensure path is within root directory (prevent directory traversal)
        string normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase);
    }
    
    private void CloseSession(FileSession session)
    {
        session.CurrentFile?.Close();
        _sessions.Remove(session.Connection.LocalIndex);
    }
    
    /// <summary>
    /// FILE session state
    /// </summary>
    private class FileSession
    {
        public ChaosConnection Connection { get; set; } = null!;
        public string RootDirectory { get; set; } = string.Empty;
        public FileStream? CurrentFile { get; set; }
        public string? CurrentFilename { get; set; }
    }
}
