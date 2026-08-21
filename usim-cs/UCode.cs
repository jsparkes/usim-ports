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

    #endregion
    
    // Machine cycles counter
    public ulong MachineCycles { get; set; }

    // Interrupt status
    public int InterruptStatusReg { get; private set; }
    public bool InterruptPendingFlag { get; set; }

    // Microcode memory
    public bool PromEnabledFlag { get; set; }
    public ulong[] Prom { get; } = new ulong[PROM_SIZE];
    public ulong[] IMem { get; } = new ulong[IMEM_SIZE];

    // A, M, and D memories
    public uint[] AMem { get; } = new uint[AMEM_SIZE];
    public uint[] MMem { get; } = new uint[MMEM_SIZE];
    public uint[] DMem { get; } = new uint[DMEM_SIZE];

    // Push-down list (stack)
    public uint[] Pdl { get; } = new uint[PDL_SIZE];

    // Stack pointer cache
    public uint[] Spc { get; } = new uint[SPC_SIZE];
    public uint SpcPtr { get; set; }

    // Named registers
    public uint DispatchConstant { get; set; }
    public uint PdlPointer { get; set; }
    public uint PdlIndex { get; set; }
    public uint VmaReg { get; set; }
    public uint MdReg { get; set; }
    public uint Lc { get; set; }
    public uint OaRegHigh { get; set; }
    public uint OaRegLow { get; set; }
    public uint Opc { get; set; }
    public uint Q { get; set; }
    public uint OldQ { get; set; }
    public uint InterruptControl { get; set; }

    // Pipeline registers
    public ulong P0 { get; set; }
    public uint P0Pc { get; set; }
    public bool P0Imem { get; set; }

    public ulong P1 { get; set; }
    public uint P1Pc { get; set; }
    public bool P1Imem { get; set; }

    public ulong Iwr { get; set; }

    public uint Npc { get; set; }

    // Latched operands for the current cycle
    public uint AAddr { get; set; }
    public int AData { get; set; }
    public uint MAddr { get; set; }
    public int MData { get; set; }

    public uint Out { get; set; }

    public bool Inhibit { get; set; }
    public bool UExecHasRunOnce { get; set; }

    // Decoded common fields for the current cycle
    public uint Op { get; set; }
    public bool Popj { get; set; }

    // Pending delayed memory read
    public uint NewMd { get; set; }
    public uint NewMdDelay { get; set; }

    // ALU result/carry for the current cycle (real hardware has no
    // persistent flags register — these replace the old Carry/Overflow/
    // Negative/Zero flags, which were an invented artifact)
    public uint AluCarry { get; set; }
    public uint AluOut { get; set; }

    // OA-register pending-merge flags
    public bool Oal { get; set; }
    public bool Oah { get; set; }

    /// <summary>
    /// Read len bits of P0 starting at bit pos (mirrors uexec.c's ir()).
    /// </summary>
    private ulong Ir(int pos, int len)
    {
        return (P0 >> pos) & ((1UL << len) - 1);
    }

    /// <summary>
    /// Initialize the microcode system
    /// </summary>
    public void Init()
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

        SpcPtr = 0;
        DispatchConstant = 0;
        PdlPointer = 0;
        PdlIndex = 0;
        VmaReg = 0;
        MdReg = 0;
        Lc = 0;
        OaRegHigh = 0;
        OaRegLow = 0;
        Opc = 0;
        Q = 0;
        OldQ = 0;
        InterruptControl = 0;

        P0 = 0;
        P0Pc = 0;
        P0Imem = false;

        P1 = 0;
        P1Pc = 0;
        P1Imem = false;

        Iwr = 0;
        Npc = 0;

        AAddr = 0;
        AData = 0;
        MAddr = 0;
        MData = 0;

        Out = 0;
        Inhibit = false;
        UExecHasRunOnce = false;

        Op = 0;
        Popj = false;
        NewMd = 0;
        NewMdDelay = 0;
        AluCarry = 0;
        AluOut = 0;
        Oal = false;
        Oah = false;
    }

    /// <summary>
    /// Load PROM from file
    /// </summary>
    public void LoadPromFromFile(string filename)
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
    public void SetInterruptStatusReg(int newValue)
    {
        InterruptStatusReg = newValue;
        InterruptPendingFlag = (newValue != 0);
    }

    /// <summary>
    /// Assert Unibus interrupt
    /// </summary>
    public void AssertUnibusInterrupt(int level)
    {
        InterruptStatusReg |= (1 << level);
        InterruptPendingFlag = true;
    }

    /// <summary>
    /// Deassert Unibus interrupt
    /// </summary>
    public void DeassertUnibusInterrupt()
    {
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
    }

    /// <summary>
    /// Assert Xbus interrupt
    /// </summary>
    public void AssertXbusInterrupt()
    {
        InterruptPendingFlag = true;
    }

    /// <summary>
    /// Deassert Xbus interrupt
    /// </summary>
    public void DeassertXbusInterrupt()
    {
        InterruptPendingFlag = false;
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
