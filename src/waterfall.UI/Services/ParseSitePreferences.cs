using System.ComponentModel;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Services;

/// <summary>
/// Persists the user's selected parse sites on <see cref="CatalogViewModel"/> across app launches
/// without altering default property initializers used by client unit tests.
/// </summary>
internal static class ParseSitePreferences
{
    private const string InitializedKey = "waterfall.parse.initialized";
    private const string DouKey = "waterfall.parse.dou";
    private const string WorkUaKey = "waterfall.parse.workua";
    private const string DjinniKey = "waterfall.parse.djinni";
    private const string RobotaUaKey = "waterfall.parse.robotaua";

    private static CatalogViewModel? _attachedViewModel;
    private static bool _isApplying;

    public static void Attach(CatalogViewModel viewModel)
    {
        if (ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        if (_attachedViewModel is not null)
        {
            _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _attachedViewModel = viewModel;
        Restore(viewModel);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private static void Restore(CatalogViewModel viewModel)
    {
        _isApplying = true;
        try
        {
            bool initialized = false;
            bool dou = true;
            bool workUa = true;
            bool djinni = true;
            bool robotaUa = false;

            try
            {
                initialized = Preferences.Default.Get(InitializedKey, false);
                if (initialized)
                {
                    dou = Preferences.Default.Get(DouKey, true);
                    workUa = Preferences.Default.Get(WorkUaKey, true);
                    djinni = Preferences.Default.Get(DjinniKey, true);
                    robotaUa = Preferences.Default.Get(RobotaUaKey, false);
                }
            }
            catch
            {
                initialized = false;
            }

            if (initialized && dou && !workUa && !djinni && !robotaUa)
            {
                // Force PropertyChanged once so explicit user choice isn't overwritten when expanding another site accordion.
                viewModel.IsDouParseEnabled = false;
            }

            viewModel.IsDouParseEnabled = dou;
            viewModel.IsWorkUaParseEnabled = workUa;
            viewModel.IsDjinniParseEnabled = djinni;
            viewModel.IsRobotaUaParseEnabled = robotaUa;

            if (!initialized)
            {
                Save(viewModel);
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    private static void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplying || sender is not CatalogViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(CatalogViewModel.IsDouParseEnabled)
            or nameof(CatalogViewModel.IsWorkUaParseEnabled)
            or nameof(CatalogViewModel.IsDjinniParseEnabled)
            or nameof(CatalogViewModel.IsRobotaUaParseEnabled))
        {
            Save(vm);
        }
    }

    private static void Save(CatalogViewModel vm)
    {
        try
        {
            Preferences.Default.Set(DouKey, vm.IsDouParseEnabled);
            Preferences.Default.Set(WorkUaKey, vm.IsWorkUaParseEnabled);
            Preferences.Default.Set(DjinniKey, vm.IsDjinniParseEnabled);
            Preferences.Default.Set(RobotaUaKey, vm.IsRobotaUaParseEnabled);
            Preferences.Default.Set(InitializedKey, true);
        }
        catch
        {
            // Ignore Preferences persistence errors in unsupported/unpackaged hosts.
        }
    }
}
