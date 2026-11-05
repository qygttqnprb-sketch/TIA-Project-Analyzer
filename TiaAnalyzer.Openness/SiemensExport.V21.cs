#if SIEMENS_V21
using System;
using System.Collections.Generic;
using System.Threading;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public sealed partial class SiemensBackend
    {
        private sealed partial class AttachedPortal
        {
            public ExportSnapshot ExportPlc(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation, TiaAnalyzer.Core.ExportOptions options = null)
            {
                if (portal == null) throw new ObjectDisposedException(nameof(AttachedPortal));
                var writer = new PlcExportWriter(directory, false, progress, cancellation, options)
                    { ApiIdentity = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(TiaPortal).Assembly.Location).FileVersion + "|" +
                        System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(PlcSoftware).Assembly.Location).FileVersion };
                var projects = new List<Tuple<Project, string, string>>();
                foreach (Project project in writer.Enumerate("TiaPortal.Projects", "", () => portal.Projects))
                {
                    var projectName = writer.Call("Project.Name", "Enumeration", "", "Project", "", () => project.Name);
                    writer.SourceIdentity = writer.Call("Project.Path", "Enumeration", projectName, "Project", "", () => project.Path?.FullName);
                    var projectProvider = (object)project as IEngineeringServiceProvider;
                    var projectStamp = writer.CaptureFingerprints && projectProvider != null ? ReadStamp(projectProvider, writer, projectName) : null;
                    writer.SourceProof = projectStamp != null && projectStamp.ReliableForXml ? projectStamp.Token : null;
                    projects.Add(Tuple.Create(project, projectName, projectStamp?.Token));
                    writer.Snapshot.Components.Add(new ProjectComponent { Name = projectName, Kind = "Progetto già aperto", Project = projectName, Status = "LETTO" });
                    writer.CheckCancellation();
                    var visited = new HashSet<Device>();
                    foreach (Device device in writer.Enumerate("Project.Devices", projectName, () => project.Devices)) ExportDevice(device, projectName, "", visited, writer);
                    foreach (DeviceUserGroup group in writer.Enumerate("Project.DeviceGroups", projectName, () => project.DeviceGroups)) ExportDeviceGroup(group, projectName, "", visited, writer);
                    foreach (Device device in writer.Enumerate("Project.UngroupedDevicesGroup.Devices", projectName, () => project.UngroupedDevicesGroup.Devices)) ExportDevice(device, projectName, "", visited, writer);
                }
                var snapshot = writer.ExecutePlan();
                foreach (var project in projects)
                {
                    if (project.Item3 == null) continue;
                    var after = ReadStamp((object)project.Item1 as IEngineeringServiceProvider, writer, project.Item2);
                    if (after == null || after.Token != project.Item3)
                    {
                        writer.Issue(project.Item2, new InvalidOperationException("Fingerprint del progetto modificato durante la raccolta; ripetere con progetto stabile."));
                        snapshot.AcquisitionState = "Incomplete"; writer.SaveCheckpoint("Incomplete");
                    }
                }
                return snapshot;
            }
            private static void Traverse(PlcExportWriter writer, string source, Action action)
            {
                writer.CheckCancellation();
                try { writer.Call("Traverse", "Traversal", "", "Group", source, action); }
                catch (EngineeringSecurityException ex) { throw Denied(ex); }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TiaConnectionException) && !(ex is ExportDiagnosticsException)) { writer.Issue(source, ex); }
            }
            private static void ExportDeviceGroup(DeviceUserGroup group, string project, string parent, HashSet<Device> visited, PlcExportWriter writer)
            {
                var path = parent + "/" + Uri.EscapeDataString(writer.Call("Group.Name", "Enumeration", "", "Group", parent, () => group.Name));
                Traverse(writer, project + path, () =>
                {
                    foreach (Device device in writer.Enumerate("DeviceGroup.Devices", path, () => group.Devices)) ExportDevice(device, project, path, visited, writer);
                    foreach (DeviceUserGroup child in writer.Enumerate("Group.Groups", path, () => group.Groups)) ExportDeviceGroup(child, project, path, visited, writer);
                });
            }
            private static void ExportDevice(Device device, string project, string parent, HashSet<Device> visited, PlcExportWriter writer)
            {
                if (!visited.Add(device)) return;
                var deviceName = writer.Call("Device.Name", "Enumeration", "", "Device", parent, () => device.Name);
                writer.Snapshot.Components.Add(new ProjectComponent { Name = deviceName, Kind = "Dispositivo", Project = project, Path = parent + "/" + deviceName, Status = "LETTO" });
                Traverse(writer, project + "/" + deviceName, () =>
                {
                    foreach (DeviceItem item in writer.Enumerate("Device.DeviceItems", parent, () => device.DeviceItems)) ExportItem(item, project, parent + "/" + Uri.EscapeDataString(deviceName), writer);
                });
            }
            private static void ExportItem(DeviceItem item, string project, string parent, PlcExportWriter writer)
            {
                var itemName = writer.Call("DeviceItem.Name", "Enumeration", "", "DeviceItem", parent, () => item.Name);
                var path = parent + "/" + Uri.EscapeDataString(itemName);
                Traverse(writer, project + path, () =>
                {
                    var container = writer.Call("GetService<SoftwareContainer>", "Enumeration", "", "DeviceItem", path, () => item.GetService<SoftwareContainer>());
                    var plc = container == null ? null : writer.Call("SoftwareContainer.Software", "Enumeration", "", "Software", path, () => container.Software as PlcSoftware);
                    writer.Snapshot.Components.Add(new ProjectComponent { Name = itemName, Kind = plc == null ? "Elemento hardware" : "PLC / STEP 7", Project = project, Path = path, Status = "LETTO" });
                    if (plc != null)
                    {
                        var plcPath = path + "/" + Uri.EscapeDataString(writer.Call("PlcSoftware.Name", "Enumeration", "", "PlcSoftware", path, () => plc.Name));
                        ExportBlocks(writer.Call("plc.BlockGroup", "Enumeration", "", "Group", plcPath, () => plc.BlockGroup), project, plcPath, "Blocks", writer);
                        ExportTypes(writer.Call("plc.TypeGroup", "Enumeration", "", "Group", plcPath, () => plc.TypeGroup), project, plcPath, "Types", writer);
                        ExportTags(writer.Call("plc.TagTableGroup", "Enumeration", "", "Group", plcPath, () => plc.TagTableGroup), project, plcPath, "Tags", writer);
                        Traverse(writer, project + plcPath + "/Units", () =>
                        {
                            var provider = writer.Call("GetService<PlcUnitProvider>", "Enumeration", "", "PLC", plcPath, () => plc.GetService<PlcUnitProvider>());
                            if (provider != null)
                            {
                                foreach (PlcUnit unit in writer.Enumerate("UnitGroup.Units", plcPath, () => provider.UnitGroup.Units)) ExportUnit(unit, project, plcPath, writer);
                                foreach (PlcSafetyUnit unit in writer.Enumerate("UnitGroup.SafetyUnits", plcPath, () => provider.UnitGroup.SafetyUnits)) ExportUnit(unit, project, plcPath, writer);
                            }
                        });
                    }
                    foreach (DeviceItem child in writer.Enumerate("DeviceItem.DeviceItems", path, () => item.DeviceItems)) ExportItem(child, project, path, writer);
                });
            }
            private static void ExportUnit(PlcUnitBase unit, string project, string plc, PlcExportWriter writer)
            {
                var path = "Units/" + Uri.EscapeDataString(writer.Call("PlcUnit.Name", "Enumeration", "", "PlcUnit", plc, () => unit.Name));
                Traverse(writer, project + plc + path, () =>
                {
                    ExportBlocks(writer.Call("unit.BlockGroup", "Enumeration", "", "Group", path, () => unit.BlockGroup), project, plc, path + "/Blocks", writer);
                    ExportTypes(writer.Call("unit.TypeGroup", "Enumeration", "", "Group", path, () => unit.TypeGroup), project, plc, path + "/Types", writer);
                    ExportTags(writer.Call("unit.TagTableGroup", "Enumeration", "", "Group", path, () => unit.TagTableGroup), project, plc, path + "/Tags", writer);
                });
            }
            private static void ExportBlocks(PlcBlockGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + "/" + path, () =>
                {
                    foreach (PlcBlock block in writer.Enumerate("BlockGroup.Blocks", path, () => group.Blocks))
                        Traverse(writer, project + plc + path + "/" + writer.Call("Block.Name", "Enumeration", "", "Block", path, () => block.Name), () => ExportBlock(block, project, plc, path, writer));
                    foreach (PlcBlockUserGroup child in writer.Enumerate("Group.Groups", path, () => group.Groups)) ExportBlocks(child, project, plc, path + "/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer);
                    var system = group as PlcBlockSystemGroup;
                    if (system != null)
                        foreach (PlcSystemBlockGroup child in writer.Enumerate("SystemBlockGroups", path, () => system.SystemBlockGroups)) ExportSystemBlocks(child, project, plc, path + "/System/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer);
                });
            }
            private static void ExportSystemBlocks(PlcSystemBlockGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcBlock block in writer.Enumerate("BlockGroup.Blocks", path, () => group.Blocks))
                        Traverse(writer, project + plc + path + "/" + writer.Call("Block.Name", "Enumeration", "", "Block", path, () => block.Name), () => ExportBlock(block, project, plc, path, writer));
                    foreach (PlcSystemBlockGroup child in writer.Enumerate("Group.Groups", path, () => group.Groups)) ExportSystemBlocks(child, project, plc, path + "/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer);
                });
            }
            private static void ExportBlock(PlcBlock block, string project, string plc, string group, PlcExportWriter writer)
            {
                var name = writer.Call("Block.Name", "Enumeration", "", "Block", group, () => block.Name);
                var language = writer.Call("Block.ProgrammingLanguage", "Enumeration", name, "Block", group, () => block.ProgrammingLanguage.ToString());
                var protectedBlock = writer.Call("Block.IsKnowHowProtected", "Enumeration", name, "Block", group, () => block.IsKnowHowProtected);
                var number = writer.Call("Block.Number", "Enumeration", name, "Block", group, () => block.Number);
                ExportObject(writer, block, project, plc, group, name, block.GetType().Name, language,
                    protectedBlock, file => block.Export(file, Siemens.Engineering.ExportOptions.WithDefaults), number);
            }
            private static void ExportTypes(PlcTypeGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcType type in writer.Enumerate("TypeGroup.Types", path, () => group.Types))
                        Traverse(writer, project + plc + path + "/" + writer.Call("Type.Name", "Enumeration", "", "UDT", path, () => type.Name), () =>
                            ExportObject(writer, type, project, plc, path, writer.Call("Type.Name", "Enumeration", "", "UDT", path, () => type.Name), "UDT", "", writer.Call("Type.IsKnowHowProtected", "Enumeration", "", "UDT", path, () => type.IsKnowHowProtected),
                                file => type.Export(file, Siemens.Engineering.ExportOptions.WithDefaults)));
                    foreach (PlcTypeUserGroup child in writer.Enumerate("Group.Groups", path, () => group.Groups)) ExportTypes(child, project, plc, path + "/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer);
                    var system = group as PlcTypeSystemGroup;
                    if (system != null)
                        foreach (PlcSystemTypeGroup child in writer.Enumerate("SystemTypeGroups", path, () => system.SystemTypeGroups))
                            Traverse(writer, project + plc + path + "/System/" + writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name), () =>
                            {
                                foreach (PlcType type in writer.Enumerate("SystemTypeGroup.Types", path, () => child.Types))
                                    Traverse(writer, project + plc + path + "/System/" + writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name) + "/" + writer.Call("Type.Name", "Enumeration", "", "UDT", path, () => type.Name), () =>
                                        ExportObject(writer, type, project, plc, path + "/System/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer.Call("Type.Name", "Enumeration", "", "UDT", path, () => type.Name), "UDT", "", writer.Call("Type.IsKnowHowProtected", "Enumeration", "", "UDT", path, () => type.IsKnowHowProtected),
                                            file => type.Export(file, Siemens.Engineering.ExportOptions.WithDefaults)));
                            });
                });
            }
            private static void ExportTags(PlcTagTableGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcTagTable table in writer.Enumerate("TagGroup.TagTables", path, () => group.TagTables))
                        ExportObject(writer, table, project, plc, path, writer.Call("TagTable.Name", "Enumeration", "", "TagTable", path, () => table.Name), "TagTable", "", false,
                            file => table.Export(file, Siemens.Engineering.ExportOptions.WithDefaults));
                    foreach (PlcTagTableUserGroup child in writer.Enumerate("Group.Groups", path, () => group.Groups)) ExportTags(child, project, plc, path + "/" + Uri.EscapeDataString(writer.Call("Child.Name", "Enumeration", "", "Group", path, () => child.Name)), writer);
                });
            }
        }
    }
}
#endif
