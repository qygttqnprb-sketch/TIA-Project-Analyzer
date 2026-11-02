using System.Windows;
using TiaAnalyzer.App.ViewModels;

namespace TiaAnalyzer.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.InitializeAsync();
        }
    }
}
