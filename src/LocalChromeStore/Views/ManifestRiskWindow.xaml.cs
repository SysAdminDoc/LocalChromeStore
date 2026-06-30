using System.Windows;
using LocalChromeStore.ViewModels;

namespace LocalChromeStore.Views;

public partial class ManifestRiskWindow : Window
{
    public bool InstallRequested { get; private set; }

    public ManifestRiskWindow(ManifestRiskViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public static bool? Show(Window owner, Models.ExtensionInfo info, Models.InstalledExtension? installed, out bool installRequested)
    {
        var requested = false;
        ManifestRiskWindow? wnd = null;
        var vm = new ManifestRiskViewModel(info,
            onInstall: () => { requested = true; wnd?.Close(); },
            onClose: () => wnd?.Close(),
            installed: installed);
        wnd = new ManifestRiskWindow(vm) { Owner = owner };
        var result = wnd.ShowDialog();
        installRequested = requested;
        return result;
    }
}
