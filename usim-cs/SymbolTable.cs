// SymbolTable.cs - Type-dimensioned symbol table for microcode
// disassembly, ported from usim/usym.c/usym.h. Real file format
// (verified against sys/ubin/promh.sym / sys/ubin/ucadr.sym and against
// usim/usym.c's sym_read_file() directly):
//
//   line 0: a leading blank line (always discarded, unread).
//   line 1: the "-4 " assembler-state marker line (must match exactly).
//   line 2: a single assembler-state S-expression line, always
//           discarded unconditionally -- this is NOT a blank line to
//           skip past; in every real .sym file it is one long non-blank
//           line, and sym_read_file() reads exactly one line here via a
//           fixed xgetline() call, never searching for "-2".
//   line 3: "-2 <name> <type> <octal-value> ..." -- the first symbol,
//           embedded directly in the "-2" marker line. Only the first
//           three whitespace-separated tokens after "-2" matter; any
//           trailing text (a large S-expression in real files) is never
//           consumed by the real C's sscanf(l, "-2 %s %s %o", ...) and
//           is dropped here too.
//   lines 4..: one "<name> <type> <octal-value>" symbol per line, until
//           a line whose trimmed content is exactly "-1" (EOF marker).
//           A line that doesn't parse as three tokens is silently
//           skipped here, matching the real C's `if (n != 3) continue;`
//           -- it is NOT a fatal error, unlike a missing "-1" terminator
//           (which is fatal in both the real C's trailing errx() and
//           here).
using System;
using System.Collections.Generic;
using System.IO;

namespace Usim;

/// <summary>
/// Matches usim/usym.h's symtype_t exactly -- values are deliberately
/// non-contiguous, not a bitmask, not a typo.
/// </summary>
public enum SymbolType
{
    IMem = 1,
    DMem = 2,
    AMem = 4,
    MMem = 5,
    Number = 6,
}

public class SymbolTable
{
    private readonly Dictionary<(SymbolType Type, uint Value), string> _byTypeAndValue = new();

    public void LoadFromFile(string filename)
    {
        string[] lines = File.ReadAllLines(filename);
        int i = 0;

        // Line 0: leading blank line, discarded unconditionally.
        if (i >= lines.Length)
        {
            throw new FormatException($"{filename}: empty symbol file");
        }
        i++;

        // Line 1: "-4" assembler-state marker.
        if (i >= lines.Length || lines[i].Trim() != "-4")
        {
            throw new FormatException($"{filename}: missing '-4' assembler-state marker line");
        }
        i++;

        // Line 2: assembler-state S-expression line, discarded
        // unconditionally -- do NOT search for "-2" here; the real
        // format has exactly one such line, whatever its content.
        if (i >= lines.Length)
        {
            throw new FormatException($"{filename}: truncated after '-4' marker line");
        }
        i++;

        // Line 3: "-2 <name> <type> <octal-value> ..." -- first symbol.
        if (i >= lines.Length || !lines[i].TrimStart().StartsWith("-2"))
        {
            throw new FormatException($"{filename}: missing '-2' first-symbol line");
        }
        AddSymbolFromTokens(SplitFirstThreeTokensAfterMarker(lines[i]), filename);
        i++;

        // One symbol per line until "-1".
        bool sawTerminator = false;
        for (; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed == "-1")
            {
                sawTerminator = true;
                break;
            }
            string[] tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                // Matches the real C's `if (n != 3) continue;` -- a
                // line that doesn't scan as three tokens (e.g. a blank
                // line) is silently skipped, not a fatal error.
                continue;
            }
            AddSymbolFromTokens(tokens, filename);
        }

        if (!sawTerminator)
        {
            throw new FormatException($"{filename}: missing '-1' terminator line -- truncated symbol file");
        }
    }

    private static string[] SplitFirstThreeTokensAfterMarker(string line)
    {
        // line looks like "-2 NAME TYPE OCTALVAL <ignored s-expression...>"
        string[] all = line.TrimStart().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (all.Length < 4)
        {
            throw new FormatException($"malformed '-2' line: {line}");
        }
        return new[] { all[1], all[2], all[3] };
    }

    private void AddSymbolFromTokens(string[] tokens, string filename)
    {
        string name = tokens[0];
        SymbolType type = tokens[1] switch
        {
            "I-MEM" => SymbolType.IMem,
            "D-MEM" => SymbolType.DMem,
            "A-MEM" => SymbolType.AMem,
            "M-MEM" => SymbolType.MMem,
            "NUMBER" => SymbolType.Number,
            _ => throw new FormatException($"{filename}: unknown symbol type '{tokens[1]}' for symbol '{name}'"),
        };
        uint value = Convert.ToUInt32(tokens[2], 8); // real-file addresses are octal
        _byTypeAndValue[(type, value)] = name;
    }

    public string? FindByTypeValue(SymbolType type, uint value) =>
        _byTypeAndValue.TryGetValue((type, value), out string? name) ? name : null;
}
