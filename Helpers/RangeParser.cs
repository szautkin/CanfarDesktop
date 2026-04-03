using System.Diagnostics.CodeAnalysis;

namespace CanfarDesktop.Helpers;

public enum RangeOperand
{
    Equals,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Between
}

public record ParsedRange(RangeOperand Operand, string Value1, string? Value2 = null);

public static class RangeParser
{
    public static bool TryParse(string? input, [NotNullWhen(true)] out ParsedRange? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();

        // Range: "A..B"
        var dotDot = trimmed.IndexOf("..", StringComparison.Ordinal);
        if (dotDot >= 0)
        {
            var left = trimmed[..dotDot].Trim();
            var right = trimmed[(dotDot + 2)..].Trim();
            if (left.Length > 0 && right.Length > 0)
            {
                result = new ParsedRange(RangeOperand.Between, left, right);
                return true;
            }
        }

        // Two-char operators first
        if (trimmed.StartsWith("<="))
        {
            var val = trimmed[2..].Trim();
            if (val.Length > 0) { result = new ParsedRange(RangeOperand.LessThanOrEqual, val); return true; }
        }
        if (trimmed.StartsWith(">="))
        {
            var val = trimmed[2..].Trim();
            if (val.Length > 0) { result = new ParsedRange(RangeOperand.GreaterThanOrEqual, val); return true; }
        }

        // Single-char operators
        if (trimmed.StartsWith('<'))
        {
            var val = trimmed[1..].Trim();
            if (val.Length > 0) { result = new ParsedRange(RangeOperand.LessThan, val); return true; }
        }
        if (trimmed.StartsWith('>'))
        {
            var val = trimmed[1..].Trim();
            if (val.Length > 0) { result = new ParsedRange(RangeOperand.GreaterThan, val); return true; }
        }

        // Plain value
        result = new ParsedRange(RangeOperand.Equals, trimmed);
        return true;
    }
}
