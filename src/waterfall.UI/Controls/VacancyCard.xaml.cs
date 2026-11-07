using System.ComponentModel;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

public partial class VacancyCard : ContentView
{
    private VacancyItemViewModel? _currentViewModel;

    public VacancyCard()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_currentViewModel != null)
        {
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentViewModel = BindingContext as VacancyItemViewModel;

        if (_currentViewModel != null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateMapSource();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VacancyItemViewModel.MapHtml) ||
            e.PropertyName == nameof(VacancyItemViewModel.IsMapExpanded))
        {
            UpdateMapSource();
        }
    }

    private void UpdateMapSource()
    {
        if (_currentViewModel != null &&
            _currentViewModel.IsMapExpanded &&
            !string.IsNullOrEmpty(_currentViewModel.MapHtml))
        {
            if (CardMapWebView != null)
            {
                CardMapWebView.Source = new HtmlWebViewSource { Html = _currentViewModel.MapHtml };
            }
        }
    }
}

