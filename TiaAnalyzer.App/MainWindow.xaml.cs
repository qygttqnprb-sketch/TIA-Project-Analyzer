using System;
using System.Threading.Tasks;
using System.Windows;
using TiaAnalyzer.Core;
using TiaAnalyzer.Openness;
using TiaAnalyzer.Export;
namespace TiaAnalyzer.App {
 public partial class MainWindow : Window {
  private bool checking;
  private IReadOnlyTiaSession session;
  public MainWindow() { InitializeComponent(); ExportStatus.Text=ExportAvailability.Description; Loaded+=async(s,e)=>await CheckEnvironmentAsync(); }
  private async void CheckClicked(object sender,RoutedEventArgs e) { await CheckEnvironmentAsync(); }
  private async void ModeChanged(object sender,RoutedEventArgs e) { if(!IsLoaded)return; session=null; SessionStatus.Text="Disconnesso"; await CheckEnvironmentAsync(); }
  private async Task CheckEnvironmentAsync() {
   if(checking)return;
   checking=true; CheckButton.IsEnabled=false; MockMode.IsEnabled=false; ConnectButton.IsEnabled=false;
   bool mock=MockMode.IsChecked==true;
   EnvironmentStatus.Text="Verifica in corso…";
   LibraryPath.Text=mock?"MOCK • nessun accesso alle DLL Siemens":SiemensEnvironmentProbe.LibraryPath;
   try {
    IEnvironmentProbe probe=mock?(IEnvironmentProbe)new MockEnvironmentProbe():new SiemensEnvironmentProbe();
    var report=await Task.Run(()=>probe.Inspect());
    Checks.ItemsSource=report.Checks;
    EnvironmentStatus.Text=mock?"MOCK · ambiente simulato":report.Available?"Librerie V21 disponibili · accesso TIA da verificare":"Ambiente incompleto · consultare i controlli";
    LastCheck.Text="Ultimo controllo: "+DateTime.Now.ToString("HH:mm:ss");
    ConnectButton.Content=mock?"Connetti istanza Mock":"Connessione reale · prossima fase";
    ConnectButton.IsEnabled=mock;
   } catch(Exception ex) {
    EnvironmentStatus.Text="Verifica non riuscita";
    Checks.ItemsSource=new[]{new EnvironmentCheck{Name="Diagnostica",Status="ERRORE",Detail=ex.Message}};
   } finally { checking=false; CheckButton.IsEnabled=true; MockMode.IsEnabled=true; }
  }
  private void ConnectClicked(object sender,RoutedEventArgs e) { if(MockMode.IsChecked!=true)return; session=new MockTiaSession(); SessionStatus.Text=session.Description; }
 }
}