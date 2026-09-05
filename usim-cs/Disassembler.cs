// Disassembler.cs - Microcode instruction disassembler
// Real field extraction and mnemonic tables, ported from the layouts
// established in Phases 2/3/6/7 (usim-cs/UCode.cs's Alu()/Jmp()/Dsp()/
// Byt()), replacing the Phase 1-7 placeholder.

using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Faithful port of usim/udiss.c's type_field()/c_or_d_adr_out()/
    /// a_or_m_adr_out(). NUMBER type never does a symbol lookup (plain
    /// octal always). IMem/DMem always print something (symbol name or
    /// plain octal). AMem/MMem print NOTHING at value 0 (elided
    /// entirely), otherwise the symbol name or "&lt;octal&gt;@A"/"&lt;octal&gt;@M"
    /// if no symbol is found.
    /// </summary>
    private static string TypeField(SymbolType type, ulong instruction, int pos, int len)
    {
        uint val = (uint)Ir(instruction, pos, len);
        if (type == SymbolType.Number)
        {
            return $"{Convert.ToString((int)val, 8)} ";
        }
        if (type == SymbolType.AMem || type == SymbolType.MMem)
        {
            if (val == 0) return "";
            string? name = Symbols?.FindByTypeValue(type, val);
            char suffix = type == SymbolType.AMem ? 'A' : 'M';
            return name != null ? $"{name} " : $"{Convert.ToString((int)val, 8)}@{suffix} ";
        }
        // IMem or DMem.
        string? lbl = Symbols?.FindByTypeValue(type, val);
        return lbl != null ? $"{lbl} " : $"{Convert.ToString((int)val, 8)} ";
    }

    /// <summary>
    /// Faithful port of usim/udiss.c:26-43's byte_field_out(). The "val"
    /// parameter there is a sub-extracted value re-sliced at bits 0-9;
    /// since both real call sites' sub-extractions start at bit 0 of the
    /// full instruction and byte_field_out only ever reads bits 0-9 of
    /// its "val", reading pos/len directly from the full instruction
    /// here is mathematically equivalent -- see this task's plan text.
    ///
    /// alwaysReflectMrot corresponds exactly to udiss.c's own
    /// always_reflect_mrot parameter: DISPATCH's call site
    /// (udiss.c:623) passes true (always reflects a nonzero pos), while
    /// BYTE's call site (udiss.c:687) passes false (reflects only when
    /// the separate mrot-check bit, Ir(instruction,12,2)==1, is set).
    /// These are NOT interchangeable -- do not hardcode unconditional
    /// reflection for both call sites.
    /// </summary>
    private static string ByteFieldOut(ulong instruction, bool alwaysReflectMrot, bool lengthIsMinusOne)
    {
        uint len = (uint)Ir(instruction, 5, 5);
        if (lengthIsMinusOne) len += 1;
        uint pos = (uint)Ir(instruction, 0, 5);
        if (pos != 0)
        {
            if (alwaysReflectMrot || Ir(instruction, 12, 2) == 1)
                pos = 32 - pos;
        }
        return $"(Byte-field {Convert.ToString((int)len, 8)} {Convert.ToString((int)pos, 8)}) ";
    }

    /// <summary>
    /// Joins non-empty tokens with exactly one space, trimming each
    /// token first -- this makes the join immune to whichever
    /// leading/trailing-space convention any individual field happens to
    /// use (some helpers here print a trailing space when non-empty,
    /// some historically used a leading space, some both), and
    /// guarantees no double spaces and no zero-separator gluing when
    /// concatenating a variable-length list of optional fields.
    /// </summary>
    private static string JoinTokens(params string[] tokens)
    {
        var nonEmpty = new List<string>();
        foreach (var token in tokens)
        {
            string trimmed = token.Trim();
            if (trimmed.Length > 0)
            {
                nonEmpty.Add(trimmed);
            }
        }
        return string.Join(" ", nonEmpty);
    }

    /// <summary>
    /// Faithful port of usim/udiss.c's m_source_desc(). m=0 -> plain
    /// MMEM type_field; m=1 -> one of 32 named special-register sources
    /// (fsource), verbatim from udiss.c:86-156.
    /// </summary>
    private static string MSourceDesc(ulong instruction)
    {
        if (Ir(instruction, 31, 1) == 0)
        {
            return TypeField(SymbolType.MMem, instruction, 26, 6);
        }
        uint fsource = (uint)Ir(instruction, 26, 5);
        string name = fsource switch
        {
            0 => "READ-I-ARG", 1 => "MICRO-STACK-PNTR-AND-DATA", 2 => "PDL-BUFFER-POINTER",
            3 => "PDL-BUFFER-INDEX", 5 => "C-PDL-BUFFER-INDEX", 6 => "C-OPC-BUFFER",
            7 => "Q-R", 8 => "VMA", 9 => "MEMORY-MAP-DATA", 10 => "MD",
            11 => "LOCATION-COUNTER", 12 => "MICRO-STACK-PNTR-AND-DATA-POP",
            20 => "C-PDL-BUFFER-POINTER-POP", 21 => "C-PDL-BUFFER-POINTER",
            _ => $"FSOURCE-{Convert.ToString((int)fsource, 8)}",
        };
        return $"{name} ";
    }

    /// <summary>
    /// Faithful port of usim/udiss.c's dest_desc()/dest_desc_1()/
    /// m_dest_desc()/a_dest_desc()/q_dest_desc(). Bit 25 selects
    /// A-dest (1) vs M-dest (0) -- independently cross-checked against
    /// UCode.cs's existing WriteDest(): dest&amp;0x800 (bit11 of a 12-bit
    /// dest value = bit25 of the full instruction) is the same selector,
    /// AMem's 10-bit field is dest&amp;0x3FF, MMem's 5-bit field is
    /// dest&amp;0x1F -- confirmed matching, not a divergence.
    /// </summary>
    private static string DestDesc(ulong instruction)
    {
        uint topCheck = (uint)Ir(instruction, 14, 11);
        if (topCheck == 0)
        {
            return QDestDesc(instruction);
        }

        var sb = new System.Text.StringBuilder(" (");
        if (Ir(instruction, 25, 1) == 0)
        {
            sb.Append(MDestDesc(instruction));
        }
        else
        {
            sb.Append(TypeField(SymbolType.AMem, instruction, 14, 10));
        }
        if (Ir(instruction, 43, 2) == 0 && Ir(instruction, 0, 2) == 3)
        {
            sb.Append("Q-R");
        }
        sb.Append(") ");
        return sb.ToString();
    }

    private static string QDestDesc(ulong instruction)
    {
        if (Ir(instruction, 43, 2) == 0 && Ir(instruction, 0, 2) == 3)
        {
            return " (Q-R) ";
        }
        return "";
    }

    private static string MDestDesc(ulong instruction)
    {
        string result = TypeField(SymbolType.MMem, instruction, 14, 5);
        uint fdest = (uint)Ir(instruction, 19, 5);
        string name = fdest switch
        {
            1 => "LOCATION-COUNTER", 2 => "INTERRUPT-CONTROL", 8 => "C-PDL-BUFFER-POINTER",
            9 => "C-PDL-BUFFER-POINTER-PUSH", 10 => "C-PDL-BUFFER-INDEX", 11 => "PDL-BUFFER-INDEX",
            12 => "PDL-BUFFER-POINTER", 13 => "MICRO-STACK-DATA-PUSH", 14 => "OA-REG-LOW",
            15 => "OA-REG-HI", 16 => "VMA", 17 => "VMA-START-READ", 18 => "VMA-START-WRITE",
            19 => "VMA-WRITE-MAP", 24 => "MD", 26 => "MD-START-WRITE", 27 => "MD-WRITE-MAP",
            0 => "",
            _ => $"FDEST-{Convert.ToString((int)fdest, 8)}",
        };
        return name.Length > 0 ? $"{result}{name} " : result;
    }

    private static string DisassembleAlu(ulong instruction)
    {
        string dest = DestDesc(instruction);
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

        // Faithful port of usim/udiss.c:413-417 -- aluop==026 octal (22
        // decimal, SUB) uses sub_carry_desc (prints only when carry==0),
        // every other aluop uses normal_carry_desc (prints only when
        // carry==1).
        bool carrySub = aluop == 22;
        uint carryBit = (uint)Ir(instruction, 2, 1);
        string carryStr = carrySub
            ? (carryBit == 0 ? "ALU-CARRY-IN-ZERO " : "")
            : (carryBit == 1 ? "ALU-CARRY-IN-ONE " : "");

        uint outputSelector = (uint)Ir(instruction, 12, 2);
        string outputSelectorStr = outputSelector switch
        {
            0 => $"OUTPUT-SELECTOR-{Convert.ToString(0, 8)} ",
            2 => "OUTPUT-SELECTOR-RIGHTSHIFT-1 ",
            3 => "OUTPUT-SELECTOR-LEFTSHIFT-1 ",
            _ => "", // case 1: real C prints nothing
        };

        uint qShift = (uint)Ir(instruction, 0, 2);
        string qShiftStr = qShift switch
        {
            1 => "SHIFT-Q-LEFT ",
            2 => "SHIFT-Q-RIGHT ",
            _ => "", // 0 and 3: real C prints nothing
        };

        string mSource = MSourceDesc(instruction);
        string aField = TypeField(SymbolType.AMem, instruction, 32, 10);

        uint mf = (uint)Ir(instruction, 10, 2);
        string mfStr = mf == 0 ? "" : $"MF-{Convert.ToString((int)mf, 8)} ";
        uint ilong = (uint)Ir(instruction, 45, 1);
        string ilongStr = ilong == 1 ? "ILONG " : "";

        string tail = JoinTokens(dest, opName, carryStr, outputSelectorStr, qShiftStr, mSource, aField, mfStr, ilongStr, $"raw=0x{instruction:X12}");
        return $"[ALU {tail}]";
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

        // Faithful port of usim/udiss.c:572-598's jmp_desc()'s exact call
        // order after type_jump_condition(): m_source_desc, A-field,
        // I-field, THEN mf, THEN ilong. (mf/ilong intentionally sit
        // after these new fields, not before them, to match that real
        // order -- NOT "after the existing mfStr" textually, which
        // would put mf ahead of m_source_desc/A-field/I-field and
        // contradict jmp_desc's real call sequence.)
        string mSource = MSourceDesc(instruction);
        string aField = TypeField(SymbolType.AMem, instruction, 32, 10);
        string iField = TypeField(SymbolType.IMem, instruction, 12, 14);

        uint mf = (uint)Ir(instruction, 10, 2);
        string mfStr = mf == 0 ? "" : $"MF-{Convert.ToString((int)mf, 8)} ";
        uint ilong = (uint)Ir(instruction, 45, 1);
        string ilongStr = ilong == 1 ? "ILONG " : "";

        string tail = JoinTokens(mSource, aField, iField, mfStr, ilongStr, $"raw=0x{instruction:X12}");
        return $"[JUMP target={target:X4} flags={flags} cond={cond} {tail}]";
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
            uint reflected = 32 - rot;
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
                modeText = $"{(xctNext ? "-XCT-NEXT" : "")} JUMP-CONDITION {Convert.ToString(rawCond, 8)}{inverted}";
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

        // Faithful port of usim/udiss.c:608-662's dsp_desc()'s remaining
        // call order (after dsp_const_desc, already ported above as
        // dispConstStr): byte_field_out (always_reflect_mrot=true here,
        // matching udiss.c:623's third argument), m_source_desc,
        // D-field, push_own_address_p, ifetch_p, then the existing
        // map/mf, then ilong.
        string byteField = ByteFieldOut(instruction, alwaysReflectMrot: true, lengthIsMinusOne: false);
        string mSource = MSourceDesc(instruction);
        string dField = TypeField(SymbolType.DMem, instruction, 12, 11);
        uint pushOwnAddress = (uint)Ir(instruction, 25, 1);
        string pushOwnAddressStr = pushOwnAddress == 1 ? "PUSH-OWN-ADDRESS " : "";
        uint ifetch = (uint)Ir(instruction, 24, 1);
        string ifetchStr = ifetch == 1 ? "IFETCH " : "";
        uint ilong = (uint)Ir(instruction, 45, 1);
        string ilongStr = ilong == 1 ? "ILONG " : "";

        string tail = JoinTokens(dispConstStr, byteField, mSource, dField, pushOwnAddressStr, ifetchStr, mapStr, mfStr, ilongStr, $"raw=0x{instruction:X12}");
        return $"[DISPATCH addr={dispAddr:X3} {tail}]";
    }

    private static string DisassembleByte(ulong instruction)
    {
        string dest = DestDesc(instruction);
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

        // Faithful port of usim/udiss.c:687's byte_field_out call
        // (always_reflect_mrot=false, length_is_minus_one=true) --
        // reflection here depends on the mrot-check bit
        // (Ir(instruction,12,2)==1), NOT unconditional like DISPATCH's
        // call.
        string byteField = ByteFieldOut(instruction, alwaysReflectMrot: false, lengthIsMinusOne: true);
        string mSource = MSourceDesc(instruction);
        string aField = TypeField(SymbolType.AMem, instruction, 32, 10);

        uint mf = (uint)Ir(instruction, 10, 2);
        string mfStr = mf == 0 ? "" : $" MF-{Convert.ToString((int)mf, 8)}";
        uint ilong = (uint)Ir(instruction, 45, 1);
        string ilongStr = ilong == 1 ? "ILONG " : "";

        string destAndOp = JoinTokens(dest, opName);
        string tail = JoinTokens(byteField, mSource, aField, mfStr, ilongStr, $"raw=0x{instruction:X12}");
        return $"[BYTE {destAndOp} width={widthm1 + 1} pos={pos} {tail}]";
    }
}
