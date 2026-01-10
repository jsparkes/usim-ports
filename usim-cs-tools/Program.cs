// Program.cs - USIM Tools main entry point
// Disk maker, MCR reader, and other utilities

using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace UsimTools;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("USIM Tools - Utilities for MIT CADR Simulator");
        
        // diskmaker command
        var diskmakerCommand = new Command("diskmaker", "Create disk images for USIM");
        var sizeOption = new Option<int>("--size", () => 1000, "Size in MB");
        var outputOption = new Option<string>("--output", "Output filename") { IsRequired = true };
        diskmakerCommand.AddOption(sizeOption);
        diskmakerCommand.AddOption(outputOption);
        diskmakerCommand.SetHandler((int size, string output) =>
        {
            DiskMaker.CreateDisk(output, size);
        }, sizeOption, outputOption);
        
        // readmcr command
        var readmcrCommand = new Command("readmcr", "Read and display MCR (microcode) files");
        var inputOption = new Option<string>("--input", "Input MCR file") { IsRequired = true };
        var verboseOption = new Option<bool>("--verbose", () => false, "Verbose output");
        readmcrCommand.AddOption(inputOption);
        readmcrCommand.AddOption(verboseOption);
        readmcrCommand.SetHandler((string input, bool verbose) =>
        {
            McrReader.ReadMcr(input, verbose);
        }, inputOption, verboseOption);
        
        // showmcr command
        var showmcrCommand = new Command("showmcr", "Display MCR file contents with disassembly");
        var showInputOption = new Option<string>("--input", "Input MCR file") { IsRequired = true };
        var showVerboseOption = new Option<bool>("--verbose", () => false, "Verbose output");
        var disasmOption = new Option<bool>("--disasm", () => true, "Show disassembly");
        showmcrCommand.AddOption(showInputOption);
        showmcrCommand.AddOption(showVerboseOption);
        showmcrCommand.AddOption(disasmOption);
        showmcrCommand.SetHandler((string input, bool verbose, bool disasm) =>
        {
            McrReader.ShowMcr(input, verbose, disasm);
        }, showInputOption, showVerboseOption, disasmOption);
        
        // lod command
        var lodCommand = new Command("lod", "Load files into disk images");
        var lodDiskOption = new Option<string>("--disk", "Disk image file") { IsRequired = true };
        var lodFileOption = new Option<string>("--file", "File to load") { IsRequired = true };
        var lodAddressOption = new Option<uint>("--address", () => 0, "Load address");
        lodCommand.AddOption(lodDiskOption);
        lodCommand.AddOption(lodFileOption);
        lodCommand.AddOption(lodAddressOption);
        lodCommand.SetHandler((string disk, string file, uint address) =>
        {
            Loader.LoadFile(disk, file, address);
        }, lodDiskOption, lodFileOption, lodAddressOption);
        
        // dump command
        var dumpCommand = new Command("dump", "Dump memory or disk contents");
        var dumpSourceOption = new Option<string>("--source", "Source file") { IsRequired = true };
        var dumpStartOption = new Option<uint>("--start", () => 0, "Start address");
        var dumpLengthOption = new Option<uint>("--length", () => 256, "Length in words");
        var dumpFormatOption = new Option<string>("--format", () => "hex", "Output format (hex, binary, text)");
        dumpCommand.AddOption(dumpSourceOption);
        dumpCommand.AddOption(dumpStartOption);
        dumpCommand.AddOption(dumpLengthOption);
        dumpCommand.AddOption(dumpFormatOption);
        dumpCommand.SetHandler((string source, uint start, uint length, string format) =>
        {
            Dumper.DumpFile(source, start, length, format);
        }, dumpSourceOption, dumpStartOption, dumpLengthOption, dumpFormatOption);
        
        rootCommand.AddCommand(diskmakerCommand);
        rootCommand.AddCommand(readmcrCommand);
        rootCommand.AddCommand(showmcrCommand);
        rootCommand.AddCommand(lodCommand);
        rootCommand.AddCommand(dumpCommand);
        
        return await rootCommand.InvokeAsync(args);
    }
}
