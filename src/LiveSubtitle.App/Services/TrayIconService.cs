using System.ComponentModel;
using System.Drawing;
using System.Windows;
using LiveSubtitle.App.ViewModels;
using Forms = System.Windows.Forms;

namespace LiveSubtitle.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem listeningItem;
    private readonly Forms.ToolStripMenuItem overlayItem;
    private readonly Forms.ToolStripMenuItem profilesItem;
    private readonly MainViewModel viewModel;
    private readonly Action showWindow;
    private readonly Func<Task> exit;

    public TrayIconService(MainViewModel viewModel, Action showWindow, Func<Task> exit)
    {
        this.viewModel = viewModel;
        this.showWindow = showWindow;
        this.exit = exit;
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LiveSubtitle.ico"));
        var appIcon = resource is null ? SystemIcons.Application : new Icon(resource.Stream);
        icon = new Forms.NotifyIcon
        {
            Text = "LiveSubtitle — not listening",
            Icon = appIcon,
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open LiveSubtitle", null, (_, _) => showWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        listeningItem = new Forms.ToolStripMenuItem("Start listening", null, (_, _) => viewModel.ToggleListeningCommand.Execute(null));
        overlayItem = new Forms.ToolStripMenuItem("Show / hide overlay", null, (_, _) => viewModel.ToggleOverlayCommand.Execute(null));
        profilesItem = new Forms.ToolStripMenuItem("Profiles");
        menu.Items.Add(listeningItem);
        menu.Items.Add(overlayItem);
        menu.Items.Add(profilesItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await exit());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => showWindow();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RebuildProfiles();
    }

    public void NotifyMinimized()
    {
        icon.BalloonTipTitle = "LiveSubtitle is still listening";
        icon.BalloonTipText = "Use the tray icon or Ctrl+Alt+Space to stop. Unsaved text remains in memory only.";
        icon.ShowBalloonTip(3500);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsListening))
        {
            listeningItem.Text = viewModel.IsListening ? "Stop listening" : "Start listening";
            icon.Text = viewModel.IsListening ? "LiveSubtitle — listening" : "LiveSubtitle — not listening";
        }
        if (e.PropertyName == nameof(MainViewModel.SelectedProfile)) RebuildProfiles();
    }

    private void RebuildProfiles()
    {
        profilesItem.DropDownItems.Clear();
        foreach (var profile in viewModel.Profiles)
        {
            var item = new Forms.ToolStripMenuItem(profile.Name)
            {
                Checked = profile.Id == viewModel.SelectedProfile?.Id,
                Tag = profile.Id
            };
            item.Click += (_, _) => viewModel.SwitchProfile((string)item.Tag!);
            profilesItem.DropDownItems.Add(item);
        }
    }

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        icon.Visible = false;
        icon.Dispose();
    }
}
