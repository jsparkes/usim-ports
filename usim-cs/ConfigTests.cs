// ConfigTests.cs - Configuration system tests

using System;
using System.IO;
using System.Linq;

namespace Usim;

/// <summary>
/// Test suite for configuration system
/// </summary>
public static class ConfigTests
{
    /// <summary>
    /// Run all configuration tests
    /// </summary>
    public static void RunAllTests()
    {
        Console.WriteLine("=== Configuration System Test Suite ===\n");
        
        int passed = 0;
        int failed = 0;
        
        if (TestBasicReadWrite()) passed++; else failed++;
        if (TestDataTypes()) passed++; else failed++;
        if (TestHexValues()) passed++; else failed++;
        if (TestEnumValues()) passed++; else failed++;
        if (TestArrayValues()) passed++; else failed++;
        if (TestPathExpansion()) passed++; else failed++;
        if (TestSectionOperations()) passed++; else failed++;
        if (TestConfigManager()) passed++; else failed++;
        if (TestApplyConfigurationParsesDiskUnits()) passed++; else failed++;
        
        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
        
        if (failed == 0)
        {
            Console.WriteLine("\n? All tests passed!");
        }
        else
        {
            Console.WriteLine($"\n? {failed} test(s) failed");
        }
    }
    
    private static bool TestBasicReadWrite()
    {
        Console.WriteLine("Test: Basic Read/Write");
        try
        {
            var config = new ConfigParser();
            
            // Set values
            config.SetString("Test", "name", "CADR");
            config.SetInt("Test", "version", 300);
            config.SetBool("Test", "enabled", true);
            
            // Read values
            Assert(config.GetString("Test", "name") == "CADR", "String read/write");
            Assert(config.GetInt("Test", "version") == 300, "Int read/write");
            Assert(config.GetBool("Test", "enabled") == true, "Bool read/write");
            
            // Test defaults
            Assert(config.GetString("Test", "missing", "default") == "default", "String default");
            Assert(config.GetInt("Test", "missing", 42) == 42, "Int default");
            Assert(config.GetBool("Test", "missing", false) == false, "Bool default");
            
            Console.WriteLine("  ? Basic Read/Write tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Basic Read/Write tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestDataTypes()
    {
        Console.WriteLine("Test: Data Types");
        try
        {
            var config = new ConfigParser();
            
            // Double
            config.SetDouble("Test", "scale", 1.5);
            Assert(Math.Abs(config.GetDouble("Test", "scale") - 1.5) < 0.001, "Double read/write");
            
            // Long
            config.SetString("Test", "bignum", "9999999999");
            Assert(config.GetLong("Test", "bignum") == 9999999999L, "Long read");
            
            Console.WriteLine("  ? Data Types tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Data Types tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestHexValues()
    {
        Console.WriteLine("Test: Hex Values");
        try
        {
            var config = new ConfigParser();
            
            // Write hex
            config.SetHex("Test", "address", 0xDEADBEEF);
            Assert(config.GetString("Test", "address") == "0xDEADBEEF", "Hex format");
            
            // Read hex with 0x prefix
            config.SetString("Test", "addr1", "0x12345678");
            Assert(config.GetHex("Test", "addr1") == 0x12345678, "Hex read with 0x");
            
            // Read hex without 0x prefix
            config.SetString("Test", "addr2", "ABCDEF00");
            Assert(config.GetHex("Test", "addr2") == 0xABCDEF00, "Hex read without 0x");
            
            Console.WriteLine("  ? Hex Values tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Hex Values tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestEnumValues()
    {
        Console.WriteLine("Test: Enum Values");
        try
        {
            var config = new ConfigParser();
            
            // Write enum
            config.SetEnum("Test", "state", TestTraceLevel.Second);
            Assert(config.GetString("Test", "state") == "Second", "Enum format");
            
            // Read enum
            var value = config.GetEnum("Test", "state", TestTraceLevel.None);
            Assert(value == TestTraceLevel.Second, "Enum read");
            
            // Case insensitive
            config.SetString("Test", "state2", "third");
            value = config.GetEnum("Test", "state2", TestTraceLevel.None);
            Assert(value == TestTraceLevel.Third, "Enum case insensitive");
            
            Console.WriteLine("  ? Enum Values tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Enum Values tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestArrayValues()
    {
        Console.WriteLine("Test: Array Values");
        try
        {
            var config = new ConfigParser();
            
            // Write array
            string[] values = new[] { "one", "two", "three" };
            config.SetStringArray("Test", "items", values);
            
            // Read array
            string[] result = config.GetStringArray("Test", "items");
            Assert(result.Length == 3, "Array length");
            Assert(result[0] == "one", "Array element 0");
            Assert(result[1] == "two", "Array element 1");
            Assert(result[2] == "three", "Array element 2");
            
            // Empty array
            result = config.GetStringArray("Test", "missing");
            Assert(result.Length == 0, "Empty array");
            
            Console.WriteLine("  ? Array Values tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Array Values tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestPathExpansion()
    {
        Console.WriteLine("Test: Path Expansion");
        try
        {
            var config = new ConfigParser();
            
            // Set relative path
            config.SetString("Test", "path", "./data");
            
            // Get expanded path
            string path = config.GetPath("Test", "path");
            Assert(Path.IsPathRooted(path), "Path is absolute");
            
            Console.WriteLine("  ? Path Expansion tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Path Expansion tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestSectionOperations()
    {
        Console.WriteLine("Test: Section Operations");
        try
        {
            var config = new ConfigParser();
            
            // Add sections
            config.SetString("Section1", "key1", "value1");
            config.SetString("Section2", "key2", "value2");
            config.SetString("Section3", "key3", "value3");
            
            // Count sections
            Assert(config.SectionCount == 3, "Section count");
            
            // Check if key exists
            Assert(config.HasKey("Section1", "key1"), "Key exists");
            Assert(!config.HasKey("Section1", "missing"), "Key doesn't exist");
            
            // Get keys
            var keys = config.GetKeys("Section1");
            Assert(keys.Any(), "Has keys");
            
            // Remove key
            bool removed = config.RemoveKey("Section1", "key1");
            Assert(removed, "Key removed");
            Assert(!config.HasKey("Section1", "key1"), "Key no longer exists");
            
            // Remove section
            removed = config.RemoveSection("Section2");
            Assert(removed, "Section removed");
            Assert(config.SectionCount == 2, "Section count after remove");
            
            // Clear all
            config.Clear();
            Assert(config.SectionCount == 0, "All cleared");
            
            Console.WriteLine("  ? Section Operations tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Section Operations tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestConfigManager()
    {
        Console.WriteLine("Test: Config Manager");
        try
        {
            var manager = new ConfigManager();
            
            // Set some test configuration
            manager.Config.SetInt("Display", "width", 1024);
            manager.Config.SetInt("Display", "height", 808);
            manager.Config.SetDouble("Display", "scale", 2.0);
            manager.Config.SetBool("Display", "color_tv", true);
            
            // Get display config
            var displayConfig = manager.GetDisplayConfig();
            Assert(displayConfig.Width == 1024, "Display width");
            Assert(displayConfig.Height == 808, "Display height");
            Assert(Math.Abs(displayConfig.Scale - 2.0) < 0.001, "Display scale");
            Assert(displayConfig.ColorTv == true, "Color TV");
            
            // Test memory config
            manager.Config.SetString("Memory", "main_memory_size", "0x2000000");
            var memoryConfig = manager.GetMemoryConfig();
            Assert(memoryConfig.MainMemorySize == 0x2000000, "Memory size");
            
            // Validation should pass
            Assert(manager.Validate(), "Validation passed");
            
            Console.WriteLine("  ? Config Manager tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Config Manager tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    private static bool TestApplyConfigurationParsesDiskUnits()
    {
        Console.WriteLine("Test: ApplyConfiguration parses the [disk] section into UsimState.DiskUnits");
        try
        {
            var parser = new ConfigParser();
            // Simulate a loaded config with a [disk] section, two units configured,
            // one bare-filename (no comma, type defaults to T-300), rest absent.
            parser.SetString("disk", "disk0", "T-80,/path/to/unit0.img");
            parser.SetString("disk", "disk3", "/path/to/unit3-bare.img");

            Program.ApplyConfiguration(parser);

            Assert(UsimState.DiskUnits.Length == 2, $"exactly 2 units configured, got {UsimState.DiskUnits.Length}");

            var unit0 = Array.Find(UsimState.DiskUnits, u => u.Unit == 0);
            Assert(unit0.TypeName == "T-80", $"unit 0 type is T-80, got {unit0.TypeName}");
            Assert(unit0.Filename == "/path/to/unit0.img", $"unit 0 filename correct, got {unit0.Filename}");

            var unit3 = Array.Find(UsimState.DiskUnits, u => u.Unit == 3);
            Assert(unit3.TypeName == "T-300", $"unit 3 (bare filename) defaults to T-300, got {unit3.TypeName}");
            Assert(unit3.Filename == "/path/to/unit3-bare.img", $"unit 3 filename correct, got {unit3.Filename}");

            Assert(Array.Find(UsimState.DiskUnits, u => u.Unit == 1).Filename == null,
                "unit 1 (never configured) is absent from the array");

            Console.WriteLine("  ApplyConfiguration disk-units test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ApplyConfiguration disk-units test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: {message}");
        }
    }
    
    // Test enum for testing
    private enum TestTraceLevel { None, First, Second, Third }
}
