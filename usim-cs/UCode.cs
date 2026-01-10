// UCode.cs - Microcode execution engine
// Converted from ucode.h and ucode.c
//
// This file contains the microcode execution engine for the CADR Lisp Machine simulator.
// The microcode implements the low-level operations including:
// - ALU operations (arithmetic, logic, shifts, rotations)
// - Memory operations (A, M, D memory access)
// - Control flow (jumps, dispatch, etc.)
// - Stack operations (PDL, SPC)
//
// Bit manipulation operations (rol32, ror32, etc.) are available in MiscUtils.cs

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Microcode execution engine for CADR simulator
/// </summary>
public class UCode
{
    #region Constants
    
    // Memory sizes
    public const int PROM_SIZE = 512;
    public const int IMEM_SIZE = 16 * 1024;
    public const int AMEM_SIZE = 1024;
    public const int MMEM_SIZE = 32;
    public const int DMEM_SIZE = 2048;
    public const int PDL_SIZE = 1024;
    public const int SPC_SIZE = 32;
    
    // Field positions in microcode instruction (48-bit)
    public const int ALU_OP_POS = 43;
    public const int ALU_OP_SIZE = 5;
    public const int M_SOURCE_POS = 37;
    public const int M_SOURCE_SIZE = 5;
    public const int A_SOURCE_POS = 26;
    public const int A_SOURCE_SIZE = 10;
    public const int DEST_POS = 19;
    public const int DEST_SIZE = 5;
    public const int JUMP_COND_POS = 14;
    public const int JUMP_COND_SIZE = 5;
    public const int NEXT_PC_POS = 0;
    public const int NEXT_PC_SIZE = 14;
    
    #endregion
    
    // Machine cycles counter
    public static ulong MachineCycles { get; set; }
    
    // Program counter
    public uint Pc { get; set; }
    
    // Interrupt status
    public static int InterruptStatusReg { get; private set; }
    public static bool InterruptPendingFlag { get; set; }
    
    // Microcode memory
    public static bool PromEnabledFlag { get; set; }
    public static ulong[] Prom { get; } = new ulong[512];
    public static ulong[] IMem { get; } = new ulong[16 * 1024];
    
    // A, M, and D memories
    public static uint[] AMem { get; } = new uint[1024];
    public static uint[] MMem { get; } = new uint[32];
    public static uint[] DMem { get; } = new uint[2048];
    
    // Push-down list (stack)
    public static uint[] Pdl { get; } = new uint[1024];
    
    // Stack pointer cache
    public static uint[] Spc { get; } = new uint[32];
    public static uint SpcPtr { get; set; }
    
    // Registers
    public static uint PdlPointer { get; set; }
    public static uint PdlIndex { get; set; }
    public static uint VmaReg { get; set; }
    public static uint MdReg { get; set; }
    public static uint Lc { get; set; }
    public static uint OaRegHigh { get; set; }
    public static uint OaRegLow { get; set; }
    
    // Pipeline registers
    public static ulong P0 { get; set; }
    public static uint P0Pc { get; set; }
    public static bool P0IMem { get; set; }
    
    public static ulong P1 { get; set; }
    public static uint P1Pc { get; set; }
    public static bool P1IMem { get; set; }
    
    public static ulong Iwr { get; set; }
    
    public static uint Npc { get; set; }
    public static uint Opc { get; set; }
    
    public static int MData { get; set; }
    public static int AData { get; set; }
    
    public static ulong DebugIr { get; set; }
    
    public static uint Out { get; set; }
    public static uint Q { get; set; }
    
    public static bool Inhibit { get; set; }
    
    public static bool UExecHasRunOnce { get; set; }
    
    // Processor status flags
    public static bool CarryFlag { get; set; }
    public static bool OverflowFlag { get; set; }
    public static bool NegativeFlag { get; set; }
    public static bool ZeroFlag { get; set; }
    
    /// <summary>
    /// Update processor flags based on ALU result
    /// </summary>
    public static void UpdateFlags(uint result, uint m, uint a, AluOp op)
    {
        // Zero flag
        ZeroFlag = (result == 0);
        
        // Negative flag (sign bit)
        NegativeFlag = ((result & 0x80000000) != 0);
        
        // Carry and overflow depend on operation
        if (op >= AluOp.Add && op <= AluOp.M_Plus_1)
        {
            // For arithmetic operations, compute carry and overflow
            ulong longResult = (ulong)m + (ulong)a;
            CarryFlag = (longResult > 0xFFFFFFFF);
            
            // Overflow: operands same sign, result different sign
            bool mSign = (m & 0x80000000) != 0;
            bool aSign = (a & 0x80000000) != 0;
            bool rSign = (result & 0x80000000) != 0;
            OverflowFlag = (mSign == aSign) && (mSign != rSign);
        }
    }
    
    /// <summary>
    /// Initialize the microcode system
    /// </summary>
    public static void Init()
    {
        MachineCycles = 0;
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
        PromEnabledFlag = false;
        
        Array.Clear(Prom);
        Array.Clear(IMem);
        Array.Clear(AMem);
        Array.Clear(MMem);
        Array.Clear(DMem);
        Array.Clear(Pdl);
        Array.Clear(Spc);
        
        PdlPointer = 0;
        PdlIndex = 0;
        VmaReg = 0;
        MdReg = 0;
        Lc = 0;
        OaRegHigh = 0;
        OaRegLow = 0;
        
        P0 = 0;
        P0Pc = 0;
        P0IMem = false;
        
        P1 = 0;
        P1Pc = 0;
        P1IMem = false;
        
        Iwr = 0;
        Npc = 0;
        Opc = 0;
        
        MData = 0;
        AData = 0;
        DebugIr = 0;
        
        Out = 0;
        Q = 0;
        Inhibit = false;
        UExecHasRunOnce = false;
        
        CarryFlag = false;
        OverflowFlag = false;
        NegativeFlag = false;
        ZeroFlag = false;
    }
    
    /// <summary>
    /// Load PROM from file
    /// </summary>
    public static void LoadPromFromFile(string filename)
    {
        using var stream = File.OpenRead(filename);
        for (int i = 0; i < Prom.Length; i++)
        {
            if (stream.Position >= stream.Length)
                break;
                
            // Read 64-bit microcode instruction
            byte[] buffer = new byte[8];
            stream.Read(buffer, 0, 8);
            
            Prom[i] = BitConverter.ToUInt64(buffer, 0);
        }
        
        PromEnabledFlag = true;
    }
    
    /// <summary>
    /// Set interrupt status register
    /// </summary>
    public static void SetInterruptStatusReg(int newValue)
    {
        InterruptStatusReg = newValue;
        InterruptPendingFlag = (newValue != 0);
    }
    
    /// <summary>
    /// Assert Unibus interrupt
    /// </summary>
    public static void AssertUnibusInterrupt(int level)
    {
        InterruptStatusReg |= (1 << level);
        InterruptPendingFlag = true;
    }
    
    /// <summary>
    /// Deassert Unibus interrupt
    /// </summary>
    public static void DeassertUnibusInterrupt()
    {
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
    }
    
    /// <summary>
    /// Assert Xbus interrupt
    /// </summary>
    public static void AssertXbusInterrupt()
    {
        InterruptPendingFlag = true;
    }
    
    /// <summary>
    /// Deassert Xbus interrupt
    /// </summary>
    public static void DeassertXbusInterrupt()
    {
        InterruptPendingFlag = false;
    }
    
    /// <summary>
    /// Shift OPC stack
    /// </summary>
    public static void ShiftOpcs(uint input)
    {
        Opc = input;
    }
    
    /// <summary>
    /// Execute one microcode step
    /// </summary>
    public static void Step()
    {
        // This is a placeholder - actual microcode execution logic
        // would be significantly more complex
        MachineCycles++;
        UExecHasRunOnce = true;
    }
    
    /// <summary>
    /// Execute one complete microcode instruction
    /// </summary>
    public static void ExecuteInstruction(uint pc, bool useImem)
    {
        // Fetch instruction
        ulong instruction = FetchInstruction(pc, useImem);
        
        // Decode instruction fields
        AluOp aluOp = GetAluOp(instruction);
        uint mSource = GetMSource(instruction);
        uint aSource = GetASource(instruction);
        uint dest = GetDest(instruction);
        uint jumpCond = GetJumpCond(instruction);
        uint nextPc = GetNextPC(instruction);
        
        // Read operands
        uint mValue = ReadMSource(mSource);
        uint aValue = ReadASource(aSource);
        
        // Execute ALU operation
        uint aluResult = ExecuteAlu(aluOp, mValue, aValue, CarryFlag);
        
        // Update processor flags
        UpdateFlags(aluResult, mValue, aValue, aluOp);
        
        // Store result to destination
        WriteDestination(dest, aluResult);
        
        // Update output register
        Out = aluResult;
        
        // Trace instruction execution
        TraceInstruction(pc, useImem, instruction, aluResult);
        
        // Evaluate jump condition and update PC
        bool jumpTaken = EvaluateJumpCondition(jumpCond, aluResult);
        if (jumpTaken)
        {
            Npc = nextPc;
        }
        
        // Update statistics
        MachineCycles++;
        TotalInstructions++;
        TotalAluOps++;
    }
    
    /// <summary>
    /// Fetch instruction from memory
    /// </summary>
    public static ulong FetchInstruction(uint pc, bool useImem)
    {
        if (useImem && pc < IMEM_SIZE)
        {
            return IMem[pc];
        }
        else if (!useImem && PromEnabledFlag && pc < PROM_SIZE)
        {
            return Prom[pc];
        }
        else if (pc < IMEM_SIZE)
        {
            return IMem[pc];
        }
        
        return 0; // Invalid PC
    }
    
    /// <summary>
    /// Read M-source operand
    /// </summary>
    public static uint ReadMSource(uint mSource)
    {
        return mSource switch
        {
            0 => 0,                    // Zero
            1 => ReadMMem(0),          // M[0]
            2 => ReadMMem(1),          // M[1]
            3 => ReadMMem(2),          // M[2]
            4 => ReadMMem(3),          // M[3]
            5 => PdlPointer,           // PDL pointer
            6 => VmaReg,               // VMA
            7 => MdReg,                // MD
            8 => Lc,                   // LC
            9 => Q,                    // Q register
            10 => OaRegLow,            // OA low
            11 => OaRegHigh,           // OA high
            _ => ReadMMem(mSource & 0x1F)
        };
    }
    
    /// <summary>
    /// Read A-source operand
    /// </summary>
    public static uint ReadASource(uint aSource)
    {
        if (aSource < 1024)
        {
            return ReadAMem(aSource);
        }
        
        // Special A-sources beyond 1024
        return aSource switch
        {
            1024 => Pdl[PdlPointer],     // PDL top
            1025 => Out,                 // Output register
            _ => 0
        };
    }
    
    /// <summary>
    /// Write to destination
    /// </summary>
    public static void WriteDestination(uint dest, uint value)
    {
        switch (dest)
        {
            case 0: // NOP - no write
                break;
            case 1: // A memory
                WriteAMem((uint)AData, value);
                break;
            case 2: // M memory
                WriteMMem((uint)MData, value);
                break;
            case 3: // PDL
                PushPdl(value);
                break;
            case 4: // VMA
                VmaReg = value;
                break;
            case 5: // MD
                MdReg = value;
                break;
            case 6: // LC
                Lc = value;
                break;
            case 7: // Q
                Q = value;
                break;
            case 8: // OA low
                OaRegLow = value;
                break;
            case 9: // OA high
                OaRegHigh = value;
                break;
            case 10: // PDL pointer
                PdlPointer = value & 0x3FF;
                break;
            default:
                // Extended destinations
                break;
        }
    }
    
    /// <summary>
    /// Evaluate jump condition
    /// </summary>
    public static bool EvaluateJumpCondition(uint condition, uint aluResult)
    {
        return condition switch
        {
            0 => true,                              // Unconditional
            1 => ZeroFlag,                          // Jump if zero
            2 => !ZeroFlag,                         // Jump if not zero
            3 => NegativeFlag,                      // Jump if negative
            4 => !NegativeFlag,                     // Jump if not negative
            5 => CarryFlag,                         // Jump if carry
            6 => !CarryFlag,                        // Jump if no carry
            7 => OverflowFlag,                      // Jump if overflow
            8 => !OverflowFlag,                     // Jump if no overflow
            9 => (aluResult & 1) != 0,              // Jump if bit 0 set
            10 => (aluResult & 1) == 0,             // Jump if bit 0 clear
            11 => InterruptPendingFlag,             // Jump if interrupt pending
            12 => !InterruptPendingFlag,            // Jump if no interrupt
            13 => ZeroFlag || NegativeFlag,         // Jump if <= 0
            14 => !ZeroFlag && !NegativeFlag,       // Jump if > 0
            15 => false,                            // Never (for debugging)
            _ => true                               // Default to unconditional
        };
    }
    
    /// <summary>
    /// Main machine run loop
    /// </summary>
    public static bool MachRun()
    {
        // Return true if machine should continue running
        return !Inhibit;
    }
    
    /// <summary>
    /// Run the microcode engine
    /// </summary>
    public static void Run()
    {
        while (MachRun())
        {
            Step();
        }
    }
    
    #region ALU Operations
    
    /// <summary>
    /// ALU operation codes
    /// </summary>
    public enum AluOp
    {
        SetZ = 0,      // Set zero
        And = 1,       // Logical AND
        AndCA = 2,     // AND with complement of A
        SetM = 3,      // Set M
        AndCM = 4,     // AND with complement of M
        SetA = 5,      // Set A
        Xor = 6,       // Exclusive OR
        Or = 7,        // Logical OR
        AndCMAndCA = 8,// AND with complement of M and A
        Eqv = 9,       // Equivalence
        SetCA = 10,    // Set complement of A
        OrCA = 11,     // OR with complement of A
        SetCM = 12,    // Set complement of M
        OrCM = 13,     // OR with complement of M
        OrCA_OrCM = 14,// OR of complements
        SetO = 15,     // Set ones
        Add = 16,      // Add
        Sub = 17,      // Subtract
        SubCM = 18,    // Subtract complement of M
        AddCA = 19,    // Add complement of A
        SubM = 20,     // Subtract M
        SubM1 = 21,    // Subtract M and 1
        AddCM = 22,    // Add complement of M
        AddCM1 = 23,   // Add complement of M and 1
        M_Plus_A = 24, // M + A
        M_Or_A = 25,   // M OR A
        M_And_A = 26,  // M AND A
        M_Xor_A = 27,  // M XOR A
        M_Minus_1 = 28,// M - 1
        M_Plus_1 = 29, // M + 1
        Undefined30 = 30,
        Undefined31 = 31
    }
    
    /// <summary>
    /// Execute ALU operation
    /// </summary>
    public static uint ExecuteAlu(AluOp op, uint m, uint a, bool carry)
    {
        uint result;
        
        switch (op)
        {
            case AluOp.SetZ: result = 0; break;
            case AluOp.And: result = m & a; break;
            case AluOp.AndCA: result = m & ~a; break;
            case AluOp.SetM: result = m; break;
            case AluOp.AndCM: result = ~m & a; break;
            case AluOp.SetA: result = a; break;
            case AluOp.Xor: result = m ^ a; break;
            case AluOp.Or: result = m | a; break;
            case AluOp.AndCMAndCA: result = ~m & ~a; break;
            case AluOp.Eqv: result = ~(m ^ a); break;
            case AluOp.SetCA: result = ~a; break;
            case AluOp.OrCA: result = m | ~a; break;
            case AluOp.SetCM: result = ~m; break;
            case AluOp.OrCM: result = ~m | a; break;
            case AluOp.OrCA_OrCM: result = ~m | ~a; break;
            case AluOp.SetO: result = 0xFFFFFFFF; break;
            
            // Arithmetic operations
            case AluOp.Add:
                result = m + a + (carry ? 1u : 0u);
                break;
            case AluOp.Sub:
                result = m - a - (carry ? 0u : 1u);
                break;
            case AluOp.SubCM:
                result = ~m - a - (carry ? 0u : 1u);
                break;
            case AluOp.AddCA:
                result = m + ~a + (carry ? 1u : 0u);
                break;
            case AluOp.SubM:
                result = a - m - (carry ? 0u : 1u);
                break;
            case AluOp.SubM1:
                result = a - m - 1;
                break;
            case AluOp.AddCM:
                result = ~m + a + (carry ? 1u : 0u);
                break;
            case AluOp.AddCM1:
                result = ~m + a + 1;
                break;
            case AluOp.M_Plus_A:
                result = m + a;
                break;
            case AluOp.M_Or_A:
                result = m | a;
                break;
            case AluOp.M_And_A:
                result = m & a;
                break;
            case AluOp.M_Xor_A:
                result = m ^ a;
                break;
            case AluOp.M_Minus_1:
                result = m - 1;
                break;
            case AluOp.M_Plus_1:
                result = m + 1;
                break;
            
            default:
                result = 0;
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// Barrel shifter operation
    /// </summary>
    public static uint BarrelShift(uint input, int operation, int count)
    {
        count &= 0x1F; // Limit to 0-31
        
        return operation switch
        {
            0 => input, // No shift
            1 => MiscUtils.Lsl32(input, count), // Logical shift left
            2 => MiscUtils.Lsr32(input, count), // Logical shift right
            3 => MiscUtils.Asr32(input, count), // Arithmetic shift right
            4 => MiscUtils.Rol32(input, count), // Rotate left
            5 => MiscUtils.Ror32(input, count), // Rotate right
            _ => input
        };
    }
    
    #endregion
    
    #region Instruction Decode
    
    /// <summary>
    /// Extract field from microcode instruction
    /// </summary>
    public static ulong ExtractField(ulong instruction, int position, int size)
    {
        return MiscUtils.LoadByte(instruction, position, size);
    }
    
    /// <summary>
    /// Get ALU operation from instruction
    /// </summary>
    public static AluOp GetAluOp(ulong instruction)
    {
        return (AluOp)ExtractField(instruction, ALU_OP_POS, ALU_OP_SIZE);
    }
    
    /// <summary>
    /// Get M-source from instruction
    /// </summary>
    public static uint GetMSource(ulong instruction)
    {
        return (uint)ExtractField(instruction, M_SOURCE_POS, M_SOURCE_SIZE);
    }
    
    /// <summary>
    /// Get A-source from instruction
    /// </summary>
    public static uint GetASource(ulong instruction)
    {
        return (uint)ExtractField(instruction, A_SOURCE_POS, A_SOURCE_SIZE);
    }
    
    /// <summary>
    /// Get destination from instruction
    /// </summary>
    public static uint GetDest(ulong instruction)
    {
        return (uint)ExtractField(instruction, DEST_POS, DEST_SIZE);
    }
    
    /// <summary>
    /// Get jump condition from instruction
    /// </summary>
    public static uint GetJumpCond(ulong instruction)
    {
        return (uint)ExtractField(instruction, JUMP_COND_POS, JUMP_COND_SIZE);
    }
    
    /// <summary>
    /// Get next PC from instruction
    /// </summary>
    public static uint GetNextPC(ulong instruction)
    {
        return (uint)ExtractField(instruction, NEXT_PC_POS, NEXT_PC_SIZE);
    }
    
    #endregion
    
    #region Memory Access
    
    /// <summary>
    /// Read from A memory
    /// </summary>
    public static uint ReadAMem(uint address)
    {
        address &= 0x3FF; // 10-bit address
        return AMem[address];
    }
    
    /// <summary>
    /// Write to A memory
    /// </summary>
    public static void WriteAMem(uint address, uint value)
    {
        address &= 0x3FF;
        AMem[address] = value;
    }
    
    /// <summary>
    /// Read from M memory
    /// </summary>
    public static uint ReadMMem(uint address)
    {
        address &= 0x1F; // 5-bit address
        return MMem[address];
    }
    
    /// <summary>
    /// Write to M memory
    /// </summary>
    public static void WriteMMem(uint address, uint value)
    {
        address &= 0x1F;
        MMem[address] = value;
    }
    
    /// <summary>
    /// Read from D memory
    /// </summary>
    public static uint ReadDMem(uint address)
    {
        address &= 0x7FF; // 11-bit address
        return DMem[address];
    }
    
    /// <summary>
    /// Write to D memory
    /// </summary>
    public static void WriteDMem(uint address, uint value)
    {
        address &= 0x7FF;
        DMem[address] = value;
    }
    
    /// <summary>
    /// Push value onto PDL stack
    /// </summary>
    public static void PushPdl(uint value)
    {
        PdlPointer = (PdlPointer + 1) & 0x3FF;
        Pdl[PdlPointer] = value;
    }
    
    /// <summary>
    /// Pop value from PDL stack
    /// </summary>
    public static uint PopPdl()
    {
        uint value = Pdl[PdlPointer];
        PdlPointer = (PdlPointer - 1) & 0x3FF;
        return value;
    }
    
    /// <summary>
    /// Read PDL at offset from pointer (0 = top of stack, 1 = second from top, etc.)
    /// </summary>
    public static uint ReadPdl(uint offset)
    {
        // In CADR, offset 0 means top of stack (current PdlPointer)
        // offset 1 means one back, offset 2 means two back, etc.
        uint address = (PdlPointer - offset) & 0x3FF;
        return Pdl[address];
    }
    
    #endregion
    
    #region Dispatch ROM
    
    // Dispatch ROM for opcode dispatch
    public static ushort[] DispatchRom { get; } = new ushort[2048];
    public static bool DispatchRomLoadedFlag { get; set; }
    
    /// <summary>
    /// Load dispatch ROM from file
    /// </summary>
    public static void LoadDispatchRomFromFile(string filename)
    {
        using var stream = File.OpenRead(filename);
        for (int i = 0; i < DispatchRom.Length; i++)
        {
            if (stream.Position >= stream.Length)
                break;
                
            // Read 16-bit dispatch entry
            byte[] buffer = new byte[2];
            stream.Read(buffer, 0, 2);
            
            DispatchRom[i] = BitConverter.ToUInt16(buffer, 0);
        }
        
        DispatchRomLoadedFlag = true;
    }
    
    /// <summary>
    /// Perform dispatch operation
    /// </summary>
    public static uint Dispatch(uint dispatchAddress, uint instruction)
    {
        // Combine dispatch address with instruction bits to form ROM address
        uint romAddress = (dispatchAddress & 0x7FF) | ((instruction & 0x0F) << 11);
        
        if (romAddress < DispatchRom.Length && DispatchRomLoadedFlag)
        {
            return DispatchRom[romAddress];
        }
        
        return 0;
    }
    
    #endregion
    
    #region SPC (Stack Pointer Cache)
    
    /// <summary>
    /// Push value onto SPC
    /// </summary>
    public static void PushSpc(uint value)
    {
        SpcPtr = (SpcPtr + 1) & 0x1F;
        Spc[SpcPtr] = value;
    }
    
    /// <summary>
    /// Pop value from SPC
    /// </summary>
    public static uint PopSpc()
    {
        uint value = Spc[SpcPtr];
        SpcPtr = (SpcPtr - 1) & 0x1F;
        return value;
    }
    
    /// <summary>
    /// Read SPC without modifying pointer
    /// </summary>
    public static uint ReadSpc()
    {
        return Spc[SpcPtr];
    }
    
    #endregion
    
    #region Pipeline and Control
    
    /// <summary>
    /// Advance pipeline by one stage
    /// </summary>
    public static void AdvancePipeline(uint currentPc, bool currentPcImem)
    {
        // Shift pipeline: P0 -> P1 -> IWR
        Iwr = P1;
        P1 = P0;
        P1Pc = P0Pc;
        P1IMem = P0IMem;
        
        // Fetch next instruction into P0
        P0 = FetchInstruction(currentPc, currentPcImem);
        P0Pc = currentPc;
        P0IMem = currentPcImem;
    }
    
    /// <summary>
    /// Flush pipeline (for jumps/interrupts)
    /// </summary>
    public static void FlushPipeline()
    {
        P0 = 0;
        P0Pc = 0;
        P0IMem = false;
        
        P1 = 0;
        P1Pc = 0;
        P1IMem = false;
        
        Iwr = 0;
    }
    
    /// <summary>
    /// Check if page fault occurred
    /// </summary>
    public static bool CheckPageFault(uint address)
    {
        // Simplified page fault check
        // In real hardware, this would check page tables
        return false;
    }
    
    /// <summary>
    /// Handle memory cycle
    /// </summary>
    public static void MemoryCycle()
    {
        // This would handle memory access timing
        // For now, just increment cycle counter
        MachineCycles++;
    }
    
    #endregion
    
    #region Debug and Trace
    
    /// <summary>
    /// Enable/disable instruction tracing
    /// </summary>
    public static bool InstructionTraceEnabled { get; set; }
    
    /// <summary>
    /// Enable/disable microcode tracing
    /// </summary>
    public static bool MicrocodeTraceEnabled { get; set; }
    
    /// <summary>
    /// Maximum number of trace lines to keep
    /// </summary>
    public static int MaxTraceLines { get; set; } = 10000;
    
    /// <summary>
    /// Trace buffer for instruction history
    /// </summary>
    private static readonly System.Collections.Generic.Queue<string> _traceBuffer = new();
    
    /// <summary>
    /// Trace an instruction execution
    /// </summary>
    private static void TraceInstruction(uint pc, bool useImem, ulong instruction, uint result)
    {
        if (!InstructionTraceEnabled && !MicrocodeTraceEnabled)
            return;
        
        string disasm = Disassembler.DisassembleInst2(instruction, useImem);
        string memType = useImem ? "IMEM" : (PromEnabledFlag ? "PROM" : "IMEM");
        
        string traceLine = $"[{MachineCycles:D10}] PC={pc:X4}({memType}) {disasm} => OUT=0x{result:X8} " +
                          $"FLAGS={FormatFlags()} " +
                          $"M={MData:X} A={AData:X}";
        
        // Output to console if enabled
        if (InstructionTraceEnabled)
        {
            Console.WriteLine(traceLine);
        }
        
        // Add to trace buffer
        if (MicrocodeTraceEnabled)
        {
            _traceBuffer.Enqueue(traceLine);
            
            // Limit buffer size
            while (_traceBuffer.Count > MaxTraceLines)
            {
                _traceBuffer.Dequeue();
            }
        }
        
        // Also log to TraceLog if available
        TraceLog.Instance?.Trace(TraceCategory.MicroCode, TraceLevel.Verbose, traceLine);
    }
    
    /// <summary>
    /// Format processor flags as string
    /// </summary>
    private static string FormatFlags()
    {
        return $"C={BoolToChar(CarryFlag)}V={BoolToChar(OverflowFlag)}" +
               $"N={BoolToChar(NegativeFlag)}Z={BoolToChar(ZeroFlag)}";
    }
    
    /// <summary>
    /// Convert bool to single character
    /// </summary>
    private static char BoolToChar(bool value)
    {
        return value ? '1' : '0';
    }
    
    /// <summary>
    /// Get trace buffer contents
    /// </summary>
    public static string[] GetTraceBuffer()
    {
        return _traceBuffer.ToArray();
    }
    
    /// <summary>
    /// Clear trace buffer
    /// </summary>
    public static void ClearTraceBuffer()
    {
        _traceBuffer.Clear();
    }
    
    /// <summary>
    /// Dump trace buffer to console
    /// </summary>
    public static void DumpTraceBuffer()
    {
        Console.WriteLine("=== Microcode Trace Buffer ===");
        Console.WriteLine($"Lines: {_traceBuffer.Count}");
        Console.WriteLine();
        
        foreach (var line in _traceBuffer)
        {
            Console.WriteLine(line);
        }
    }
    
    /// <summary>
    /// Save trace buffer to file
    /// </summary>
    public static void SaveTraceBuffer(string filename)
    {
        try
        {
            System.IO.File.WriteAllLines(filename, _traceBuffer);
            Console.WriteLine($"Trace buffer saved to {filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving trace buffer: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get current instruction as string for debugging
    /// </summary>
    public static string GetCurrentInstructionString()
    {
        if (Iwr != 0)
        {
            return Disassembler.DisassembleInst(Iwr);
        }
        return "[No instruction in IWR]";
    }
    
    /// <summary>
    /// Dump microcode state for debugging
    /// </summary>
    public static void DumpState()
    {
        Console.WriteLine("=== Microcode State ===");
        Console.WriteLine($"Cycles: {MachineCycles}");
        Console.WriteLine($"PC: {Npc:X4}  OPC: {Opc:X4}");
        Console.WriteLine($"Out: {Out:X8}  Q: {Q:X8}");
        Console.WriteLine($"VMA: {VmaReg:X8}  MD: {MdReg:X8}");
        Console.WriteLine($"LC: {Lc:X8}");
        Console.WriteLine($"PDL Ptr: {PdlPointer:X3}  SPC Ptr: {SpcPtr:X2}");
        Console.WriteLine($"Flags: C={CarryFlag} V={OverflowFlag} N={NegativeFlag} Z={ZeroFlag}");
        Console.WriteLine($"Interrupts: {(InterruptPendingFlag ? "PENDING" : "None")} (SR={InterruptStatusReg:X})");
        Console.WriteLine($"IWR: {GetCurrentInstructionString()}");
    }
    
    /// <summary>
    /// Dump A memory contents
    /// </summary>
    public static void DumpAMem(uint start, uint count)
    {
        Console.WriteLine($"=== A Memory [{start:X3}..{start+count-1:X3}] ===");
        for (uint i = 0; i < count; i++)
        {
            uint addr = (start + i) & 0x3FF;
            if (i % 4 == 0)
                Console.Write($"{addr:X3}: ");
            Console.Write($"{AMem[addr]:X8} ");
            if (i % 4 == 3)
                Console.WriteLine();
        }
        if (count % 4 != 0)
            Console.WriteLine();
    }
    
    /// <summary>
    /// Dump M memory contents
    /// </summary>
    public static void DumpMMem()
    {
        Console.WriteLine("=== M Memory ===");
        for (int i = 0; i < MMEM_SIZE; i++)
        {
            if (i % 4 == 0)
                Console.Write($"M[{i:X2}]: ");
            Console.Write($"{MMem[i]:X8} ");
            if (i % 4 == 3)
                Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Dump PDL stack
    /// </summary>
    public static void DumpPdlStack(int depth)
    {
        Console.WriteLine($"=== PDL Stack (top {depth}) ===");
        Console.WriteLine($"PDL Pointer: {PdlPointer:X3}");
        for (int i = 0; i < depth && i <= PdlPointer; i++)
        {
            uint addr = (PdlPointer - (uint)i) & 0x3FF;
            Console.WriteLine($"  [{i}] @{addr:X3}: {Pdl[addr]:X8}");
        }
    }
    
    #endregion
    
    #region Performance Statistics
    
    public static ulong TotalInstructions { get; set; }
    public static ulong TotalAluOps { get; set; }
    public static ulong TotalMemoryAccesses { get; set; }
    public static ulong TotalJumps { get; set; }
    public static ulong TotalInterrupts { get; set; }
    
    /// <summary>
    /// Reset performance counters
    /// </summary>
    public static void ResetStats()
    {
        TotalInstructions = 0;
        TotalAluOps = 0;
        TotalMemoryAccesses = 0;
        TotalJumps = 0;
        TotalInterrupts = 0;
    }
    
    /// <summary>
    /// Print performance statistics
    /// </summary>
    public static void PrintStats()
    {
        Console.WriteLine("=== Performance Statistics ===");
        Console.WriteLine($"Total Cycles:          {MachineCycles:N0}");
        Console.WriteLine($"Total Instructions:    {TotalInstructions:N0}");
        Console.WriteLine($"Total ALU Operations:  {TotalAluOps:N0}");
        Console.WriteLine($"Total Memory Accesses: {TotalMemoryAccesses:N0}");
        Console.WriteLine($"Total Jumps:           {TotalJumps:N0}");
        Console.WriteLine($"Total Interrupts:      {TotalInterrupts:N0}");
        
        if (MachineCycles > 0)
        {
            double ipc = (double)TotalInstructions / MachineCycles;
            Console.WriteLine($"Instructions per Cycle: {ipc:F3}");
        }
    }
    
    #endregion
}
