namespace Kirana.Domain.Entities;

public enum HardwareType
{
    Printer,
    ThermalPrinter,
    A4Printer,
    BarcodeScanner,
    UsbHidScanner,
    BluetoothHidScanner,
    VirtualScanner,
    CashDrawer,
    CustomerDisplay,
    WeightScale,
    PaymentTerminal,
    MobileScanner,
    QrPaymentDevice,
}

public enum HardwareStatus
{
    Connected,
    Disconnected,
    Error,
    Busy,
    Offline,
    Unknown,
}

[Flags]
public enum HardwareCapability
{
    None = 0,
    PrintReceipt = 1,
    PrintA4 = 2,
    ScanBarcode = 4,
    OpenCashDrawer = 8,
    DisplayCustomerTotal = 16,
    ReadWeight = 32,
    AcceptPayment = 64,
    RemoteInput = 128,
}

public enum HardwareConnectionType
{
    Unknown,
    WindowsPrintQueue,
    Usb,
    Bluetooth,
    KeyboardWedge,
    Network,
    Virtual,
}

public enum PrinterPaperSize
{
    Thermal58mm,
    Thermal80mm,
    A4,
}

/// <summary>
/// Runtime description of a physical or virtual device. Discovery state is deliberately not an
/// EF entity: Windows owns the device catalogue, while only user preferences belong in Kirana's
/// database.
/// </summary>
public sealed record HardwareDevice
{
    public required string DeviceId { get; init; }
    public required HardwareType Type { get; init; }
    public required string FriendlyName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public HardwareConnectionType ConnectionType { get; init; }
    public HardwareStatus Status { get; init; } = HardwareStatus.Unknown;
    public HardwareCapability Capabilities { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public bool IsDefault { get; init; }
    public string? ErrorMessage { get; init; }
}
