namespace Kirana.Application.Barcodes;

/// <summary>
/// Detects a completed scan from raw keystrokes (PRD §17: USB scanners act as a keyboard —
/// "keyboard-wedge" — so a scan looks like a very fast burst of characters terminated by Enter).
/// UI-framework-agnostic: a WinUI page's KeyDown handler feeds characters in; this reusable
/// buffer decides whether the burst was a scan and raises <see cref="BarcodeScanned"/>.
/// Also reusable, unmodified, by the future POS phase's cart-entry scanning.
/// </summary>
public interface IScannerInputBuffer
{
    /// <summary>Raised with the completed barcode once a fast, Enter-terminated burst is detected.</summary>
    event Action<string>? BarcodeScanned;

    /// <summary>Feed one typed/scanned character plus the moment it arrived.</summary>
    void OnCharacter(char character, DateTimeOffset timestampUtc);

    /// <summary>
    /// Feed an Enter keypress — the conventional scanner-input terminator. Returns <c>true</c> if
    /// the burst was recognized as a scan (and <see cref="BarcodeScanned"/> was therefore raised),
    /// so callers can tell scanner input apart from a human pressing Enter in a search box.
    /// Callers MUST check this before also treating the Enter as a manual search submission —
    /// otherwise a real scan is handled twice, adding the same product to the cart/purchase twice.
    /// </summary>
    bool OnEnterPressed(DateTimeOffset timestampUtc);

    /// <summary>Discards any in-progress, unterminated burst (e.g. the user clicked away).</summary>
    void Reset();
}
