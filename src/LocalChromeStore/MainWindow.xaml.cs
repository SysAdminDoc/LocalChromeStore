using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using LocalChromeStore.ViewModels;

namespace LocalChromeStore;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => HookLogAutoScroll();
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsDrawer.Visibility = SettingsDrawer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void HookLogAutoScroll()
    {
        if (DataContext is not MainViewModel vm) return;
        ((INotifyCollectionChanged)vm.LogLines).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        };
    }
}
