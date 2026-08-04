using Kirana.Application.Barcodes;

namespace Kirana.Tests.Barcodes;

public class ScannerInputBufferTests
{
    private static readonly TimeSpan FastGap = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan SlowGap = TimeSpan.FromMilliseconds(500);

    private static ScannerInputBuffer CreateSut(int minimumLength = 6) =>
        new(maxInterKeyDelay: TimeSpan.FromMilliseconds(40), minimumBarcodeLength: minimumLength);

    private static void FeedFast(ScannerInputBuffer sut, string text, DateTimeOffset start)
    {
        var t = start;
        foreach (var c in text)
        {
            sut.OnCharacter(c, t);
            t += FastGap;
        }

        sut.OnEnterPressed(t);
    }

    [Fact]
    public void FastBurstFollowedByEnter_RaisesBarcodeScanned()
    {
        var sut = CreateSut();
        string? scanned = null;
        sut.BarcodeScanned += code => scanned = code;

        FeedFast(sut, "8901030826501", DateTimeOffset.UtcNow);

        Assert.Equal("8901030826501", scanned);
    }

    [Fact]
    public void SlowHumanTyping_DoesNotRaiseBarcodeScanned()
    {
        var sut = CreateSut();
        var raised = false;
        sut.BarcodeScanned += _ => raised = true;

        var t = DateTimeOffset.UtcNow;
        foreach (var c in "8901030826501")
        {
            sut.OnCharacter(c, t);
            t += SlowGap;
        }

        sut.OnEnterPressed(t);

        Assert.False(raised);
    }

    [Fact]
    public void BurstShorterThanMinimumLength_DoesNotRaiseBarcodeScanned()
    {
        var sut = CreateSut(minimumLength: 6);
        var raised = false;
        sut.BarcodeScanned += _ => raised = true;

        FeedFast(sut, "123", DateTimeOffset.UtcNow);

        Assert.False(raised);
    }

    [Fact]
    public void SlowGapMidBurst_ResetsAndOnlyKeepsTrailingFastPortion()
    {
        var sut = CreateSut(minimumLength: 3);
        string? scanned = null;
        sut.BarcodeScanned += code => scanned = code;

        var t = DateTimeOffset.UtcNow;
        sut.OnCharacter('A', t);
        t += SlowGap; // huge gap — should discard the 'A' and restart the burst
        sut.OnCharacter('B', t);
        t += FastGap;
        sut.OnCharacter('C', t);
        t += FastGap;
        sut.OnCharacter('D', t);
        t += FastGap;
        sut.OnEnterPressed(t);

        Assert.Equal("BCD", scanned);
    }

    [Fact]
    public void EnterWithoutAnyCharacters_DoesNotRaise()
    {
        var sut = CreateSut();
        var raised = false;
        sut.BarcodeScanned += _ => raised = true;

        sut.OnEnterPressed(DateTimeOffset.UtcNow);

        Assert.False(raised);
    }

    [Fact]
    public void Reset_DiscardsInProgressBurst()
    {
        var sut = CreateSut(minimumLength: 3);
        var raised = false;
        sut.BarcodeScanned += _ => raised = true;

        var t = DateTimeOffset.UtcNow;
        sut.OnCharacter('A', t);
        sut.OnCharacter('B', t += FastGap);
        sut.OnCharacter('C', t += FastGap);
        sut.Reset();
        sut.OnEnterPressed(t += FastGap);

        Assert.False(raised);
    }

    [Fact]
    public void AfterSuccessfulScan_BufferIsReadyForNextScan()
    {
        var sut = CreateSut(minimumLength: 3);
        var results = new List<string>();
        sut.BarcodeScanned += code => results.Add(code);

        var t = DateTimeOffset.UtcNow;
        FeedFast(sut, "AAA", t);
        t = t.AddSeconds(1);
        FeedFast(sut, "BBB", t);

        Assert.Equal(["AAA", "BBB"], results);
    }

    // --- OnEnterPressed's return value ---
    // Callers (POS cart entry, purchase entry) use this to decide whether the Enter was already
    // consumed as a scan. Getting it wrong double-adds the scanned product, so it's covered
    // explicitly rather than left implicit in the event-raising tests above.

    [Fact]
    public void OnEnterPressed_ReturnsTrue_WhenBurstIsRecognizedAsScan()
    {
        var sut = CreateSut(minimumLength: 3);

        var t = DateTimeOffset.UtcNow;
        foreach (var c in "8901030826501")
        {
            sut.OnCharacter(c, t);
            t += FastGap;
        }

        Assert.True(sut.OnEnterPressed(t));
    }

    [Fact]
    public void OnEnterPressed_ReturnsFalse_ForSlowHumanTyping()
    {
        var sut = CreateSut(minimumLength: 3);

        var t = DateTimeOffset.UtcNow;
        foreach (var c in "salt")
        {
            sut.OnCharacter(c, t);
            t += SlowGap;
        }

        Assert.False(sut.OnEnterPressed(t));
    }

    [Fact]
    public void OnEnterPressed_ReturnsFalse_ForBurstShorterThanMinimumLength()
    {
        var sut = CreateSut(minimumLength: 6);

        var t = DateTimeOffset.UtcNow;
        foreach (var c in "123")
        {
            sut.OnCharacter(c, t);
            t += FastGap;
        }

        Assert.False(sut.OnEnterPressed(t));
    }

    [Fact]
    public void OnEnterPressed_ReturnsFalse_WhenNoCharactersWereTyped()
    {
        var sut = CreateSut();

        Assert.False(sut.OnEnterPressed(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OnEnterPressed_ReturnValueAgreesWithWhetherBarcodeScannedWasRaised()
    {
        var sut = CreateSut(minimumLength: 3);
        var raisedCount = 0;
        sut.BarcodeScanned += _ => raisedCount++;

        var t = DateTimeOffset.UtcNow;
        foreach (var c in "ABCDEF")
        {
            sut.OnCharacter(c, t);
            t += FastGap;
        }

        var handled = sut.OnEnterPressed(t);

        Assert.True(handled);
        Assert.Equal(1, raisedCount);
    }
}
