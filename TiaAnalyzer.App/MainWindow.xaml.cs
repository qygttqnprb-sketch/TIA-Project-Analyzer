using System.Windows;
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
    }
}
