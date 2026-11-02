using System.Windows;
using TiaAnalyzer.Openness;

namespace TiaAnalyzer.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            OpennessRuntime.Configure();
            base.OnStartup(e);
        }
    }
}
