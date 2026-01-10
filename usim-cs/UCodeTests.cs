// UCodeTests.cs - Microcode engine tests and examples
// Demonstrates the CADR microcode execution capabilities

using System;

namespace Usim;

/// <summary>
/// Test suite and examples for the microcode execution engine
/// </summary>
public static class UCodeTests
{
    /// <summary>
    /// Run all microcode tests
    /// </summary>
    public static void RunAllTests()
    {
        Console.WriteLine("=== CADR Microcode Engine Test Suite ===\n");
        
        int passed = 0;
        int failed = 0;
        
        // Run tests
        if (TestAluOperations()) passed++; else failed++;
        if (TestBarrelShifter()) passed++; else failed++;
        if (TestMemoryOperations()) passed++; else failed++;
        if (TestStackOperations()) passed++; else failed++;
        if (TestProcessorFlags()) passed++; else failed++;
        if (TestInstructionDecode()) passed++; else failed++;
        if (TestJumpConditions()) passed++; else failed++;
        if (TestPipeline()) passed++; else failed++;
        
        // Summary
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
    
    /// <summary>
    /// Test ALU operations
    /// </summary>
    public static bool TestAluOperations()
    {
        Console.WriteLine("Test: ALU Operations");
        UCode.Init();
        
        try
        {
            // Test logic operations
            uint result;
            
            // AND
            result = UCode.ExecuteAlu(UCode.AluOp.And, 0xFF00, 0x00FF, false);
            Assert(result == 0x0000, "AND operation");
            
            result = UCode.ExecuteAlu(UCode.AluOp.And, 0xFFFF, 0x0F0F, false);
            Assert(result == 0x0F0F, "AND operation 2");
            
            // OR
            result = UCode.ExecuteAlu(UCode.AluOp.Or, 0xFF00, 0x00FF, false);
            Assert(result == 0xFFFF, "OR operation");
            
            // XOR
            result = UCode.ExecuteAlu(UCode.AluOp.Xor, 0xFFFF, 0x0F0F, false);
            Assert(result == 0xF0F0, "XOR operation");
            
            // Test arithmetic operations
            
            // Addition
            result = UCode.ExecuteAlu(UCode.AluOp.Add, 0x1000, 0x2000, false);
            Assert(result == 0x3000, "Addition");
            
            result = UCode.ExecuteAlu(UCode.AluOp.Add, 0x1000, 0x2000, true);
            Assert(result == 0x3001, "Addition with carry");
            
            // Subtraction
            result = UCode.ExecuteAlu(UCode.AluOp.Sub, 0x3000, 0x1000, false);
            Assert(result == 0x1FFF, "Subtraction");
            
            // Increment/Decrement
            result = UCode.ExecuteAlu(UCode.AluOp.M_Plus_1, 0x1234, 0, false);
            Assert(result == 0x1235, "Increment");
            
            result = UCode.ExecuteAlu(UCode.AluOp.M_Minus_1, 0x1234, 0, false);
            Assert(result == 0x1233, "Decrement");
            
            // Complement operations
            result = UCode.ExecuteAlu(UCode.AluOp.SetCA, 0, 0x00FF, false);
            Assert(result == 0xFFFFFF00, "Complement A");
            
            result = UCode.ExecuteAlu(UCode.AluOp.SetCM, 0x00FF, 0, false);
            Assert(result == 0xFFFFFF00, "Complement M");
            
            Console.WriteLine("  ? ALU Operations tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? ALU Operations tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test barrel shifter operations
    /// </summary>
    public static bool TestBarrelShifter()
    {
        Console.WriteLine("Test: Barrel Shifter");
        
        try
        {
            uint value = 0x80000001;
            uint result;
            
            // Logical shift left
            result = UCode.BarrelShift(value, 1, 1);
            Assert(result == 0x00000002, "LSL by 1");
            
            result = UCode.BarrelShift(0x00000001, 1, 8);
            Assert(result == 0x00000100, "LSL by 8");
            
            // Logical shift right
            result = UCode.BarrelShift(0x80000000, 2, 1);
            Assert(result == 0x40000000, "LSR by 1");
            
            result = UCode.BarrelShift(0xFF000000, 2, 8);
            Assert(result == 0x00FF0000, "LSR by 8");
            
            // Arithmetic shift right (sign extend)
            result = UCode.BarrelShift(0x80000000, 3, 1);
            Assert(result == 0xC0000000, "ASR by 1");
            
            result = UCode.BarrelShift(0x80000000, 3, 8);
            Assert(result == 0xFF800000, "ASR by 8");
            
            // Rotate left
            result = UCode.BarrelShift(0x80000001, 4, 1);
            Assert(result == 0x00000003, "ROL by 1");
            
            result = UCode.BarrelShift(0x12345678, 4, 8);
            Assert(result == 0x34567812, "ROL by 8");
            
            // Rotate right
            result = UCode.BarrelShift(0x80000001, 5, 1);
            Assert(result == 0xC0000000, "ROR by 1");
            
            result = UCode.BarrelShift(0x12345678, 5, 8);
            Assert(result == 0x78123456, "ROR by 8");
            
            Console.WriteLine("  ? Barrel Shifter tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Barrel Shifter tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test memory operations
    /// </summary>
    public static bool TestMemoryOperations()
    {
        Console.WriteLine("Test: Memory Operations");
        UCode.Init();
        
        try
        {
            // A Memory
            UCode.WriteAMem(0x100, 0xDEADBEEF);
            uint value = UCode.ReadAMem(0x100);
            Assert(value == 0xDEADBEEF, "A Memory read/write");
            
            // M Memory
            UCode.WriteMMem(5, 0x12345678);
            value = UCode.ReadMMem(5);
            Assert(value == 0x12345678, "M Memory read/write");
            
            // D Memory
            UCode.WriteDMem(0x400, 0xABCDEF01);
            value = UCode.ReadDMem(0x400);
            Assert(value == 0xABCDEF01, "D Memory read/write");
            
            // Test address masking
            UCode.WriteAMem(0x3FF, 0x11111111);
            value = UCode.ReadAMem(0x3FF);
            Assert(value == 0x11111111, "A Memory boundary");
            
            // Test wrap-around
            UCode.WriteAMem(0x400, 0x22222222); // Should wrap to 0
            value = UCode.ReadAMem(0x000);
            Assert(value == 0x22222222, "A Memory wrap-around");
            
            Console.WriteLine("  ? Memory Operations tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Memory Operations tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test stack operations
    /// </summary>
    public static bool TestStackOperations()
    {
        Console.WriteLine("Test: Stack Operations");
        UCode.Init();
        
        try
        {
            // PDL Stack
            UCode.PushPdl(0x1111);
            UCode.PushPdl(0x2222);
            UCode.PushPdl(0x3333);
            
            uint value = UCode.PopPdl();
            Assert(value == 0x3333, "PDL pop 1");
            
            value = UCode.PopPdl();
            Assert(value == 0x2222, "PDL pop 2");
            
            value = UCode.PopPdl();
            Assert(value == 0x1111, "PDL pop 3");
            
            // SPC Stack
            UCode.PushSpc(0xAAAA);
            UCode.PushSpc(0xBBBB);
            
            value = UCode.ReadSpc();
            Assert(value == 0xBBBB, "SPC read");
            
            value = UCode.PopSpc();
            Assert(value == 0xBBBB, "SPC pop 1");
            
            value = UCode.PopSpc();
            Assert(value == 0xAAAA, "SPC pop 2");
            
            // Test PDL indexed read
            UCode.PushPdl(0x1000);
            UCode.PushPdl(0x2000);
            UCode.PushPdl(0x3000);
            
            value = UCode.ReadPdl(0); // Current position (top of stack)
            Assert(value == 0x3000, "PDL indexed read 0");
            
            value = UCode.ReadPdl(1); // One position back (second from top)
            Assert(value == 0x2000, "PDL indexed read 1");
            
            value = UCode.ReadPdl(2); // Two positions back (third from top)
            Assert(value == 0x1000, "PDL indexed read 2");
            
            Console.WriteLine("  ? Stack Operations tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Stack Operations tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test processor flags
    /// </summary>
    public static bool TestProcessorFlags()
    {
        Console.WriteLine("Test: Processor Flags");
        UCode.Init();
        
        try
        {
            uint result;
            
            // Zero flag
            result = UCode.ExecuteAlu(UCode.AluOp.Sub, 0x1000, 0x1000, true);
            UCode.UpdateFlags(result, 0x1000, 0x1000, UCode.AluOp.Sub);
            Assert(result == 0x00000000, "Zero flag result");
            Assert(UCode.ZeroFlag == true, "Zero flag set");
            
            result = UCode.ExecuteAlu(UCode.AluOp.Add, 0x0001, 0x0000, false);
            UCode.UpdateFlags(result, 0x0001, 0x0000, UCode.AluOp.Add);
            Assert(UCode.ZeroFlag == false, "Zero flag clear");
            
            // Negative flag
            result = UCode.ExecuteAlu(UCode.AluOp.SetM, 0x80000000, 0, false);
            UCode.UpdateFlags(result, 0x80000000, 0, UCode.AluOp.SetM);
            Assert(UCode.NegativeFlag == true, "Negative flag set");
            
            result = UCode.ExecuteAlu(UCode.AluOp.SetM, 0x7FFFFFFF, 0, false);
            UCode.UpdateFlags(result, 0x7FFFFFFF, 0, UCode.AluOp.SetM);
            Assert(UCode.NegativeFlag == false, "Negative flag clear");
            
            // Carry flag
            result = UCode.ExecuteAlu(UCode.AluOp.Add, 0xFFFFFFFF, 0x00000001, false);
            UCode.UpdateFlags(result, 0xFFFFFFFF, 0x00000001, UCode.AluOp.Add);
            Assert(UCode.CarryFlag == true, "Carry flag set");
            
            result = UCode.ExecuteAlu(UCode.AluOp.Add, 0x00000001, 0x00000001, false);
            UCode.UpdateFlags(result, 0x00000001, 0x00000001, UCode.AluOp.Add);
            Assert(UCode.CarryFlag == false, "Carry flag clear");
            
            Console.WriteLine("  ? Processor Flags tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Processor Flags tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test instruction decode
    /// </summary>
    public static bool TestInstructionDecode()
    {
        Console.WriteLine("Test: Instruction Decode");
        
        try
        {
            // Build a test instruction
            // Format: [ALU_OP(5)] [M_SRC(5)] [A_SRC(10)] [DEST(5)] [JUMP(5)] [NEXT_PC(14)]
            ulong instruction = 0;
            
            // Set ALU op to Add (16)
            instruction = MiscUtils.DepositByte(instruction, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, 16);
            
            // Set M source to 5
            instruction = MiscUtils.DepositByte(instruction, UCode.M_SOURCE_POS, UCode.M_SOURCE_SIZE, 5);
            
            // Set A source to 100
            instruction = MiscUtils.DepositByte(instruction, UCode.A_SOURCE_POS, UCode.A_SOURCE_SIZE, 100);
            
            // Set destination to 4 (VMA)
            instruction = MiscUtils.DepositByte(instruction, UCode.DEST_POS, UCode.DEST_SIZE, 4);
            
            // Set jump condition to 1 (jump if zero)
            instruction = MiscUtils.DepositByte(instruction, UCode.JUMP_COND_POS, UCode.JUMP_COND_SIZE, 1);
            
            // Set next PC to 0x100
            instruction = MiscUtils.DepositByte(instruction, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 0x100);
            
            // Decode and verify
            UCode.AluOp aluOp = UCode.GetAluOp(instruction);
            Assert(aluOp == UCode.AluOp.Add, "Decode ALU op");
            
            uint mSource = UCode.GetMSource(instruction);
            Assert(mSource == 5, "Decode M source");
            
            uint aSource = UCode.GetASource(instruction);
            Assert(aSource == 100, "Decode A source");
            
            uint dest = UCode.GetDest(instruction);
            Assert(dest == 4, "Decode destination");
            
            uint jumpCond = UCode.GetJumpCond(instruction);
            Assert(jumpCond == 1, "Decode jump condition");
            
            uint nextPc = UCode.GetNextPC(instruction);
            Assert(nextPc == 0x100, "Decode next PC");
            
            Console.WriteLine("  ? Instruction Decode tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Instruction Decode tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test jump conditions
    /// </summary>
    public static bool TestJumpConditions()
    {
        Console.WriteLine("Test: Jump Conditions");
        UCode.Init();
        
        try
        {
            // Test unconditional jump
            bool jump = UCode.EvaluateJumpCondition(0, 0);
            Assert(jump == true, "Unconditional jump");
            
            // Test zero flag
            UCode.ZeroFlag = true;
            jump = UCode.EvaluateJumpCondition(1, 0);
            Assert(jump == true, "Jump if zero (true)");
            
            UCode.ZeroFlag = false;
            jump = UCode.EvaluateJumpCondition(1, 0);
            Assert(jump == false, "Jump if zero (false)");
            
            // Test not zero
            UCode.ZeroFlag = false;
            jump = UCode.EvaluateJumpCondition(2, 0);
            Assert(jump == true, "Jump if not zero (true)");
            
            UCode.ZeroFlag = true;
            jump = UCode.EvaluateJumpCondition(2, 0);
            Assert(jump == false, "Jump if not zero (false)");
            
            // Test negative flag
            UCode.NegativeFlag = true;
            jump = UCode.EvaluateJumpCondition(3, 0);
            Assert(jump == true, "Jump if negative (true)");
            
            // Test bit 0
            jump = UCode.EvaluateJumpCondition(9, 0x00000001);
            Assert(jump == true, "Jump if bit 0 set (true)");
            
            jump = UCode.EvaluateJumpCondition(9, 0x00000000);
            Assert(jump == false, "Jump if bit 0 set (false)");
            
            // Test interrupt pending
            UCode.InterruptPendingFlag = true;
            jump = UCode.EvaluateJumpCondition(11, 0);
            Assert(jump == true, "Jump if interrupt (true)");
            
            UCode.InterruptPendingFlag = false;
            jump = UCode.EvaluateJumpCondition(11, 0);
            Assert(jump == false, "Jump if interrupt (false)");
            
            Console.WriteLine("  ? Jump Conditions tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Jump Conditions tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Test pipeline operations
    /// </summary>
    public static bool TestPipeline()
    {
        Console.WriteLine("Test: Pipeline");
        UCode.Init();
        
        try
        {
            // Load some test instructions into IMEM
            UCode.IMem[0] = 0x1111111111111111;
            UCode.IMem[1] = 0x2222222222222222;
            UCode.IMem[2] = 0x3333333333333333;
            
            // Advance pipeline
            UCode.AdvancePipeline(0, true);
            Assert(UCode.P0 == 0x1111111111111111, "Pipeline P0 after advance 1");
            
            UCode.AdvancePipeline(1, true);
            Assert(UCode.P0 == 0x2222222222222222, "Pipeline P0 after advance 2");
            Assert(UCode.P1 == 0x1111111111111111, "Pipeline P1 after advance 2");
            
            UCode.AdvancePipeline(2, true);
            Assert(UCode.P0 == 0x3333333333333333, "Pipeline P0 after advance 3");
            Assert(UCode.P1 == 0x2222222222222222, "Pipeline P1 after advance 3");
            Assert(UCode.Iwr == 0x1111111111111111, "Pipeline IWR after advance 3");
            
            // Test pipeline flush
            UCode.FlushPipeline();
            Assert(UCode.P0 == 0, "Pipeline P0 after flush");
            Assert(UCode.P1 == 0, "Pipeline P1 after flush");
            Assert(UCode.Iwr == 0, "Pipeline IWR after flush");
            
            Console.WriteLine("  ? Pipeline tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? Pipeline tests failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Demonstrate complete instruction execution
    /// </summary>
    public static void DemoInstructionExecution()
    {
        Console.WriteLine("=== Instruction Execution Demo ===\n");
        
        UCode.Init();
        
        // Build an instruction: Add M[5] + A[100] -> VMA
        ulong instruction = 0;
        instruction = MiscUtils.DepositByte(instruction, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.Add);
        instruction = MiscUtils.DepositByte(instruction, UCode.M_SOURCE_POS, UCode.M_SOURCE_SIZE, 5);
        instruction = MiscUtils.DepositByte(instruction, UCode.A_SOURCE_POS, UCode.A_SOURCE_SIZE, 100);
        instruction = MiscUtils.DepositByte(instruction, UCode.DEST_POS, UCode.DEST_SIZE, 4); // VMA
        instruction = MiscUtils.DepositByte(instruction, UCode.JUMP_COND_POS, UCode.JUMP_COND_SIZE, 0); // Unconditional
        instruction = MiscUtils.DepositByte(instruction, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 1);
        
        // Load into IMEM
        UCode.IMem[0] = instruction;
        
        // Set up operands
        UCode.WriteMMem(5, 0x1000);
        UCode.WriteAMem(100, 0x2000);
        
        Console.WriteLine("Before execution:");
        Console.WriteLine($"  M[5] = 0x{UCode.ReadMMem(5):X8}");
        Console.WriteLine($"  A[100] = 0x{UCode.ReadAMem(100):X8}");
        Console.WriteLine($"  VMA = 0x{UCode.VmaReg:X8}");
        Console.WriteLine();
        
        // Execute
        UCode.ExecuteInstruction(0, true);
        
        Console.WriteLine("After execution:");
        Console.WriteLine($"  VMA = 0x{UCode.VmaReg:X8}");
        Console.WriteLine($"  Expected: 0x00003000");
        Console.WriteLine($"  Flags: C={UCode.CarryFlag} V={UCode.OverflowFlag} N={UCode.NegativeFlag} Z={UCode.ZeroFlag}");
        Console.WriteLine();
    }
    
    /// <summary>
    /// Performance benchmark
    /// </summary>
    public static void RunBenchmark()
    {
        Console.WriteLine("=== Performance Benchmark ===\n");
        
        UCode.Init();
        UCode.ResetStats();
        
        // Create a simple loop program
        for (int i = 0; i < 10; i++)
        {
            ulong instruction = 0;
            instruction = MiscUtils.DepositByte(instruction, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.M_Plus_1);
            instruction = MiscUtils.DepositByte(instruction, UCode.M_SOURCE_POS, UCode.M_SOURCE_SIZE, 0);
            instruction = MiscUtils.DepositByte(instruction, UCode.DEST_POS, UCode.DEST_SIZE, 2); // M memory
            instruction = MiscUtils.DepositByte(instruction, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, (ulong)((i + 1) % 10));
            UCode.IMem[i] = instruction;
        }
        
        // Run for 1000 cycles
        var startTime = DateTime.Now;
        for (int i = 0; i < 1000; i++)
        {
            UCode.ExecuteInstruction((uint)(i % 10), true);
            UCode.TotalInstructions++;
        }
        var elapsed = DateTime.Now - startTime;
        
        Console.WriteLine($"Executed 1000 instructions in {elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"Rate: {1000.0 / elapsed.TotalSeconds:F0} instructions/second");
        Console.WriteLine($"Average: {elapsed.TotalMilliseconds / 1000.0:F6} ms/instruction");
        Console.WriteLine();
        
        UCode.PrintStats();
    }
    
    /// <summary>
    /// Demonstrate instruction tracing
    /// </summary>
    public static void DemoInstructionTracing()
    {
        Console.WriteLine("=== Instruction Tracing Demo ===\n");
        
        UCode.Init();
        UCode.ClearTraceBuffer();
        
        // Enable tracing
        UCode.InstructionTraceEnabled = true;
        UCode.MicrocodeTraceEnabled = true;
        UCode.MaxTraceLines = 100;
        
        Console.WriteLine("Building test program...\n");
        
        // Build a small test program
        // Instruction 0: Load 0x1000 into A[100]
        ulong inst0 = 0;
        inst0 = MiscUtils.DepositByte(inst0, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.SetM);
        inst0 = MiscUtils.DepositByte(inst0, UCode.M_SOURCE_POS, UCode.M_SOURCE_SIZE, 0);
        inst0 = MiscUtils.DepositByte(inst0, UCode.DEST_POS, UCode.DEST_SIZE, 1); // A memory
        inst0 = MiscUtils.DepositByte(inst0, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 1);
        UCode.IMem[0] = inst0;
        
        // Instruction 1: Add 0x2000 to A[100]
        ulong inst1 = 0;
        inst1 = MiscUtils.DepositByte(inst1, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.Add);
        inst1 = MiscUtils.DepositByte(inst1, UCode.M_SOURCE_POS, UCode.M_SOURCE_SIZE, 0);
        inst1 = MiscUtils.DepositByte(inst1, UCode.A_SOURCE_POS, UCode.A_SOURCE_SIZE, 100);
        inst1 = MiscUtils.DepositByte(inst1, UCode.DEST_POS, UCode.DEST_SIZE, 4); // VMA
        inst1 = MiscUtils.DepositByte(inst1, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 2);
        UCode.IMem[1] = inst1;
        
        // Instruction 2: Store result to Q
        ulong inst2 = 0;
        inst2 = MiscUtils.DepositByte(inst2, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.SetA);
        inst2 = MiscUtils.DepositByte(inst2, UCode.A_SOURCE_POS, UCode.A_SOURCE_SIZE, 1025); // OUT
        inst2 = MiscUtils.DepositByte(inst2, UCode.DEST_POS, UCode.DEST_SIZE, 7); // Q
        inst2 = MiscUtils.DepositByte(inst2, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 3);
        UCode.IMem[2] = inst2;
        
        // Instruction 3: Halt (jump to self)
        ulong inst3 = 0;
        inst3 = MiscUtils.DepositByte(inst3, UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, (ulong)UCode.AluOp.SetZ);
        inst3 = MiscUtils.DepositByte(inst3, UCode.NEXT_PC_POS, UCode.NEXT_PC_SIZE, 3);
        UCode.IMem[3] = inst3;
        
        // Set up initial values
        UCode.WriteMMem(0, 0x1000);
        UCode.WriteAMem(100, 0x2000);
        
        Console.WriteLine("Executing program with tracing enabled...\n");
        Console.WriteLine("--- Trace Output ---\n");
        
        // Execute instructions
        for (int i = 0; i < 4; i++)
        {
            UCode.ExecuteInstruction((uint)i, true);
        }
        
        Console.WriteLine("\n--- End Trace Output ---\n");
        
        // Show results
        Console.WriteLine("Results:");
        Console.WriteLine($"  VMA = 0x{UCode.VmaReg:X8}");
        Console.WriteLine($"  Q = 0x{UCode.Q:X8}");
        Console.WriteLine($"  Cycles = {UCode.MachineCycles}");
        Console.WriteLine();
        
        // Show trace buffer
        Console.WriteLine("Trace buffer contains {0} lines", UCode.GetTraceBuffer().Length);
        
        // Optionally save trace
        string traceFile = "instruction_trace.txt";
        UCode.SaveTraceBuffer(traceFile);
        Console.WriteLine($"Trace saved to {traceFile}");
        Console.WriteLine();
        
        // Disable tracing
        UCode.InstructionTraceEnabled = false;
        UCode.MicrocodeTraceEnabled = false;
    }
    
    /// <summary>
    /// Assert helper
    /// </summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: {message}");
        }
    }
}
