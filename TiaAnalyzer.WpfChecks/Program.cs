using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TiaAnalyzer.App;
using TiaAnalyzer.App.ViewModels;
using TiaAnalyzer.Openness;

internal static class Program
{
    private static int exitCode;
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length == 0 ? Path.Combine("artifacts", "wpf-" + Guid.NewGuid().ToString("N")) : args[0]);
        Directory.CreateDirectory(output);
        var bindingErrors = new StringWriter();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        OpennessRuntime.Configure();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Render a real WPF visual tree off screen without taking focus from the user.
        var window = new MainWindow { ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 960 };
        window.Loaded += async (sender, eventArgs) =>
        {
            try { await Run(window, output); listener.Flush(); Require(bindingErrors.ToString().Length == 0, "no WPF binding errors: " + bindingErrors); }
            catch (Exception error) { exitCode = 1; Console.Error.WriteLine(error); }
            finally
            {
                try { window.Close(); await Until(() => !window.IsVisible); }
                catch (Exception error) { exitCode = 1; Console.Error.WriteLine(error); }
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose();
                app.Shutdown(exitCode);
            }
        };
        app.Run(window);
        return exitCode;
    }
    private static async Task Run(MainWindow window, string output)
    {
        var vm = (MainViewModel)window.DataContext;
        await Until(() => !vm.IsBusy && vm.CheckCommand.CanExecute(null));
        Require(((ItemsControl)window.FindName("Checks")).Items.Count == 7, "real diagnostic bindings");
        Require(!((Button)window.FindName("ConnectButton")).IsEnabled && !((Button)window.FindName("ExportZipButton")).IsEnabled, "no automatic Attach or export");
        ((CheckBox)window.FindName("MockMode")).IsChecked = true; await Until(() => !vm.IsBusy); await Dispatcher.Yield();
        Require(vm.IsDemo && ((ItemsControl)window.FindName("Checks")).Items.Count == 3, "demo mode binding");
        await vm.ScanCommand.ExecuteAsync(); await Dispatcher.Yield();
        var instances = (ComboBox)window.FindName("InstancesList");
        Require(instances.Items.Count == 1 && vm.SelectedInstance == null, "discovery requires explicit selection");
        instances.SelectedIndex = 0; await Dispatcher.Yield();
        Require(((Button)window.FindName("ConnectButton")).IsEnabled, "selected demo enables connection");
        await vm.ConnectCommand.ExecuteAsync(); await Dispatcher.Yield();
        Require(((ItemsControl)window.FindName("ComponentsList")).Items.Count == 6, "six synthetic components rendered");
        vm.ExportRoot = output;
        vm.ReuseXml = true;
        await vm.ExportZipCommand.ExecuteAsync(); await Dispatcher.Yield();
        Require(File.Exists(vm.LastZipPath) && ((TextBlock)window.FindName("ExportStatus")).Text.Contains("ZIP:"), "ZIP command and result bindings");
        var firstZip = vm.LastZipPath;
        await vm.ExportZipCommand.ExecuteAsync(); await Dispatcher.Yield();
        Require(vm.PerformanceSummary.Contains("XML riusati 5") && vm.PerformanceSummary.Contains("cache analisi 6"), "warm cache and performance bindings");
        vm.SearchTerm = "SafetyOK"; vm.SearchSymbolsCommand.Execute(null); await Dispatcher.Yield();
        Require(vm.SymbolMatches.Count == 3, "symbol query binds declaration, XML and demo cross-reference");
        vm.PreviousSnapshotPath = firstZip; await vm.CompareCommand.ExecuteAsync(); await Dispatcher.Yield();
        Require(vm.ComparisonStatus.StartsWith("0 differenze"), "comparison command completes on WPF dispatcher");
        var scroll = (ScrollViewer)window.FindName("MainScroll");
        scroll.ScrollToTop(); await Dispatcher.Yield(); Capture(window, Path.Combine(output, "overview.png"));
        var exportCard = (FrameworkElement)window.FindName("ExportCard");
        var offset = exportCard.TranslatePoint(new Point(), scroll).Y + scroll.VerticalOffset;
        scroll.ScrollToVerticalOffset(offset); await Dispatcher.Yield(); Capture(window, Path.Combine(output, "export.png"));
        scroll.ScrollToEnd(); await Dispatcher.Yield(); Capture(window, Path.Combine(output, "comparison.png"));
        window.Width = 900; window.UpdateLayout();
        offset = exportCard.TranslatePoint(new Point(), scroll).Y + scroll.VerticalOffset;
        scroll.ScrollToVerticalOffset(offset); await Dispatcher.Yield(); Capture(window, Path.Combine(output, "export-900.png"));
        await vm.DisconnectCommand.ExecuteAsync(); await Dispatcher.Yield();
        Require(((ItemsControl)window.FindName("ComponentsList")).Items.Count == 0 && !((Button)window.FindName("ExportZipButton")).IsEnabled, "disconnect clears components and disables export");
        Console.WriteLine("Artifacts: " + output);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = new FileStream(path, FileMode.CreateNew)) encoder.Save(stream);
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF test timed out."); await Task.Delay(10); }
        await Dispatcher.Yield();
    }
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
