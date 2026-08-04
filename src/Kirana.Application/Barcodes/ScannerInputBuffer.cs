using System.Text;

namespace Kirana.Application.Barcodes;

/// <summary>
/// Default <see cref="IScannerInputBuffer"/>: a burst counts as a scan when every character
/// arrived within <see cref="_maxInterKeyDelay"/> of the previous one, Enter arrived just as
/// quickly after the last character, and the total length meets <see cref="_minimumBarcodeLength"/>.
/// Real scanners emit characters only a few milliseconds apart; sustained human typing rarely does.
/// </summary>
public sealed class ScannerInputBuffer(TimeSpan? maxInterKeyDelay = null, int minimumBarcodeLength = 6) : IScannerInputBuffer
{
    private readonly TimeSpan _maxInterKeyDelay = maxInterKeyDelay ?? TimeSpan.FromMilliseconds(40);
    private readonly int _minimumBarcodeLength = minimumBarcodeLength;
    private readonly StringBuilder _buffer = new();
    private DateTimeOffset? _lastCharacterTimestamp;

    public event Action<string>? BarcodeScanned;

    public void OnCharacter(char character, DateTimeOffset timestampUtc)
    {
        if (_lastCharacterTimestamp is { } last && timestampUtc - last > _maxInterKeyDelay)
        {
            Reset();
        }

        _buffer.Append(character);
        _lastCharacterTimestamp = timestampUtc;
    }

    public bool OnEnterPressed(DateTimeOffset timestampUtc)
    {
        var arrivedQuicklyAfterLastCharacter =
            _lastCharacterTimestamp is { } last && timestampUtc - last <= _maxInterKeyDelay;

        var isScan = arrivedQuicklyAfterLastCharacter && _buffer.Length >= _minimumBarcodeLength;
        if (isScan)
        {
            BarcodeScanned?.Invoke(_buffer.ToString());
        }

        Reset();
        return isScan;
    }

    public void Reset()
    {
        _buffer.Clear();
        _lastCharacterTimestamp = null;
    }
}
