// ImportLayersFrom3dm.cs
// Rhino 8 (Windows/Mac) C# script: import only selected layers from a .3dm file,
// without opening/importing the whole file first.
//
// Run: ScriptEditor > File > New > C#  > paste > Run (F5)
//  or: RunScript command > browse to this file.

using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

var doc = RhinoDoc.ActiveDoc;

// ---------------------------------------------------------------- 1. pick file
var openDlg = new Rhino.UI.OpenFileDialog
{
    Title = "Select a Rhino file to import layers from",
    Filter = "Rhino 3D Models (*.3dm)|*.3dm"
};
if (!openDlg.ShowOpenDialog()) return;

using var file = File3dm.Read(openDlg.FileName);
if (file == null)
{
    Rhino.UI.Dialogs.ShowMessage("Could not read the selected file.", "Import layers");
    return;
}

// ---------------------------------------------------------------- 2. read layers
var layers = file.AllLayers.Where(l => !l.IsDeleted).ToList();
if (layers.Count == 0)
{
    Rhino.UI.Dialogs.ShowMessage("The file contains no layers.", "Import layers");
    return;
}
var byId = layers.ToDictionary(l => l.Id);
var layerByIndex = new Dictionary<int, Layer>();
foreach (var l in layers) layerByIndex[l.Index] = l;

// object count per layer (ignoring block-definition members)
var counts = new Dictionary<int, int>();
foreach (var o in file.Objects)
{
    if (o.Attributes.Mode == ObjectMode.InstanceDefinitionObject) continue;
    int li = o.Attributes.LayerIndex;
    counts[li] = counts.TryGetValue(li, out var c) ? c + 1 : 1;
}

// build tree
var nodes = layers.ToDictionary(l => l.Id, l => new LayerNode { Layer = l, Count = counts.TryGetValue(l.Index, out var n) ? n : 0 });
var roots = new List<LayerNode>();
foreach (var l in layers)
{
    var node = nodes[l.Id];
    if (l.ParentLayerId != Guid.Empty && nodes.TryGetValue(l.ParentLayerId, out var parent))
        parent.Children.Add(node);
    else
        roots.Add(node);
}
var flat = new List<LayerNode>();
void Flatten(LayerNode n, int depth)
{
    n.Depth = depth;
    flat.Add(n);
    foreach (var ch in n.Children) Flatten(ch, depth + 1);
}
foreach (var r in roots) Flatten(r, 0);

// ---------------------------------------------------------------- 3. let user choose
var picker = new LayerPickerDialog(System.IO.Path.GetFileName(openDlg.FileName), flat);
if (!picker.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow)) return;

var selectedIds = new HashSet<Guid>(flat.Where(n => n.Box.Checked == true).Select(n => n.Layer.Id));
if (selectedIds.Count == 0) return;

// ---------------------------------------------------------------- 4. unit scaling
var scaleXf = Transform.Identity;
var scaleInv = Transform.Identity;
bool scaled = false;
var fileUnits = file.Settings.ModelUnitSystem;
if (fileUnits != doc.ModelUnitSystem)
{
    var answer = Rhino.UI.Dialogs.ShowMessage(
        $"The file uses {fileUnits}, the current model uses {doc.ModelUnitSystem}.\n\nScale imported objects to the current model units?",
        "Import layers", Rhino.UI.ShowMessageButton.YesNo, Rhino.UI.ShowMessageIcon.Question);
    if (answer == Rhino.UI.ShowMessageResult.Yes)
    {
        double s = RhinoMath.UnitScale(fileUnits, doc.ModelUnitSystem);
        if (Math.Abs(s - 1.0) > 1e-12)
        {
            scaleXf = Transform.Scale(Point3d.Origin, s);
            scaleXf.TryGetInverse(out scaleInv);
            scaled = true;
        }
    }
}

// ---------------------------------------------------------------- 5. helpers
var layerMap = new Dictionary<Guid, int>();   // file layer id -> doc layer index
var matMap = new Dictionary<int, int>();
var ltMap = new Dictionary<int, int>();
var groupMap = new Dictionary<int, int>();
var defMap = new Dictionary<Guid, int>();     // file idef id -> doc idef index

var fileMaterials = file.AllMaterials.ToDictionary(m => m.Index);
var fileLinetypes = file.AllLinetypes.ToDictionary(t => t.LinetypeIndex);
var fileGroups = file.AllGroups.ToDictionary(g => g.Index);
var defById = file.AllInstanceDefinitions.ToDictionary(d => d.Id);
var objById = new Dictionary<Guid, File3dmObject>();
foreach (var o in file.Objects) objById[o.Attributes.ObjectId] = o;

string FullPath(Layer l)
{
    var parts = new List<string>();
    var cur = l;
    int guard = 0;
    while (cur != null && guard++ < 64)
    {
        parts.Insert(0, cur.Name);
        cur = (cur.ParentLayerId != Guid.Empty && byId.TryGetValue(cur.ParentLayerId, out var p)) ? p : null;
    }
    return string.Join("::", parts);
}

int EnsureLayer(Layer fl)
{
    if (layerMap.TryGetValue(fl.Id, out var idx)) return idx;

    idx = doc.Layers.FindByFullPath(FullPath(fl), -1);
    if (idx < 0)
    {
        var nl = new Layer
        {
            Name = fl.Name,
            Color = fl.Color,
            PlotColor = fl.PlotColor,
            PlotWeight = fl.PlotWeight,
            IsVisible = fl.IsVisible,
            IsLocked = fl.IsLocked
        };
        if (fl.ParentLayerId != Guid.Empty && byId.TryGetValue(fl.ParentLayerId, out var pl))
        {
            int pi = EnsureLayer(pl);
            if (pi >= 0) nl.ParentLayerId = doc.Layers[pi].Id;
        }
        idx = doc.Layers.Add(nl);
    }
    if (idx < 0) idx = doc.Layers.CurrentLayerIndex;
    layerMap[fl.Id] = idx;
    return idx;
}

int MapMaterial(int fi)
{
    if (fi < 0) return -1;
    if (matMap.TryGetValue(fi, out var di)) return di;
    di = fileMaterials.TryGetValue(fi, out var m) ? doc.Materials.Add(m) : -1;
    matMap[fi] = di;
    return di;
}

int MapLinetype(int fi)
{
    if (fi < 0) return -1;
    if (ltMap.TryGetValue(fi, out var di)) return di;
    di = fileLinetypes.TryGetValue(fi, out var t) ? doc.Linetypes.Add(t) : -1;
    ltMap[fi] = di;
    return di;
}

int MapGroup(int fi)
{
    if (groupMap.TryGetValue(fi, out var di)) return di;
    string name = fileGroups.TryGetValue(fi, out var g) ? g.Name : null;
    di = string.IsNullOrEmpty(name) ? doc.Groups.Add() : doc.Groups.Add(name);
    if (di < 0) di = doc.Groups.Add();
    groupMap[fi] = di;
    return di;
}

ObjectAttributes PrepAttributes(ObjectAttributes fa, bool keepGroups)
{
    var a = fa.Duplicate();
    a.ObjectId = Guid.Empty;

    a.LayerIndex = layerByIndex.TryGetValue(fa.LayerIndex, out var fl)
        ? EnsureLayer(fl)
        : doc.Layers.CurrentLayerIndex;

    if (a.MaterialSource == ObjectMaterialSource.MaterialFromObject)
        a.MaterialIndex = MapMaterial(fa.MaterialIndex);
    if (a.LinetypeSource == ObjectLinetypeSource.LinetypeFromObject)
        a.LinetypeIndex = MapLinetype(fa.LinetypeIndex);

    a.RemoveFromAllGroups();
    if (keepGroups)
    {
        var gl = fa.GetGroupList();
        if (gl != null)
            foreach (var g in gl)
            {
                int ng = MapGroup(g);
                if (ng >= 0) a.AddToGroup(ng);
            }
    }
    return a;
}

GeometryBase PrepGeometry(GeometryBase geo)
{
    var g = geo.Duplicate();
    if (scaled) g.Transform(scaleXf);
    return g;
}

int EnsureDefinition(Guid fileDefId)
{
    if (defMap.TryGetValue(fileDefId, out var existing)) return existing;
    defMap[fileDefId] = -1; // guards against circular references
    if (!defById.TryGetValue(fileDefId, out var d)) return -1;

    var geoms = new List<GeometryBase>();
    var attrs = new List<ObjectAttributes>();
    foreach (var oid in d.GetObjectIds())
    {
        if (!objById.TryGetValue(oid, out var mo) || mo.Geometry == null) continue;

        GeometryBase g;
        if (mo.Geometry is InstanceReferenceGeometry nested)
        {
            int ni = EnsureDefinition(nested.ParentIdefId);
            if (ni < 0) continue;
            g = new InstanceReferenceGeometry(doc.InstanceDefinitions[ni].Id, scaleXf * nested.Xform * scaleInv);
        }
        else g = PrepGeometry(mo.Geometry);

        geoms.Add(g);
        attrs.Add(PrepAttributes(mo.Attributes, false));
    }
    if (geoms.Count == 0) return -1;

    // unique name
    string name = string.IsNullOrWhiteSpace(d.Name) ? "Block" : d.Name;
    string unique = name;
    for (int i = 2; doc.InstanceDefinitions.Find(unique, true) != null; i++) unique = $"{name} ({i})";

    int di = doc.InstanceDefinitions.Add(unique, d.Description ?? "", Point3d.Origin, geoms, attrs);
    defMap[fileDefId] = di;
    return di;
}

// ---------------------------------------------------------------- 6. import
int imported = 0, skipped = 0;
uint undo = doc.BeginUndoRecord("Import layers from 3dm");
try
{
    foreach (var o in file.Objects)
    {
        var fa = o.Attributes;
        if (fa.Mode == ObjectMode.InstanceDefinitionObject) continue;
        if (!layerByIndex.TryGetValue(fa.LayerIndex, out var fl) || !selectedIds.Contains(fl.Id)) continue;
        if (o.Geometry == null) { skipped++; continue; }

        var attr = PrepAttributes(fa, true);
        Guid newId;

        if (o.Geometry is InstanceReferenceGeometry iref)
        {
            int di = EnsureDefinition(iref.ParentIdefId);
            if (di < 0) { skipped++; continue; }
            newId = doc.Objects.AddInstanceObject(di, scaleXf * iref.Xform * scaleInv, attr);
        }
        else
        {
            newId = doc.Objects.Add(PrepGeometry(o.Geometry), attr);
        }

        if (newId == Guid.Empty) skipped++; else imported++;
    }
}
finally
{
    doc.EndUndoRecord(undo);
}

doc.Views.Redraw();
RhinoApp.WriteLine($"Import layers: {imported} object(s) imported from {selectedIds.Count} layer(s)" +
                   (skipped > 0 ? $", {skipped} skipped." : "."));

// ================================================================ types (must stay below the statements)

class LayerNode
{
    public Layer Layer;
    public int Depth;
    public int Count;
    public Eto.Forms.CheckBox Box;
    public List<LayerNode> Children = new List<LayerNode>();
}

class LayerPickerDialog : Eto.Forms.Dialog<bool>
{
    readonly List<LayerNode> _nodes;
    readonly Eto.Forms.Label _status = new Eto.Forms.Label();
    bool _busy;

    public LayerPickerDialog(string fileName, List<LayerNode> nodes)
    {
        _nodes = nodes;
        Title = "Import layers";
        Resizable = true;
        Padding = new Eto.Drawing.Padding(10);

        // Initial size = a fraction of the working area of the screen Rhino is on.
        // Change these two numbers to taste.
        const double WidthFraction = 1.0 / 3.0;
        const double HeightFraction = 1.0 / 2.0;
        var screen = Rhino.UI.RhinoEtoApp.MainWindow?.Screen ?? Eto.Forms.Screen.PrimaryScreen;
        var area = screen.WorkingArea;
        int w = Math.Max(380, (int)(area.Width * WidthFraction));
        int h = Math.Max(360, (int)(area.Height * HeightFraction));
        ClientSize = new Eto.Drawing.Size(
            Math.Min(w, (int)area.Width - 40),
            Math.Min(h, (int)area.Height - 80));
        MinimumSize = new Eto.Drawing.Size(320, 280);

        var list = new Eto.Forms.StackLayout { Spacing = 2, Padding = new Eto.Drawing.Padding(4) };
        foreach (var n in nodes)
        {
            var node = n;
            node.Box = new Eto.Forms.CheckBox { Text = $"{node.Layer.Name}  ({node.Count})" };
            node.Box.CheckedChanged += (s, e) =>
            {
                if (_busy) return;
                _busy = true;
                SetChildren(node, node.Box.Checked == true);
                _busy = false;
                UpdateStatus();
            };
            list.Items.Add(new Eto.Forms.Panel
            {
                Padding = new Eto.Drawing.Padding(node.Depth * 20, 0, 0, 0),
                Content = node.Box
            });
        }

        var all = new Eto.Forms.Button { Text = "All" };
        all.Click += (s, e) => SetAll(true);
        var none = new Eto.Forms.Button { Text = "None" };
        none.Click += (s, e) => SetAll(false);
        var ok = new Eto.Forms.Button { Text = "Import", Size = new Eto.Drawing.Size(90, -1) };
        ok.Click += (s, e) => Close(true);
        var cancel = new Eto.Forms.Button { Text = "Cancel", Size = new Eto.Drawing.Size(90, -1) };
        cancel.Click += (s, e) => Close(false);
        DefaultButton = ok;
        AbortButton = cancel;
        all.Size = new Eto.Drawing.Size(70, -1);
        none.Size = new Eto.Drawing.Size(70, -1);

        // Row: [All] [None]  status text
        var selectionRow = new Eto.Forms.StackLayout
        {
            Orientation = Eto.Forms.Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = Eto.Forms.VerticalAlignment.Center
        };
        selectionRow.Items.Add(all);
        selectionRow.Items.Add(none);
        selectionRow.Items.Add(_status);

        // Row: right-aligned [Import] [Cancel]
        var actionRow = new Eto.Forms.TableLayout { Spacing = new Eto.Drawing.Size(6, 0) };
        actionRow.Rows.Add(new Eto.Forms.TableRow(
            new Eto.Forms.TableCell { ScaleWidth = true },   // empty spacer pushes buttons right
            ok,
            cancel));

        var scroll = new Eto.Forms.Scrollable { Content = list, Border = Eto.Forms.BorderType.Bezel };

        var layout = new Eto.Forms.TableLayout { Spacing = new Eto.Drawing.Size(6, 8) };
        layout.Rows.Add(new Eto.Forms.Label { Text = $"Layers in {fileName} (object count in brackets):" });
        layout.Rows.Add(new Eto.Forms.TableRow(new Eto.Forms.TableCell(scroll, true)) { ScaleHeight = true });
        layout.Rows.Add(selectionRow);
        layout.Rows.Add(actionRow);
        Content = layout;
        UpdateStatus();
    }

    void SetChildren(LayerNode n, bool value)
    {
        foreach (var ch in n.Children)
        {
            ch.Box.Checked = value;
            SetChildren(ch, value);
        }
    }

    void SetAll(bool value)
    {
        _busy = true;
        foreach (var n in _nodes) n.Box.Checked = value;
        _busy = false;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        int layers = _nodes.Count(n => n.Box.Checked == true);
        int objs = _nodes.Where(n => n.Box.Checked == true).Sum(n => n.Count);
        _status.Text = $"{layers} layer(s), {objs} object(s)";
    }
}