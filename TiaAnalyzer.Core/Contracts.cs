using System.Collections.Generic;
namespace TiaAnalyzer.Core {
 public sealed class EnvironmentCheck { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
 public sealed class EnvironmentReport { public bool Available {get;set;} public IList<EnvironmentCheck> Checks {get;set;} }
 public interface IEnvironmentProbe { EnvironmentReport Inspect(); }
 // Future implementations must inspect an already open instance without modifying projects.
 public interface IReadOnlyTiaSession { bool IsConnected {get;} string Description {get;} }
}