using System.Text.RegularExpressions;

namespace Kirana.Domain.Taxation;

/// <summary>Single GSTIN validation policy shared by setup, settings and party masters.</summary>
public static partial class GstinValidator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static GstinValidationResult Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new(GstinValidationStatus.Missing);
        }

        var gstin = value.Trim();
        if (!GstinPattern().IsMatch(gstin))
        {
            return new(GstinValidationStatus.StructurallyInvalid, ErrorMessage:
                "GSTIN must be 15 uppercase characters in the standard Indian GST format.");
        }

        var stateCode = gstin[..2];
        if (!IndianGstStateCatalog.IsValidCode(stateCode))
        {
            return new(GstinValidationStatus.StructurallyInvalid, stateCode,
                $"GSTIN starts with unsupported state code '{stateCode}'.");
        }

        if (CalculateCheckCharacter(gstin.AsSpan(0, 14)) != gstin[14])
        {
            return new(GstinValidationStatus.StructurallyInvalid, stateCode,
                "GSTIN checksum is invalid.");
        }

        return new(GstinValidationStatus.Valid, stateCode);
    }

    public static GstIdentityValidationResult ValidateIdentity(string? gstin, string? stateCode)
    {
        var normalizedStateCode = string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim();
        if (normalizedStateCode is not null && !IndianGstStateCatalog.IsValidCode(normalizedStateCode))
        {
            return new(Validate(gstin), $"'{normalizedStateCode}' is not a supported Indian GST state code.");
        }

        var result = Validate(gstin);
        if (result.Status == GstinValidationStatus.StructurallyInvalid)
        {
            return new(result, result.ErrorMessage);
        }

        if (result.IsValid && normalizedStateCode is not null && result.StateCode != normalizedStateCode)
        {
            return new(result,
                $"GSTIN state code {result.StateCode} does not match the selected state code {normalizedStateCode}.");
        }

        return new(result, null);
    }

    public static void EnsureValidForWrite(string? gstin, string? stateCode)
    {
        var result = ValidateIdentity(gstin, stateCode);
        if (!result.IsValid)
        {
            throw new ArgumentException(result.ErrorMessage);
        }
    }

    internal static char CalculateCheckCharacter(ReadOnlySpan<char> firstFourteenCharacters)
    {
        if (firstFourteenCharacters.Length != 14)
        {
            throw new ArgumentException("A GSTIN checksum requires exactly 14 source characters.");
        }

        var factor = 2;
        var sum = 0;
        for (var index = firstFourteenCharacters.Length - 1; index >= 0; index--)
        {
            var character = firstFourteenCharacters[index];
            var codePoint = Alphabet.IndexOf(character);
            if (codePoint < 0)
            {
                throw new ArgumentException("GSTIN contains an unsupported character.");
            }

            var product = factor * codePoint;
            sum += (product / 36) + (product % 36);
            factor = factor == 2 ? 1 : 2;
        }

        return Alphabet[(36 - (sum % 36)) % 36];
    }

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex GstinPattern();
}
