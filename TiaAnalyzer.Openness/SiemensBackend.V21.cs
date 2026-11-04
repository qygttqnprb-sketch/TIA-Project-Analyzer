#if SIEMENS_V21
// Compile and validate this file on Windows against the installed V21 net48 API.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public sealed partial class SiemensBackend : ITiaBackend
    {
        public bool IsAvailable { get { return true; } }
        public IReadOnlyList<TiaInstance> Discover()
        {
            try
            {
                return TiaPortal.GetProcesses().Select(process => new TiaInstance
                {
                    Id = process.Id,
                    ExecutablePath = process.Path == null ? null : process.Path.FullName,
                    Version = process.Path == null ? "Non disponibile" : FileVersionInfo.GetVersionInfo(process.Path.FullName).FileVersion,
                    ProjectPath = process.ProjectPath == null ? null : process.ProjectPath.FullName
                }).ToList().AsReadOnly();
            }
            catch (EngineeringSecurityException ex) { throw Denied(ex); }
            catch (System.Security.SecurityException ex) { throw Denied(ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(ex); }
            catch (Exception ex) { throw new TiaConnectionException(ConnectionFailure.ApiUnavailable, "Rilevamento TIA non riuscito: " + ex.Message, ex); }
        }
        public IAttachedPortal Attach(TiaInstance instance)
        {
            try
            {
                var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == instance.Id);
                if (process == null || process.Path == null || !string.Equals(process.Path.FullName, instance.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    throw new TiaConnectionException(ConnectionFailure.InstanceUnavailable, "Istanza non più disponibile. Ripetere il rilevamento e selezionare il PID.");
                // Siemens owns the standard Openness authorization prompt. Never auto-approve it.
                return new AttachedPortal(process.Attach());
            }
            catch (EngineeringSecurityException ex) { throw Denied(ex); }
            catch (System.Security.SecurityException ex) { throw Denied(ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(ex); }
            catch (TiaConnectionException) { throw; }
            catch (Exception ex) { throw new TiaConnectionException(ConnectionFailure.InstanceUnavailable, "Attach non riuscito: " + ex.Message, ex); }
        }
        private static TiaConnectionException Denied(Exception ex)
        {
            return new TiaConnectionException(ConnectionFailure.AuthorizationDenied,
                "Accesso Openness rifiutato. " + OpennessAuthorization.Recovery, ex);
        }
        private sealed partial class AttachedPortal : IAttachedPortal, IPlcExportSession
        {
            private TiaPortal portal;
            public AttachedPortal(TiaPortal portal) { this.portal = portal; }
            public IReadOnlyList<ProjectComponent> ReadComponents()
            {
                if (portal == null) throw new ObjectDisposedException(nameof(AttachedPortal));
                try
                {
                    var result = new List<ProjectComponent>();
                    foreach (Project project in portal.Projects)
                    {
                        result.Add(new ProjectComponent { Name = project.Name, Kind = "Progetto già aperto", Project = project.Name,
                            Path = project.Path == null ? null : project.Path.FullName, Status = "LETTO" });
                        var visited = new HashSet<Device>();
                        foreach (Device device in project.Devices) ReadDevice(device, project.Name, result, visited);
                        foreach (DeviceUserGroup group in project.DeviceGroups) ReadGroup(group, project.Name, result, visited);
                        foreach (Device device in project.UngroupedDevicesGroup.Devices) ReadDevice(device, project.Name, result, visited);
                    }
                    return result.AsReadOnly();
                }
                catch (EngineeringSecurityException ex) { throw Denied(ex); }
                catch (Exception ex) { throw new TiaConnectionException(ConnectionFailure.ReadFailed, "Lettura dei progetti aperti non riuscita: " + ex.Message, ex); }
            }
            private static void ReadGroup(DeviceUserGroup group, string project, List<ProjectComponent> result, HashSet<Device> visited)
            {
                foreach (Device device in group.Devices) ReadDevice(device, project, result, visited);
                foreach (DeviceUserGroup child in group.Groups) ReadGroup(child, project, result, visited);
            }
            private static void ReadDevice(Device device, string project, List<ProjectComponent> result, HashSet<Device> visited)
            {
                if (!visited.Add(device)) return;
                result.Add(new ProjectComponent { Name = device.Name, Kind = "Dispositivo", Project = project, Path = device.Name, Status = "LETTO" });
                foreach (DeviceItem item in device.DeviceItems) ReadItem(item, project, device.Name, result);
            }
            private static void ReadItem(DeviceItem item, string project, string parent, List<ProjectComponent> result)
            {
                var path = parent + "/" + item.Name;
                var container = item.GetService<SoftwareContainer>();
                var plc = container == null ? null : container.Software as PlcSoftware;
                result.Add(new ProjectComponent { Name = item.Name, Kind = plc == null ? "Elemento hardware" : "PLC / STEP 7",
                    Project = project, Path = path, Status = "LETTO" });
                if (plc != null)
                    result.Add(new ProjectComponent { Name = plc.Name, Kind = "Software STEP 7", Project = project, Path = path + "/" + plc.Name, Status = "LETTO" });
                foreach (DeviceItem child in item.DeviceItems) ReadItem(child, project, path, result);
            }
            public void Dispose()
            {
                if (portal == null) return;
                // Dispose of an ATTACHED handle detaches; no Project.Close or process termination.
                portal.Dispose();
                portal = null;
            }
        }
    }
}
#endif
