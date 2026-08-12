using System.Windows;
using LiveSubtitle.App.Services;
using LiveSubtitle.App.ViewModels;

namespace LiveSubtitle.App.Views;

public partial class FirstRunWindow : Window
{
    private readonly MainViewModel viewModel;
    private int step;

    public FirstRunWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this, viewModel.Theme);
        viewModel.SelectedModel = viewModel.Models.First(model => model.Recommended);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (step > 0) step--;
        UpdateStep();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (step < 3)
        {
            step++;
            UpdateStep();
            return;
        }
        await viewModel.MarkFirstRunCompleteAsync();
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Close();

    private void UpdateStep()
    {
        PrivacyPage.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModelPage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        SourcesPage.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        FinishPage.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = $"{step + 1} of 4";
        Progress.Value = (step + 1) * 25;
        BackButton.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step == 3 ? "Finish" : "Next";
    }
}
