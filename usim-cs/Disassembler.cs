// Disassembler.cs - Microcode disassembly
// Converted from udiss.h and udiss.c
//
// NOTE: This is a placeholder pending Phase 8 of the microcode engine port
// (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md). Real
// mnemonic-level disassembly needs the ALU/jump/dispatch/byte semantics from
// Phases 2/3/6/7 to have names to print.

namespace Usim;

/// <summary>
/// Microcode disassembler
/// </summary>
public static class Disassembler
{
    /// <summary>
    /// Disassemble instruction at PC
    /// </summary>
    public static string DisassemblePC(uint pc)
    {
        return DisassemblePC2(pc, false);
    }

    /// <summary>
    /// Disassemble instruction at PC with memory selection
    /// </summary>
    public static string DisassemblePC2(uint pc, bool pcImem)
    {
        return $"[PC {pc:X4} ({(pcImem ? "IMEM" : "PROM")}): disassembly pending Phase 8]";
    }

    /// <summary>
    /// Disassemble a microcode instruction
    /// </summary>
    public static string DisassembleInst(ulong instruction)
    {
        return DisassembleInst2(instruction, false);
    }

    /// <summary>
    /// Disassemble a microcode instruction with memory selection
    /// </summary>
    public static string DisassembleInst2(ulong instruction, bool pcImem)
    {
        if (instruction == 0)
            return "[NOP]";

        uint op = (uint)((instruction >> 43) & 0x3);
        string[] opNames = { "ALU", "JUMP", "DISPATCH", "BYTE" };
        return $"[{opNames[op]} raw=0x{instruction:X12}] (disassembly pending Phase 8)";
    }
}
