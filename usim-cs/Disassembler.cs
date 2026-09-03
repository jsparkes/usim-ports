// Disassembler.cs - Microcode instruction disassembler
// Real field extraction and mnemonic tables, ported from the layouts
// established in Phases 2/3/6/7 (usim-cs/UCode.cs's Alu()/Jmp()/Dsp()/
// Byt()), replacing the Phase 1-7 placeholder.

using System;

namespace Usim;

public static class Disassembler
{
    private static ulong Ir(ulong word, int pos, int len) => (word >> pos) & ((1UL << len) - 1);

    // LogiOps real names (UCode.cs LogiOps(), codes 0-15).
    private static readonly string[] LogicOpNames =
    {
        "SETZ", "AND", "ANDCA", "SETM", "ANDCM", "SETA", "XOR", "IOR",
        "NOR", "EQV", "SETCA", "ORCA", "SETCM", "ORCM", "ORCB", "SETO"
    };

    public static string DisassemblePC(uint pc) => DisassemblePC2(pc, false);

    public static string DisassemblePC2(uint pc, bool pcImem)
    {
        return $"[PC {pc:X4} ({(pcImem ? "IMEM" : "PROM")})]";
    }

    public static string DisassembleInst(ulong instruction) => DisassembleInst2(instruction, false);

    public static string DisassembleInst2(ulong instruction, bool pcImem)
    {
        if (instruction == 0) return "[NOP]";

        uint op = (uint)Ir(instruction, 43, 2);
        return op switch
        {
            0 => DisassembleAlu(instruction),
            1 => DisassembleJump(instruction),
            2 => DisassembleDispatch(instruction),
            3 => DisassembleByte(instruction),
            _ => $"[UNKNOWN raw=0x{instruction:X12}]",
        };
    }

    private static string DisassembleAlu(ulong instruction)
    {
        uint dest = (uint)Ir(instruction, 14, 12);
        uint aluop = (uint)Ir(instruction, 3, 6);

        string opName = aluop switch
        {
            <= 15 => LogicOpNames[aluop],
            >= 16 and <= 31 => $"ARITH-{Convert.ToString(aluop, 8)}", // usim/uexec.c's arith_ops() names only a couple of these (SUB=026, ADD=031) -- most are genuinely unnamed in the real source
            32 => "MULTIPLY-STEP",
            33 => "DIVIDE-STEP",
            37 => "REMAINDER-CORRECTION",
            41 => "INITIAL-DIVIDE-STEP",
            _ => $"ALU-{aluop}",
        };

        return $"[ALU {opName} dest={dest:X3} raw=0x{instruction:X12}]";
    }

    private static string DisassembleJump(ulong instruction)
    {
        uint target = (uint)Ir(instruction, 12, 14);
        bool r = Ir(instruction, 9, 1) != 0;
        bool p = Ir(instruction, 8, 1) != 0;
        bool n = Ir(instruction, 7, 1) != 0;
        bool invertSense = Ir(instruction, 6, 1) != 0;

        string flags = (p ? "P" : "") + (r ? "R" : "") + (n ? "N" : "") + (invertSense ? "~" : "");

        string cond;
        if (Ir(instruction, 5, 1) == 0)
        {
            cond = $"ROT{Ir(instruction, 0, 5)}";
        }
        else
        {
            cond = Ir(instruction, 0, 4) switch
            {
                1 => "M<A", 2 => "M<=A", 3 => "M=A", 4 => "PAGE-FAULT",
                5 => "PAGE-FAULT-OR-INT", 6 => "PAGE-FAULT-OR-INT-OR-CLOCK", 7 => "ALWAYS",
                _ => "UNKNOWN-COND",
            };
        }

        return $"[JUMP target={target:X4} flags={flags} cond={cond} raw=0x{instruction:X12}]";
    }

    private static string DisassembleDispatch(ulong instruction)
    {
        uint dispAddr = (uint)Ir(instruction, 12, 11);
        uint map = (uint)Ir(instruction, 8, 2);
        uint sel = (uint)Ir(instruction, 10, 2);
        uint dispConst = (uint)Ir(instruction, 32, 10);

        string dispConstStr = DefMics.Lookup((int)dispConst) is string name ? $"{dispConst}[{name}]" : dispConst.ToString();

        return $"[DISPATCH addr={dispAddr:X3} map={map} sel={sel} const={dispConstStr} raw=0x{instruction:X12}]";
    }

    private static string DisassembleByte(ulong instruction)
    {
        uint dest = (uint)Ir(instruction, 14, 12);
        uint mrSrBits = (uint)Ir(instruction, 12, 2);
        uint widthm1 = (uint)Ir(instruction, 5, 5);
        uint pos = (uint)Ir(instruction, 0, 5);
        bool byteMode = Ir(instruction, 10, 2) == 3;

        string opName = mrSrBits switch
        {
            0 => "NONE",
            1 => "LDB",
            2 => "SEL-DEP",
            3 => "DPB",
            _ => "UNKNOWN",
        };

        return $"[BYTE {opName} dest={dest:X3} width={widthm1 + 1} pos={pos}{(byteMode ? " byte-mode" : "")} raw=0x{instruction:X12}]";
    }
}
