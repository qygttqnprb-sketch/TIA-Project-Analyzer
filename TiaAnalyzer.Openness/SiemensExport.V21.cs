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
            public ExportSnapshot ExportPlc(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation)
            {
                if (portal == null) throw new ObjectDisposedException(nameof(AttachedPortal));
                var writer = new PlcExportWriter(directory, false, progress, cancellation);
                writer.Snapshot.Components.AddRange(ReadComponents());
                foreach (Project project in portal.Projects)
                {
                    writer.CheckCancellation();
                    var visited = new HashSet<Device>();
                    foreach (Device device in project.Devices) ExportDevice(device, project.Name, "", visited, writer);
                    foreach (DeviceUserGroup group in project.DeviceGroups) ExportDeviceGroup(group, project.Name, "", visited, writer);
                    foreach (Device device in project.UngroupedDevicesGroup.Devices) ExportDevice(device, project.Name, "", visited, writer);
                }
                if (writer.Snapshot.Artifacts.Count == 0)
                    writer.Snapshot.Issues.Add(new ExportIssue { Source = "PLC", Message = "Nessun oggetto PLC esportabile trovato nei progetti aperti." });
                return writer.Snapshot;
            }
            private static void Traverse(PlcExportWriter writer, string source, Action action)
            {
                writer.CheckCancellation();
                try { action(); }
                catch (EngineeringSecurityException ex) { throw Denied(ex); }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TiaConnectionException)) { writer.Issue(source, ex); }
            }
            private static void ExportDeviceGroup(DeviceUserGroup group, string project, string parent, HashSet<Device> visited, PlcExportWriter writer)
            {
                var path = parent + "/" + group.Name;
                Traverse(writer, project + path, () =>
                {
                    foreach (Device device in group.Devices) ExportDevice(device, project, path, visited, writer);
                    foreach (DeviceUserGroup child in group.Groups) ExportDeviceGroup(child, project, path, visited, writer);
                });
            }
            private static void ExportDevice(Device device, string project, string parent, HashSet<Device> visited, PlcExportWriter writer)
            {
                if (!visited.Add(device)) return;
                Traverse(writer, project + "/" + device.Name, () =>
                {
                    foreach (DeviceItem item in device.DeviceItems) ExportItem(item, project, parent + "/" + device.Name, writer);
                });
            }
            private static void ExportItem(DeviceItem item, string project, string parent, PlcExportWriter writer)
            {
                var path = parent + "/" + item.Name;
                Traverse(writer, project + path, () =>
                {
                    var container = item.GetService<SoftwareContainer>();
                    var plc = container == null ? null : container.Software as PlcSoftware;
                    if (plc != null)
                    {
                        var plcPath = path + "/" + plc.Name;
                        ExportBlocks(plc.BlockGroup, project, plcPath, "Blocks", writer);
                        ExportTypes(plc.TypeGroup, project, plcPath, "Types", writer);
                        ExportTags(plc.TagTableGroup, project, plcPath, "Tags", writer);
                        Traverse(writer, project + plcPath + "/Units", () =>
                        {
                            var provider = plc.GetService<PlcUnitProvider>();
                            if (provider != null)
                            {
                                foreach (PlcUnit unit in provider.UnitGroup.Units) ExportUnit(unit, project, plcPath, writer);
                                foreach (PlcSafetyUnit unit in provider.UnitGroup.SafetyUnits) ExportUnit(unit, project, plcPath, writer);
                            }
                        });
                    }
                    foreach (DeviceItem child in item.DeviceItems) ExportItem(child, project, path, writer);
                });
            }
            private static void ExportUnit(PlcUnitBase unit, string project, string plc, PlcExportWriter writer)
            {
                var path = "Units/" + unit.Name;
                Traverse(writer, project + plc + path, () =>
                {
                    ExportBlocks(unit.BlockGroup, project, plc, path + "/Blocks", writer);
                    ExportTypes(unit.TypeGroup, project, plc, path + "/Types", writer);
                    ExportTags(unit.TagTableGroup, project, plc, path + "/Tags", writer);
                });
            }
            private static void ExportBlocks(PlcBlockGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + "/" + path, () =>
                {
                    foreach (PlcBlock block in group.Blocks) ExportBlock(block, project, plc, path, writer);
                    foreach (PlcBlockUserGroup child in group.Groups) ExportBlocks(child, project, plc, path + "/" + child.Name, writer);
                    var system = group as PlcBlockSystemGroup;
                    if (system != null)
                        foreach (PlcSystemBlockGroup child in system.SystemBlockGroups) ExportSystemBlocks(child, project, plc, path + "/System/" + child.Name, writer);
                });
            }
            private static void ExportSystemBlocks(PlcSystemBlockGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcBlock block in group.Blocks) ExportBlock(block, project, plc, path, writer);
                    foreach (PlcSystemBlockGroup child in group.Groups) ExportSystemBlocks(child, project, plc, path + "/" + child.Name, writer);
                });
            }
            private static void ExportBlock(PlcBlock block, string project, string plc, string group, PlcExportWriter writer)
            {
                writer.Write(project, plc, group, block.Name, block.GetType().Name, block.ProgrammingLanguage.ToString(),
                    block.IsKnowHowProtected, file => block.Export(file, ExportOptions.WithDefaults));
            }
            private static void ExportTypes(PlcTypeGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcType type in group.Types)
                        writer.Write(project, plc, path, type.Name, "UDT", "", type.IsKnowHowProtected,
                            file => type.Export(file, ExportOptions.WithDefaults));
                    foreach (PlcTypeUserGroup child in group.Groups) ExportTypes(child, project, plc, path + "/" + child.Name, writer);
                });
            }
            private static void ExportTags(PlcTagTableGroup group, string project, string plc, string path, PlcExportWriter writer)
            {
                Traverse(writer, project + plc + path, () =>
                {
                    foreach (PlcTagTable table in group.TagTables)
                        writer.Write(project, plc, path, table.Name, "TagTable", "", false,
                            file => table.Export(file, ExportOptions.WithDefaults));
                    foreach (PlcTagTableUserGroup child in group.Groups) ExportTags(child, project, plc, path + "/" + child.Name, writer);
                });
            }
        }
    }
}
#endif
