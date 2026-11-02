using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TiaAnalyzer.Core;
namespace TiaAnalyzer.Openness {
 public sealed class SiemensEnvironmentProbe : IEnvironmentProbe {
  public const string LibraryPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
  private readonly string libraryPath;
  public SiemensEnvironmentProbe() : this(LibraryPath) {}
  public SiemensEnvironmentProbe(string libraryPath) { this.libraryPath=libraryPath ?? throw new ArgumentNullException(nameof(libraryPath)); }
  public EnvironmentReport Inspect() {
   var checks = new List<EnvironmentCheck>();
   bool available = Environment.Is64BitProcess;
   checks.Add(new EnvironmentCheck {Name="Processo applicazione",Status=available?"OK":"ERRORE",Detail=available?"Processo x64 attivo":"Avviare in x64"});
   bool directoryExists=Directory.Exists(libraryPath);
   available &= directoryExists;
   checks.Add(new EnvironmentCheck {Name="Public API V21 / net48",Status=directoryExists?"OK":"ERRORE",Detail=libraryPath});
   foreach(string file in new[]{"Siemens.Engineering.Base.dll","Siemens.Engineering.Step7.dll"}) {
    try {
     // Metadata inspection only; does not load or execute Siemens assemblies.
     var identity=AssemblyName.GetAssemblyName(Path.Combine(libraryPath,file));
     bool valid=identity.Name==Path.GetFileNameWithoutExtension(file) && identity.Version!=null && identity.Version.Major==21;
     available &= valid;
     checks.Add(new EnvironmentCheck {Name=file,Status=valid?"OK":"ERRORE",Detail="Identità: "+identity.Name+" • versione: "+identity.Version});
    } catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is System.Security.SecurityException) {
     available=false; checks.Add(new EnvironmentCheck{Name=file,Status="ERRORE",Detail=ex.Message});
    }
   }
   checks.Add(new EnvironmentCheck{Name="Accesso Openness",Status="DA VERIFICARE",Detail="Le DLL non garantiscono l'accesso. Autorizzazione e collegamento a un'istanza aperta saranno verificati con la futura connessione reale."});
   checks.Add(new EnvironmentCheck{Name="Protezione progetto",Status="SOLA LETTURA",Detail="Nessuna apertura, modifica, compilazione o salvataggio di progetti TIA."});
   return new EnvironmentReport{Available=available,Checks=checks};
  }
 }
 public sealed class MockEnvironmentProbe : IEnvironmentProbe {
  public EnvironmentReport Inspect() { return new EnvironmentReport{Available=true,Checks=new List<EnvironmentCheck>{
   new EnvironmentCheck{Name="Ambiente simulato",Status="MOCK",Detail="Nessuna DLL Siemens utilizzata. Nessun accesso a TIA."},
   new EnvironmentCheck{Name="Public API V21",Status="MOCK",Detail="Base e Step7 disponibili (simulazione)."},
   new EnvironmentCheck{Name="Istanza dimostrativa",Status="MOCK",Detail="Linea confezionamento • progetto simulato in sola lettura"}
  }}; }
 }
}
