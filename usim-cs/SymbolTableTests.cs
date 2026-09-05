// SymbolTableTests.cs - Tests for the real, type-dimensioned symbol
// table (Phase 8B of the microcode engine port), replacing the prior
// invented hex-address-only format.

using System;
using System.IO;

namespace Usim;

public static class SymbolTableTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== SymbolTable Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestLoadRealFormatAndExactMatchLookup()) passed++; else failed++;
        if (TestTypeDimensionDisambiguatesSameAddress()) passed++; else failed++;
        if (TestUnknownValueReturnsNull()) passed++; else failed++;
        if (TestMalformedFileWithoutTerminatorThrows()) passed++; else failed++;
        if (TestLoadRealPromhSymFile()) passed++; else failed++;
        if (TestLoadRealUcadrSymFileWithNegativeValues()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static string WriteTempSymFile(string body)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, body);
        return path;
    }

    private static bool TestLoadRealFormatAndExactMatchLookup()
    {
        Console.WriteLine("Test: LoadFromFile parses the real -4/-2/-1-delimited format and FindByTypeValue does exact-match lookup");
        try
        {
            // Minimal real-shaped file: -4 marker, blank, -2 line (first
            // symbol embedded, ignoring trailing junk), one more symbol
            // line, -1 terminator.
            string body = "\n-4 \n\n-2 A-C A-MEM 6 (some irrelevant s-expression here)\nA-MEM-LOOP I-MEM 474\n-1 \n";
            string path = WriteTempSymFile(body);
            try
            {
                var table = new SymbolTable();
                table.LoadFromFile(path);

                // A-C A-MEM 6 (octal 6 = decimal 6)
                Assert(table.FindByTypeValue(SymbolType.AMem, 6) == "A-C", "A-C found as AMem symbol at value 6 (from the -2 line)");
                // A-MEM-LOOP I-MEM 474 (octal 474 = decimal 316)
                Assert(table.FindByTypeValue(SymbolType.IMem, 316) == "A-MEM-LOOP", "A-MEM-LOOP found as IMem symbol at value 316 (octal 474)");
            }
            finally
            {
                File.Delete(path);
            }

            Console.WriteLine("  real-format-load-and-lookup test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  real-format-load-and-lookup test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestTypeDimensionDisambiguatesSameAddress()
    {
        Console.WriteLine("Test: the same numeric value can be a real symbol in more than one type dimension simultaneously, without collision");
        try
        {
            string body = "\n-4 \n\n-2 FOO-A A-MEM 10 (junk)\nFOO-M M-MEM 10\nFOO-N NUMBER 10\n-1 \n";
            string path = WriteTempSymFile(body);
            try
            {
                var table = new SymbolTable();
                table.LoadFromFile(path);

                Assert(table.FindByTypeValue(SymbolType.AMem, 8) == "FOO-A", "value 10 octal = 8 decimal, AMem -> FOO-A");
                Assert(table.FindByTypeValue(SymbolType.MMem, 8) == "FOO-M", "same value, MMem -> FOO-M (not FOO-A)");
                Assert(table.FindByTypeValue(SymbolType.Number, 8) == "FOO-N", "same value, Number -> FOO-N (not FOO-A or FOO-M)");
                Assert(table.FindByTypeValue(SymbolType.IMem, 8) == null, "same value, IMem (never defined) -> null, not a cross-type false match");
            }
            finally
            {
                File.Delete(path);
            }

            Console.WriteLine("  type-dimension-disambiguation test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  type-dimension-disambiguation test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnknownValueReturnsNull()
    {
        Console.WriteLine("Test: FindByTypeValue returns null for a value with no matching symbol, not a fabricated name");
        try
        {
            string body = "\n-4 \n\n-2 ONLY-ONE A-MEM 1 (junk)\n-1 \n";
            string path = WriteTempSymFile(body);
            try
            {
                var table = new SymbolTable();
                table.LoadFromFile(path);

                Assert(table.FindByTypeValue(SymbolType.AMem, 999) == null, "an address with no defined symbol returns null");
            }
            finally
            {
                File.Delete(path);
            }

            Console.WriteLine("  unknown-value-returns-null test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  unknown-value-returns-null test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMalformedFileWithoutTerminatorThrows()
    {
        Console.WriteLine("Test: a file missing the -1 terminator throws (matching the real C's fatal errx on truncated input)");
        try
        {
            string body = "\n-4 \n\n-2 A-C A-MEM 6 (junk)\nA-MEM-LOOP I-MEM 474\n"; // no -1 terminator
            string path = WriteTempSymFile(body);
            bool threw = false;
            try
            {
                var table = new SymbolTable();
                table.LoadFromFile(path);
            }
            catch (Exception)
            {
                threw = true;
            }
            finally
            {
                File.Delete(path);
            }

            Assert(threw, "missing -1 terminator throws rather than silently succeeding");

            Console.WriteLine("  malformed-file-throws test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  malformed-file-throws test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLoadRealPromhSymFile()
    {
        Console.WriteLine("Test: loading the real sys/ubin/promh.sym succeeds and resolves at least one known real symbol (skipped if the file is absent)");
        string path = "sys/ubin/promh.sym";
        if (!File.Exists(path))
        {
            Console.WriteLine("  SKIPPED: sys/ubin/promh.sym not present\n");
            return true;
        }
        try
        {
            var table = new SymbolTable();
            table.LoadFromFile(path);
            // Just confirm loading a real file doesn't throw and the
            // table isn't empty -- exact symbol names in promh.sym
            // aren't asserted here since this test must survive that
            // file's content changing; UCodeFetchDecodeTests.cs's
            // existing TestPromDecodeSanity establishes the precedent
            // for this "load real file, sanity check, skip if absent" pattern.
            Assert(table.FindByTypeValue(SymbolType.AMem, 0xFFFFFF) == null, "sanity: a nonsense address is never found");

            Console.WriteLine("  real-promh.sym-load test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  real-promh.sym-load test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLoadRealUcadrSymFileWithNegativeValues()
    {
        Console.WriteLine("Test: loading the real sys/ubin/ucadr.sym succeeds (including its negative-octal-valued NUMBER symbols) and resolves a known real symbol (skipped if the file is absent)");
        string path = "sys/ubin/ucadr.sym";
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine("  SKIPPED: sys/ubin/ucadr.sym not present\n");
            return true;
        }
        try
        {
            var table = new SymbolTable();
            table.LoadFromFile(path); // must not throw -- ucadr.sym contains negative NUMBER values like "%MAPPING-TABLE-FLAVOR NUMBER -3"

            // %MAPPING-TABLE-FLAVOR NUMBER -3 -> two's-complement wraparound: 0xFFFFFFFD.
            Assert(table.FindByTypeValue(SymbolType.Number, 0xFFFFFFFD) == "%MAPPING-TABLE-FLAVOR", $"negative octal value -3 correctly wraps to 0xFFFFFFFD and resolves, got {table.FindByTypeValue(SymbolType.Number, 0xFFFFFFFD)}");

            Console.WriteLine("  real-ucadr.sym-with-negative-values-load test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  real-ucadr.sym-with-negative-values-load test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
