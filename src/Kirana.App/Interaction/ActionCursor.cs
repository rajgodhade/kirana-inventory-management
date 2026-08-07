using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using System.Reflection;

namespace Kirana.App.Interaction;

/// <summary>Marks a non-button element as an actionable surface for the window-level hand cursor.</summary>
public static class ActionCursor
{
    private static readonly PropertyInfo? ProtectedCursorProperty = typeof(UIElement).GetProperty(
        "ProtectedCursor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly InputSystemCursor HandCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public static readonly DependencyProperty IsActionableProperty = DependencyProperty.RegisterAttached(
        "IsActionable",
        typeof(bool),
        typeof(ActionCursor),
        new PropertyMetadata(false, OnIsActionableChanged));

    public static bool GetIsActionable(DependencyObject element) => (bool)element.GetValue(IsActionableProperty);

    public static void SetIsActionable(DependencyObject element, bool value) => element.SetValue(IsActionableProperty, value);

    internal static void ApplyHandCursor(UIElement element)
    {
        try
        {
            // ProtectedCursor is intentionally protected in WinUI. Applying it here keeps the
            // cursor owned by the framework, so it persists across routed pointer events.
            ProtectedCursorProperty?.SetValue(element, HandCursor);
        }
        catch
        {
            // Cursor decoration must never interfere with the underlying control action.
        }
    }

    private static void OnIsActionableChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is true && sender is UIElement element)
        {
            ApplyHandCursor(element);
        }
    }
}
