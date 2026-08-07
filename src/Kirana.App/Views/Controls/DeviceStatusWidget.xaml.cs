using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Controls;

public sealed partial class DeviceStatusWidget : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DeviceStatusWidget), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(DeviceStatusWidget), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(DeviceStatusWidget), new PropertyMetadata("\uE7BA"));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(HardwareStatus), typeof(DeviceStatusWidget), new PropertyMetadata(HardwareStatus.Unknown));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public HardwareStatus Status { get => (HardwareStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    public DeviceStatusWidget() => InitializeComponent();
}
