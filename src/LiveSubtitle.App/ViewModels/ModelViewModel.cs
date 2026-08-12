using LiveSubtitle.Core;

namespace LiveSubtitle.App.ViewModels;

public sealed class ModelViewModel : ObservableObject
{
    private bool installed;
    private bool verified;
    private bool downloading;
    private double progress;
    private string state = "Not installed";

    public ModelViewModel(ModelDefinition definition)
    {
        Definition = definition;
    }

    public ModelDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;
    public string SizeLabel => $"{Definition.ApproximateBytes / 1024d / 1024d:0} MB";
    public bool Recommended => Definition.Recommended;
    public bool Installed { get => installed; set => Set(ref installed, value); }
    public bool Verified { get => verified; set => Set(ref verified, value); }
    public bool Downloading { get => downloading; set => Set(ref downloading, value); }
    public double Progress { get => progress; set => Set(ref progress, value); }
    public string State { get => state; set => Set(ref state, value); }
}
