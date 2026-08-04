namespace Kirana.Domain.Barcodes;

/// <summary>
/// GS1 EAN-13 check-digit computation and validation (PRD §16-17). Pure math, no
/// infrastructure dependency, so it can be used from Domain/Application/Infrastructure alike.
/// </summary>
public static class Ean13
{
    public const int Length = 13;

    /// <summary>Computes the 13th (check) digit for the first 12 digits of an EAN-13 code.</summary>
    public static char ComputeCheckDigit(ReadOnlySpan<char> first12Digits)
    {
        if (first12Digits.Length != 12)
        {
            throw new ArgumentException("EAN-13 requires exactly 12 digits before the check digit.", nameof(first12Digits));
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            if (!char.IsAsciiDigit(first12Digits[i]))
            {
                throw new ArgumentException("EAN-13 digits must be numeric.", nameof(first12Digits));
            }

            var digit = first12Digits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        var checkDigit = (10 - sum % 10) % 10;
        return (char)('0' + checkDigit);
    }

    /// <summary>Appends a computed check digit to 12 digits, returning the full 13-digit code.</summary>
    public static string BuildWithCheckDigit(string first12Digits) =>
        first12Digits + ComputeCheckDigit(first12Digits.AsSpan());

    /// <summary>True when <paramref name="candidate"/> is exactly 13 digits with a matching check digit.</summary>
    public static bool IsValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length != Length)
        {
            return false;
        }

        foreach (var c in candidate)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return candidate[12] == ComputeCheckDigit(candidate.AsSpan(0, 12));
    }
}
