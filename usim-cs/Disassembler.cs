// Disassembler.cs - Microcode disassembly
// Converted from udiss.h and udiss.c

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
        // Fetch instruction from appropriate memory
        ulong instruction;
        
        if (pcImem && pc < UCode.IMem.Length)
        {
            instruction = UCode.IMem[pc];
        }
        else if (!pcImem && UCode.PromEnabledFlag && pc < UCode.Prom.Length)
        {
            instruction = UCode.Prom[pc];
        }
        else if (pc < UCode.IMem.Length)
        {
            instruction = UCode.IMem[pc];
        }
        else
        {
            return $"[PC {pc:X4}: invalid]";
        }
        
        return DisassembleInst2(instruction, pcImem);
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
        
        // Extract instruction fields using UCode constants
        var aluOp = UCode.GetAluOp(instruction);
        uint mSource = UCode.GetMSource(instruction);
        uint aSource = UCode.GetASource(instruction);
        uint dest = UCode.GetDest(instruction);
        uint jumpCond = UCode.GetJumpCond(instruction);
        uint nextPc = UCode.GetNextPC(instruction);
        
        // Build disassembly string
        var parts = new System.Collections.Generic.List<string>();
        
        // ALU operation
        parts.Add(FormatAluOp(aluOp));
        
        // Sources
        if (mSource != 0 || aSource != 0)
        {
            parts.Add($"M={FormatMSource(mSource)}");
            parts.Add($"A={FormatASource(aSource)}");
        }
        
        // Destination
        if (dest != 0)
        {
            parts.Add($"?{FormatDestination(dest)}");
        }
        
        // Jump condition
        if (jumpCond != 0 || nextPc != 0)
        {
            parts.Add($"JMP[{FormatJumpCond(jumpCond)}]?{nextPc:X4}");
        }
        
        return string.Join(" ", parts);
    }
    
    /// <summary>
    /// Format ALU operation name
    /// </summary>
    private static string FormatAluOp(UCode.AluOp op)
    {
        return op switch
        {
            UCode.AluOp.SetZ => "SETZ",
            UCode.AluOp.And => "AND",
            UCode.AluOp.AndCA => "ANDCA",
            UCode.AluOp.SetM => "SETM",
            UCode.AluOp.AndCM => "ANDCM",
            UCode.AluOp.SetA => "SETA",
            UCode.AluOp.Xor => "XOR",
            UCode.AluOp.Or => "OR",
            UCode.AluOp.AndCMAndCA => "NOR",
            UCode.AluOp.Eqv => "EQV",
            UCode.AluOp.SetCA => "NOTCA",
            UCode.AluOp.OrCA => "ORCA",
            UCode.AluOp.SetCM => "NOTCM",
            UCode.AluOp.OrCM => "ORCM",
            UCode.AluOp.OrCA_OrCM => "NAND",
            UCode.AluOp.SetO => "SETO",
            UCode.AluOp.Add => "ADD",
            UCode.AluOp.Sub => "SUB",
            UCode.AluOp.SubCM => "SUBCM",
            UCode.AluOp.AddCA => "ADDCA",
            UCode.AluOp.SubM => "SUBM",
            UCode.AluOp.SubM1 => "SUBM1",
            UCode.AluOp.AddCM => "ADDCM",
            UCode.AluOp.AddCM1 => "ADDCM1",
            UCode.AluOp.M_Plus_A => "M+A",
            UCode.AluOp.M_Or_A => "M|A",
            UCode.AluOp.M_And_A => "M&A",
            UCode.AluOp.M_Xor_A => "M^A",
            UCode.AluOp.M_Minus_1 => "M-1",
            UCode.AluOp.M_Plus_1 => "M+1",
            _ => $"ALU{(int)op:X2}"
        };
    }
    
    /// <summary>
    /// Format M source
    /// </summary>
    private static string FormatMSource(uint mSource)
    {
        return mSource switch
        {
            0 => "0",
            1 => "M[0]",
            2 => "M[1]",
            3 => "M[2]",
            4 => "M[3]",
            5 => "PDL-PTR",
            6 => "VMA",
            7 => "MD",
            8 => "LC",
            9 => "Q",
            10 => "OA-LOW",
            11 => "OA-HIGH",
            _ => $"M[{mSource}]"
        };
    }
    
    /// <summary>
    /// Format A source
    /// </summary>
    private static string FormatASource(uint aSource)
    {
        return aSource switch
        {
            1024 => "PDL-TOP",
            1025 => "OUT",
            _ when aSource < 1024 => $"A[{aSource:X3}]",
            _ => $"A[{aSource}]"
        };
    }
    
    /// <summary>
    /// Format destination
    /// </summary>
    private static string FormatDestination(uint dest)
    {
        return dest switch
        {
            0 => "NOP",
            1 => "AMEM",
            2 => "MMEM",
            3 => "PDL",
            4 => "VMA",
            5 => "MD",
            6 => "LC",
            7 => "Q",
            8 => "OA-LOW",
            9 => "OA-HIGH",
            10 => "PDL-PTR",
            _ => $"DEST{dest}"
        };
    }
    
    /// <summary>
    /// Format jump condition
    /// </summary>
    private static string FormatJumpCond(uint cond)
    {
        return cond switch
        {
            0 => "UNCOND",
            1 => "Z",
            2 => "NZ",
            3 => "N",
            4 => "NN",
            5 => "C",
            6 => "NC",
            7 => "V",
            8 => "NV",
            9 => "B0",
            10 => "NB0",
            11 => "INT",
            12 => "NINT",
            13 => "LE",
            14 => "GT",
            15 => "NEVER",
            _ => $"COND{cond}"
        };
    }
    
    /// <summary>
    /// Format microcode field
    /// </summary>
    private static string FormatField(string name, ulong value)
    {
        return $"{name}={value:X}";
    }
}
