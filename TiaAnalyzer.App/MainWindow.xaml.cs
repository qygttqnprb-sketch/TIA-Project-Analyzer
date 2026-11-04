using System.Windows;
using Microsoft.Win32;
using TiaAnalyzer.App.ViewModels;

namespace TiaAnalyzer.App
{
    public partial class MainWindow : Window
    {
        private bool released;
        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.InitializeAsync();
            Closing += async (sender, args) =>
            {
                if (released) return;
                args.Cancel = true;
                if (await viewModel.CloseAsync()) { released = true; Close(); }
            };
        }
        private void BrowsePreviousSnapshot(object sender, RoutedEventArgs args) { BrowseSnapshot(true); }
        private void BrowseCurrentSnapshot(object sender, RoutedEventArgs args) { BrowseSnapshot(false); }
        private void BrowseSnapshot(bool previous)
        {
            var dialog = new OpenFileDialog { Title = previous ? "Snapshot precedente" : "Snapshot successivo",
                Filter = "Snapshot TIA (*.zip;manifest.json)|*.zip;manifest.json|Manifest JSON (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            var vm = (MainViewModel)DataContext;
            if (previous) vm.PreviousSnapshotPath = dialog.FileName; else vm.CurrentSnapshotPath = dialog.FileName;
        }
    }
}
