using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LiveSubtitle.App.Controls;

public sealed class SettingHelpLabel : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SettingHelpLabel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingHelpLabel),
        new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}

public sealed class SettingInfoButton : Button
{
    public static readonly DependencyProperty SettingNameProperty = DependencyProperty.Register(
        nameof(SettingName),
        typeof(string),
        typeof(SettingInfoButton),
        new PropertyMetadata(string.Empty, OnHelpPropertyChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingInfoButton),
        new PropertyMetadata(string.Empty, OnHelpPropertyChanged));

    private readonly ToolTip helpToolTip;

    public SettingInfoButton()
    {
        Content = "i";
        helpToolTip = new ToolTip
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = this,
            HorizontalOffset = 6,
            VerticalOffset = 4,
            StaysOpen = true
        };
        ToolTip = helpToolTip;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetBetweenShowDelay(this, 80);
        ToolTipService.SetShowDuration(this, 30000);

        GotKeyboardFocus += OnGotKeyboardFocus;
        LostKeyboardFocus += OnLostKeyboardFocus;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        Click += OnClick;
        UpdateHelp();
    }

    public string SettingName
    {
        get => (string)GetValue(SettingNameProperty);
        set => SetValue(SettingNameProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private static void OnHelpPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((SettingInfoButton)dependencyObject).UpdateHelp();

    private void UpdateHelp()
    {
        if (helpToolTip is null) return;

        helpToolTip.Content = Description;
        AutomationProperties.SetName(this, string.IsNullOrWhiteSpace(SettingName)
            ? "Setting information"
            : $"About {SettingName}");
        AutomationProperties.SetHelpText(this, Description);
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) =>
        helpToolTip.IsOpen = true;

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) =>
        helpToolTip.IsOpen = false;

    private void OnMouseEnter(object sender, MouseEventArgs args) =>
        helpToolTip.IsOpen = true;

    private void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (!IsKeyboardFocusWithin) helpToolTip.IsOpen = false;
    }

    private void OnClick(object sender, RoutedEventArgs args) =>
        helpToolTip.IsOpen = true;
}
