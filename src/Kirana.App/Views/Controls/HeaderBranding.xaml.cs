using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Controls;

/// <summary>"VyaparOS" / "Powered by StackVeil" header branding, shared by the POS and Management
/// shells. Purely static content — see HeaderBranding.xaml for why this has no
/// DependencyProperties and no code-behind logic (HyperlinkButton.NavigateUri handles the
/// StackVeil link on its own).</summary>
public sealed partial class HeaderBranding : UserControl
{
    public HeaderBranding() => InitializeComponent();
}
