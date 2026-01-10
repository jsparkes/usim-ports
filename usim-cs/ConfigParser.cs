// ConfigParser.cs - Configuration file parser
// Converted from ini.h and ini.c

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Usim;

/// <summary>
/// INI-style configuration file parser
/// </summary>
public class ConfigParser
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new();
    
    /// <summary>
    /// Load configuration from file
    /// </summary>
    public bool Load(string filename)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"Configuration file not found: {filename}");
            return false;
        }
        
        try
        {
            var lines = File.ReadAllLines(filename);
            string currentSection = "General";
            
            if (!_sections.ContainsKey(currentSection))
            {
                _sections[currentSection] = new Dictionary<string, string>();
            }
            
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                    continue;
                
                // Check for section header
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (!_sections.ContainsKey(currentSection))
                    {
                        _sections[currentSection] = new Dictionary<string, string>();
                    }
                    continue;
                }
                
                // Parse key=value
                var parts = trimmed.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    
                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    
                    _sections[currentSection][key] = value;
                }
            }
            
            Console.WriteLine($"Loaded configuration from {filename}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading configuration: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get string value
    /// </summary>
    public string GetString(string section, string key, string defaultValue = "")
    {
        if (_sections.TryGetValue(section, out var sectionDict))
        {
            if (sectionDict.TryGetValue(key, out var value))
            {
                return value;
            }
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Get integer value
    /// </summary>
    public int GetInt(string section, string key, int defaultValue = 0)
    {
        string value = GetString(section, key);
        if (int.TryParse(value, out int result))
        {
            return result;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Get boolean value
    /// </summary>
    public bool GetBool(string section, string key, bool defaultValue = false)
    {
        string value = GetString(section, key).ToLowerInvariant();
        
        if (value == "true" || value == "yes" || value == "1" || value == "on")
            return true;
        if (value == "false" || value == "no" || value == "0" || value == "off")
            return false;
            
        return defaultValue;
    }
    
    /// <summary>
    /// Set string value
    /// </summary>
    public void SetString(string section, string key, string value)
    {
        if (!_sections.ContainsKey(section))
        {
            _sections[section] = new Dictionary<string, string>();
        }
        _sections[section][key] = value;
    }
    
    /// <summary>
    /// Set integer value
    /// </summary>
    public void SetInt(string section, string key, int value)
    {
        SetString(section, key, value.ToString());
    }
    
    /// <summary>
    /// Set boolean value
    /// </summary>
    public void SetBool(string section, string key, bool value)
    {
        SetString(section, key, value ? "true" : "false");
    }
    
    /// <summary>
    /// Check if key exists
    /// </summary>
    public bool HasKey(string section, string key)
    {
        if (_sections.TryGetValue(section, out var sectionDict))
        {
            return sectionDict.ContainsKey(key);
        }
        return false;
    }
    
    /// <summary>
    /// Get all keys in a section
    /// </summary>
    public IEnumerable<string> GetKeys(string section)
    {
        if (_sections.TryGetValue(section, out var sectionDict))
        {
            return sectionDict.Keys;
        }
        return Enumerable.Empty<string>();
    }
    
    /// <summary>
    /// Get all sections
    /// </summary>
    public IEnumerable<string> GetSections()
    {
        return _sections.Keys;
    }
    
    /// <summary>
    /// Save configuration to file
    /// </summary>
    public bool Save(string filename)
    {
        try
        {
            using var writer = new StreamWriter(filename);
            
            foreach (var section in _sections)
            {
                writer.WriteLine($"[{section.Key}]");
                
                foreach (var kvp in section.Value)
                {
                    writer.WriteLine($"{kvp.Key} = {kvp.Value}");
                }
                
                writer.WriteLine();
            }
            
            Console.WriteLine($"Saved configuration to {filename}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving configuration: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get double value
    /// </summary>
    public double GetDouble(string section, string key, double defaultValue = 0.0)
    {
        string value = GetString(section, key);
        if (double.TryParse(value, out double result))
        {
            return result;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Set double value
    /// </summary>
    public void SetDouble(string section, string key, double value)
    {
        SetString(section, key, value.ToString("F3"));
    }
    
    /// <summary>
    /// Get long value
    /// </summary>
    public long GetLong(string section, string key, long defaultValue = 0)
    {
        string value = GetString(section, key);
        if (long.TryParse(value, out long result))
        {
            return result;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Get hex value (supports 0x prefix)
    /// </summary>
    public uint GetHex(string section, string key, uint defaultValue = 0)
    {
        string value = GetString(section, key);
        if (string.IsNullOrEmpty(value))
            return defaultValue;
            
        // Remove 0x prefix if present
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(2);
        }
        
        if (uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out uint result))
        {
            return result;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Set hex value (with 0x prefix)
    /// </summary>
    public void SetHex(string section, string key, uint value)
    {
        SetString(section, key, $"0x{value:X8}");
    }
    
    /// <summary>
    /// Get path value (expands environment variables and relative paths)
    /// </summary>
    public string GetPath(string section, string key, string defaultValue = "")
    {
        string value = GetString(section, key, defaultValue);
        if (string.IsNullOrEmpty(value))
            return defaultValue;
            
        // Expand environment variables
        value = Environment.ExpandEnvironmentVariables(value);
        
        // Convert to absolute path if relative
        if (!Path.IsPathRooted(value))
        {
            value = Path.GetFullPath(value);
        }
        
        return value;
    }
    
    /// <summary>
    /// Get enum value
    /// </summary>
    public T GetEnum<T>(string section, string key, T defaultValue) where T : struct, Enum
    {
        string value = GetString(section, key);
        if (Enum.TryParse<T>(value, true, out T result))
        {
            return result;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Set enum value
    /// </summary>
    public void SetEnum<T>(string section, string key, T value) where T : Enum
    {
        SetString(section, key, value.ToString());
    }
    
    /// <summary>
    /// Get string array (comma-separated)
    /// </summary>
    public string[] GetStringArray(string section, string key, string[]? defaultValue = null)
    {
        string value = GetString(section, key);
        if (string.IsNullOrEmpty(value))
            return defaultValue ?? Array.Empty<string>();
            
        return value.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
    
    /// <summary>
    /// Set string array (comma-separated)
    /// </summary>
    public void SetStringArray(string section, string key, string[] values)
    {
        SetString(section, key, string.Join(", ", values));
    }
    
    /// <summary>
    /// Remove a key from a section
    /// </summary>
    public bool RemoveKey(string section, string key)
    {
        if (_sections.TryGetValue(section, out var sectionDict))
        {
            return sectionDict.Remove(key);
        }
        return false;
    }
    
    /// <summary>
    /// Remove an entire section
    /// </summary>
    public bool RemoveSection(string section)
    {
        return _sections.Remove(section);
    }
    
    /// <summary>
    /// Clear all configuration
    /// </summary>
    public void Clear()
    {
        _sections.Clear();
    }
    
    /// <summary>
    /// Get count of sections
    /// </summary>
    public int SectionCount => _sections.Count;
    
    /// <summary>
    /// Get count of keys in a section
    /// </summary>
    public int GetKeyCount(string section)
    {
        if (_sections.TryGetValue(section, out var sectionDict))
        {
            return sectionDict.Count;
        }
        return 0;
    }
    
    /// <summary>
    /// Dump configuration to console (for debugging)
    /// </summary>
    public void Dump()
    {
        Console.WriteLine("=== Configuration ===");
        foreach (var section in _sections)
        {
            Console.WriteLine($"[{section.Key}]");
            foreach (var kvp in section.Value)
            {
                Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
            }
            Console.WriteLine();
        }
    }
}
