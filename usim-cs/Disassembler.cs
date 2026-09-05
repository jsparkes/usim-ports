// Disassembler.cs - Microcode instruction disassembler
// Real field extraction and mnemonic tables, ported from the layouts
// established in Phases 2/3/6/7 (usim-cs/UCode.cs's Alu()/Jmp()/Dsp()/
// Byt()), replacing the Phase 1-7 placeholder.

using System;

namespace Usim;

public static class Disassembler
{
    /// <summary>
    /// PROM/microcode symbol table (Phase 8B), loaded by
    /// MachineControl.LoadSystemFiles() when a .sym file is present.
    /// Null until loaded. Established here so MachineControl has
    /// somewhere to assign the loaded table; consumed by a later phase
    /// to resolve symbolic A/M/I/D-memory operand names.
    ///
    /// The real C (usim/ucode.c, usim/usim.c) keeps sym_prom and sym_mcr as
    /// two separate tables, selected by a pc_imem/promdisabled flag -- this
    /// port merges both promh.sym and ucadr.sym into ONE table here, since
    /// every current call site (MicrocodeDebugger.cs's two DisassembleInst2
    /// callers) always passes pcImem=true (always wants mcr/ucadr
    /// semantics), and no helper yet threads pcImem into symbol lookup at
    /// all. If a pcImem=false (PROM) lookup path is ever added, or if
    /// promh.sym and ucadr.sym ever define colliding (type,value) keys with
    /// different intended names, this merge must be split into two tables
    /// selected by pcImem.
    /// </summary>
    public static SymbolTable? Symbols { get; set; }

    private static ulong Ir(ulong word, int pos, int len) => (word >> pos) & ((1UL << len) - 1);

    public static string DisassemblePC(uint pc) => DisassemblePC2(pc, false);

    public static string DisassemblePC2(uint pc, bool pcImem)
    {
        return $"[PC {pc:X4} ({(pcImem ? "IMEM" : "PROM")}) -- no instruction word available to this overload; use DisassembleInst2]";
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

        // Real mnemonics ported from usim/udiss.c:308-412's alu_desc() --
        // verified against that file directly. Case 5 (SETA) prints NO
        // mnemonic at all in the real disassembler either (not a gap).
        // Unnamed codes fall back to "ALU-FUNCTION-<octal>", matching
        // udiss.c's own "ALU-FUNCTION-%o" (octal, not decimal).
        string opName = aluop switch
        {
            0 => "SETZ", 1 => "AND", 2 => "ANDCA", 3 => "SETM", 4 => "ANDCM",
            5 => "", 6 => "XOR", 7 => "IOR", 8 => "ANDCB", 9 => "EQV",
            10 => "SETCA", 11 => "ORCA", 12 => "SETCM", 13 => "ORCM",
            14 => "ORCB", 15 => "SETO",
            22 => "SUB", 25 => "ADD", 28 => "INCM", 31 => "LSHM",
            32 => "MUL", 33 => "DIV", 37 => "DIVRC", 41 => "DIVFS",
            _ => $"ALU-FUNCTION-{Convert.ToString((int)aluop, 8)}",
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

        // Faithful, disassembly-only port of usim/udiss.c:465-570's
        // type_jump_condition() -- kept as a separate helper below since
        // it re-slices the same 10 raw bits with a different bit
        // combination than UCode.CheckJumpCondition()'s execution-time
        // logic. flags= above (p/r/n/invertSense) is left as-is even
        // though TypeJumpCondition's own selector/mode text conveys the
        // same bits in named form -- TestJumpInstructionDecodesTargetAndFlags
        // (existing, pre-Phase-8b) depends on the literal "flags=P " display.
        string cond = TypeJumpCondition(instruction);

        uint mf = (uint)Ir(instruction, 10, 2);
        string mfStr = mf == 0 ? "" : $" MF-{Convert.ToString((int)mf, 8)}";

        return $"[JUMP target={target:X4} flags={flags} cond={cond}{mfStr} raw=0x{instruction:X12}]";
    }

    /// <summary>
    /// Faithful, disassembly-only port of usim/udiss.c:465-570's
    /// type_jump_condition(load_byte(u,0,10)) -- operates on the SAME 10
    /// raw bits Jmp()/CheckJumpCondition() already read via Ir(), but
    /// recombines them differently (bit6 as a sign bit over bits0-2,
    /// not Ir(0,4)'s contiguous 4 bits) specifically so the disassembler
    /// can print pre-negated mnemonics from a flat 16-entry table. Do
    /// NOT call UCode.CheckJumpCondition() here -- that's execution
    /// logic with side effects (it rotates MData) and uses a different
    /// bit combination for its own condition code.
    /// </summary>
    private static string TypeJumpCondition(ulong instruction)
    {
        bool p = Ir(instruction, 8, 1) != 0;
        bool r = Ir(instruction, 9, 1) != 0;
        string selector = (p, r) switch
        {
            (false, false) => "JUMP",
            (true, false) => "CALL",
            (false, true) => "POPJ",
            (true, true) => "CALL-POPJ-??",
        };

        bool xctNext = Ir(instruction, 7, 1) == 0; // bit7==0 -> "-XCT-NEXT"
        string modeText;

        if (Ir(instruction, 5, 1) == 0)
        {
            // Bit-test/rotate mode.
            string setClear = Ir(instruction, 6, 1) == 0 ? "Set" : "Clear";
            uint rot = (uint)Ir(instruction, 0, 5);
            uint reflected = rot == 0 ? 0 : 32 - rot;
            // usim/udiss.c:492 prints this with "%o" (octal), matching
            // the real disassembler's octal convention throughout --
            // NOT decimal.
            modeText = $"-IF-BIT-{setClear}{(xctNext ? "-XCT-NEXT" : "")} (Byte-field 1 {Convert.ToString((int)reflected, 8)})";
        }
        else
        {
            // Condition-code mode: bit6 is a sign bit over bits0-2 --
            // NOT the same combination as Ir(0,4).
            bool bit6 = Ir(instruction, 6, 1) != 0;
            int rawCond = (int)Ir(instruction, 0, 3);
            int cond = bit6 ? rawCond + 8 : rawCond;

            string[] tem =
            {
                "T", "-LESS-THAN", "-LESS-OR-EQUAL", "-EQUAL",
                "-IF-PAGE-FAULT", "-IF-PAGE-FAULT-OR-INTERRUPT", "-IF-SEQUENCE-BREAK", "NIL",
                "T", "-GREATER-OR-EQUAL", "-GREATER-THAN", "-NOT-EQUAL",
                "-IF-NO-PAGE-FAULT", "-IF-NO-PAGE-FAULT-OR-INTERRUPT", "-IF-NO-SEQUENCE-BREAK", "-NEVER",
            };
            string t = tem[cond];

            if (t == "T")
            {
                string inverted = !bit6 ? "(Inverted)" : "";
                modeText = $"JUMP-CONDITION {Convert.ToString(rawCond, 8)}{(xctNext ? "-XCT-NEXT" : "")}{inverted}";
            }
            else if (t == "NIL")
            {
                modeText = xctNext ? "-XCT-NEXT" : "";
            }
            else
            {
                modeText = $"{t}{(xctNext ? "-XCT-NEXT" : "")}";
            }
        }

        return $"{selector}{modeText}";
    }

    private static string DisassembleDispatch(ulong instruction)
    {
        uint dispAddr = (uint)Ir(instruction, 12, 11);
        uint map = (uint)Ir(instruction, 8, 2);
        uint dispConst = (uint)Ir(instruction, 32, 10);

        // Real names ported from usim/udiss.c:632-645's dsp_desc().
        string mapStr = map switch
        {
            1 => " MAP-14",
            2 => " MAP-15",
            3 => " MAP-BOTH-14-AND-15",
            _ => "",
        };
        uint mf = (uint)Ir(instruction, 10, 2); // Ir(10,2) -- was already extracted as "sel" in this method; reuse that local instead of re-declaring if it already exists under that name
        string mfStr = mf == 0 ? "" : $" MF-{Convert.ToString((int)mf, 8)}";

        // disp_const is NOT a defmics[]-style function number -- see
        // usim/udiss.c:600-606's dsp_const_desc(), which prints this
        // field as a plain address/NUMBER. defmics[] is used only by
        // usim/unfasl*.c (macrocode FASL decoding), never by the
        // microcode disassembler -- DefMics.cs is kept for a possible
        // future unfasl port, not wired into live disassembly output.
        string dispConstStr = dispConst == 0 ? "" : $" ({Convert.ToString((int)dispConst, 8)})";

        return $"[DISPATCH addr={dispAddr:X3}{mapStr}{mfStr}{dispConstStr} raw=0x{instruction:X12}]";
    }

    private static string DisassembleByte(ulong instruction)
    {
        uint dest = (uint)Ir(instruction, 14, 12);
        uint mrSrBits = (uint)Ir(instruction, 12, 2);
        uint widthm1 = (uint)Ir(instruction, 5, 5);
        uint pos = (uint)Ir(instruction, 0, 5);

        // Real mnemonics ported from usim/udiss.c:664-706's byt_desc().
        string opName = mrSrBits switch
        {
            1 => "LDB",
            2 => "SELECTIVE-DEPOSIT",
            3 => "DPB",
            _ => "BYTE-OPERATION-0",
        };

        uint mf = (uint)Ir(instruction, 10, 2);
        string mfStr = mf == 0 ? "" : $" MF-{Convert.ToString((int)mf, 8)}";

        return $"[BYTE {opName} dest={dest:X3} width={widthm1 + 1} pos={pos}{mfStr} raw=0x{instruction:X12}]";
    }
}
