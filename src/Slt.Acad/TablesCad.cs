// Кабели и таблицы на чертеже: линии связи, кабельный журнал, спецификация, таблица ФСА, легенда, выгрузка в Excel (CSV).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Slt.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Symbol = Slt.Core.Symbol;
using Font = System.Drawing.Font;

[assembly: CommandClass(typeof(Slt.Acad.TableCommands))]

namespace Slt.Acad
{
    sealed class Found { public BlockReference Br; public Smart Obj; }

    public class TableCommands
    {
        static Document Doc => AcApp.DocumentManager.MdiActiveDocument;
        static Editor Ed => Doc.Editor;
        static Cable lastCable = new Cable { Mark = "К1", Type = "КСПЭВПнг(А)-HF 2x2x0,97", Line = "an" };

        // ---------------- область таблицы: рамка формата или всё пространство ----------------

        /// <summary>Умные объекты в той же рамке формата, что и точка at (NaN — всё пространство).</summary>
        internal static List<Found> Scope(Transaction tr, Database db, ObjectId space, Point3d at)
        {
            var refs = SmartRef.All(tr, db).Where(b => b.OwnerId == space).ToList();
            Extents3d? frame = Frame(refs, at);
            var res = new List<Found>();
            foreach (var b in refs)
            {
                if (frame.HasValue && !Inside(frame.Value, b.Position)) continue;
                try { res.Add(new Found { Br = b, Obj = SmartRef.Load(b) }); } catch { }
            }
            return res;
        }

        static Extents3d? Frame(IEnumerable<BlockReference> refs, Point3d at)
        {
            if (double.IsNaN(at.X)) return null;
            foreach (var b in refs)
            {
                if (!(Data.Read(b).TryGetValue("kind", out var kd) && kd == "format")) continue;
                var f = (SheetFormat)SmartRef.Load(b); var wh = f.Paper;
                var e = new Extents3d(b.Position, b.Position + new Vector3d(wh[0] * f.K, wh[1] * f.K, 0));
                if (Inside(e, at)) return e;
            }
            return null;
        }

        static bool Inside(Extents3d e, Point3d p) => p.X >= e.MinPoint.X && p.X <= e.MaxPoint.X && p.Y >= e.MinPoint.Y && p.Y <= e.MaxPoint.Y;

        /// <summary>Позиция объекта у точки (начало/конец кабеля): прибор, символ — в пределах допуска.</summary>
        internal static string PosAt(IEnumerable<Found> all, Point3d p, double tol)
        {
            string best = ""; double bd = double.MaxValue;
            foreach (var f in all)
            {
                string pos = f.Obj is Instrument i ? i.Pos : f.Obj is Symbol y ? y.Pos : "";
                if (pos == "") continue;
                double d;
                try
                {
                    var e = f.Br.GeometricExtents;
                    var dx = Math.Max(0, Math.Max(e.MinPoint.X - p.X, p.X - e.MaxPoint.X));
                    var dy = Math.Max(0, Math.Max(e.MinPoint.Y - p.Y, p.Y - e.MaxPoint.Y));
                    d = Math.Sqrt(dx * dx + dy * dy);
                }
                catch { d = f.Br.Position.DistanceTo(p); }
                if (d <= tol && d < bd) { bd = d; best = pos; }
            }
            return best;
        }

        static double UnitsPerMm(Database db, ObjectId space) => space == SymbolUtilityServices.GetBlockModelSpaceId(db) ? Settings.UnitsPerMm(db) : 0;

        static List<JournalItem> Cables(List<Found> scope, Database db)
        {
            var res = new List<JournalItem>();
            foreach (var f in scope)
            {
                if (!(f.Obj is Cable c)) continue;
                var tol = 4 * c.K;
                var tf = f.Br.BlockTransform;
                var a = new Point3d(c.Path[0].X, c.Path[0].Y, 0).TransformBy(tf); var b = new Point3d(c.Path[c.Path.Count - 1].X, c.Path[c.Path.Count - 1].Y, 0).TransformBy(tf);
                res.Add(new JournalItem
                {
                    Mark = c.Mark, Type = c.Type, Note = c.Note,
                    From = c.Start != "" ? c.Start : PosAt(scope, a, tol), To = c.To != "" ? c.To : PosAt(scope, b, tol),
                    Meters = c.Length(UnitsPerMm(db, f.Br.OwnerId)),
                });
            }
            return res;
        }

        static List<SpecItem> SpecItems(List<Found> scope, Database db)
        {
            var res = new List<SpecItem>();
            foreach (var f in scope)
            {
                if (f.Obj is Instrument i && (i.Name != "" || i.Type != ""))
                    res.Add(new SpecItem { Section = Spec.Sections[0], Pos = i.Pos, Name = i.Name, Type = i.Type, Code = i.Code, Maker = i.Maker, Unit = i.Unit, Qty = i.Qty, Mass = i.Mass, Note = i.Note });
                else if (f.Obj is Symbol y && (y.Name != "" || y.Type != "" || y.Pos != ""))
                    res.Add(new SpecItem { Section = Spec.Sections[1], Pos = y.Pos, Name = y.Name != "" ? y.Name : y.Def?.Name ?? y.Sym, Type = y.Type, Code = y.Code, Maker = y.Maker, Unit = y.Unit, Qty = y.Qty, Mass = y.Mass, Note = y.Note });
            }
            foreach (var c in Cables(scope, db).Where(c => c.Type != ""))
                res.Add(new SpecItem { Section = Spec.Sections[2], Name = "Кабель", Type = c.Type, Unit = "м", Qty = c.Meters });
            return res;
        }

        /// <summary>Заполнить таблицу по её области.</summary>
        static void Fill(Smart t, List<Found> scope, Database db)
        {
            switch (t)
            {
                case PosList l:
                    l.Rows = PosList.Compose(scope.Select(f =>
                        f.Obj is Instrument i && (i.Pos != "" || i.Name != "") ? new PosItem(i.Pos, i.Name, i.Qty) :
                        f.Obj is Symbol y && (y.Pos != "" || y.Name != "") ? new PosItem(y.Pos, y.Name != "" ? y.Name : y.Def?.Name ?? y.Sym, y.Qty) : null).Where(x => x != null));
                    break;
                case Journal j: j.Rows = Journal.Compose(Cables(scope, db)); break;
                case Spec sp: sp.Rows = Spec.Compose(SpecItems(scope, db)); break;
            }
        }

        public static int UpdateAll(Database db, ObjectId only)
        {
            int n = 0;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var b in SmartRef.All(tr, db).Where(b => Data.Read(b).TryGetValue("kind", out var k) && (k == "list" || k == "journal" || k == "spec")))
                {
                    if (!only.IsNull && b.ObjectId != only) continue;
                    var t = SmartRef.Load(b);
                    if (!IsAuto(t)) continue;
                    Fill(t, Scope(tr, db, b.OwnerId, b.Position), db);
                    SmartRef.Save(b, t); SmartRef.Rebuild(tr, b); n++;
                }
                tr.Commit();
            }
            return n;
        }

        static bool IsAuto(Smart t) => t is PosList l ? l.Auto : t is Journal j ? j.Auto : t is Spec sp ? sp.Auto : false;
        static void SetAuto(Smart t, bool v) { if (t is PosList l) l.Auto = v; if (t is Journal j) j.Auto = v; if (t is Spec sp) sp.Auto = v; }
        internal static List<string[]> RowsOf(Smart t) => t is PosList l ? l.Rows : t is Journal j ? j.Rows : t is Spec sp ? sp.Rows : new List<string[]>();

        /// <summary>Строки таблицы по объектам её листа (для кнопки «Заполнить из чертежа»).</summary>
        internal static List<string[]> Refill(Smart t, ObjectId owner, Point3d at)
        {
            var db = Doc.Database;
            var copy = Smart.From(t.SaveAll());
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                Fill(copy, Scope(tr, db, owner.IsNull ? db.CurrentSpaceId : owner, at), db);
            return RowsOf(copy);
        }

        internal static void Place(Smart t, string prompt)
        {
            var db = Doc.Database;
            t.K = Settings.Read(db).KFor(db, db.CurrentSpaceId);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                Fill(t, Scope(tr, db, db.CurrentSpaceId, new Point3d(double.NaN, 0, 0)), db);
            // Объектов СЛТ нет — таблица заполняется вручную (или вставкой из Excel).
            if (RowsOf(t).Count == 0)
            {
                Ed.WriteMessage("\nНа чертеже нет объектов СЛТ для этой таблицы — заполните её вручную или вставьте строки из Excel (Ctrl+V).");
                SetAuto(t, false);
                if (!TableDialog.Edit(t, null)) return;
            }
            SmartJig jig = null;
            jig = new SmartJig(t, prompt, Point3d.Origin, (x, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at == null) return;
            ObjectId id;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                id = SmartRef.Create(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), t, at.Value).ObjectId;
                tr.Commit();
            }
            if (IsAuto(t)) UpdateAll(db, id);
            bool framed;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction()) framed = Frame(SmartRef.All(tr, db).Where(b => b.OwnerId == db.CurrentSpaceId), at.Value).HasValue;
            if (IsAuto(t)) Ed.WriteMessage("\n" + t.Title + " — по элементам " + (framed ? "этого листа (рамки формата)." : "всего пространства.") + " После правок — «Обновить таблицы». Двойной щелчок — правка строк.");
            else Ed.WriteMessage("\n" + t.Title + " заполнена вручную. Двойной щелчок — правка строк.");
        }

        // ---------------- кабели ----------------

        [CommandMethod("SLT_CABLE", CommandFlags.Modal)]
        public void CableCmd()
        {
            var db = Doc.Database;
            var k = Settings.Read(db).KFor(db, db.CurrentSpaceId);
            var proto = (Cable)Smart.From(lastCable.SaveAll());
            proto.K = k; proto.Mark = NextMark(db, proto.Mark); proto.Start = proto.To = "";
            if (!Dialogs.Edit(proto)) return;
            while (true)
            {
                var c = (Cable)Smart.From(proto.SaveAll());
                var r = Ed.GetPoint(new PromptPointOptions($"\nНачало кабеля {c.Mark} <готово>:") { AllowNone = true });
                if (r.Status != PromptStatus.OK) return;
                var start = r.Value.TransformBy(Ed.CurrentUserCoordinateSystem);
                var pts = new List<P2> { new P2(0, 0) };
                while (true)
                {
                    c.Path = pts.Concat(new[] { pts[pts.Count - 1] + new P2(1e-6, 0) }).ToList();
                    var basePt = start + new Vector3d(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, 0);
                    var jig = new SmartJig(c, pts.Count < 2 ? "Следующая точка:" : "Следующая точка <конец>:", start, (o, p) =>
                    {
                        var list = new List<P2>(pts) { new P2(p.X - start.X, p.Y - start.Y) };
                        ((Cable)o).Path = list;
                    }, basePt);
                    var res = Ed.Drag(jig);
                    if (res.Status == PromptStatus.Cancel) return;
                    if (res.Status != PromptStatus.OK) break;
                    pts.Add(new P2(jig.Last.X - start.X, jig.Last.Y - start.Y));
                }
                if (pts.Count < 2) return;
                c.Path = pts;
                using (Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var scope = Scope(tr, db, db.CurrentSpaceId, new Point3d(double.NaN, 0, 0));
                    var end = start + new Vector3d(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, 0);
                    var from = PosAt(scope, start, 4 * k); var to = PosAt(scope, end, 4 * k);
                    SmartRef.Create(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), c, start);
                    tr.Commit();
                    var len = c.Length(UnitsPerMm(db, db.CurrentSpaceId));
                    Ed.WriteMessage($"\n{c.Mark}: {(from == "" ? "?" : from)} → {(to == "" ? "?" : to)}" + (len > 0 ? $", {len:0} м" : ", длина — вручную в свойствах (на листе)"));
                }
                lastCable = (Cable)Smart.From(c.SaveAll());
                proto.Mark = Inc(c.Mark);
            }
        }

        static string Inc(string s) { var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"^(.*?)(\d+)$"); return m.Success ? m.Groups[1].Value + (int.Parse(m.Groups[2].Value) + 1) : s; }

        static string NextMark(Database db, string mark)
        {
            var m = System.Text.RegularExpressions.Regex.Match(mark ?? "К1", @"^(.*?)(\d+)$");
            var prefix = m.Success ? m.Groups[1].Value : "К";
            int max = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                foreach (var b in SmartRef.All(tr, db, "cable"))
                    if (Data.Read(b).TryGetValue("mark", out var mk))
                    {
                        var mm = System.Text.RegularExpressions.Regex.Match(mk, @"^(.*?)(\d+)$");
                        if (mm.Success && mm.Groups[1].Value == prefix) max = Math.Max(max, int.Parse(mm.Groups[2].Value));
                    }
            return prefix + (max + 1);
        }

        [CommandMethod("SLT_JOURNAL", CommandFlags.Modal)]
        public void JournalCmd() => Place(new Journal(), "Левый верхний угол кабельного журнала:");

        [CommandMethod("SLT_SPEC", CommandFlags.Modal)]
        public void SpecCmd() => Place(new Spec(), "Левый верхний угол спецификации:");

        [CommandMethod("SLT_LEGEND", CommandFlags.Modal)]
        public void LegendCmd()
        {
            var db = Doc.Database;
            var l = new LineLegend { K = Settings.Read(db).KFor(db, db.CurrentSpaceId) };
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                // Только виды линий, которые есть на чертеже; если кабелей нет — основной набор.
                var used = SmartRef.All(tr, db, "cable").Select(b => Data.Read(b).TryGetValue("line", out var v) ? v : "an").Distinct().ToList();
                if (used.Count > 0) l.Kinds = string.Join(",", LineKinds.All.Select(x => x.Id).Where(used.Contains));
            }
            SmartJig jig = null;
            jig = new SmartJig(l, "Точка вставки условных обозначений:", Point3d.Origin, (x, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at == null) return;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                SmartRef.Create(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), l, at.Value);
                tr.Commit();
            }
        }

        // ---------------- таблица ФСА ----------------

        [CommandMethod("SLT_FSA", CommandFlags.Modal)]
        public void FsaCmd()
        {
            var db = Doc.Database;
            var t = new FsaTable { K = Settings.Read(db).KFor(db, db.CurrentSpaceId) };
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                // Графы — по ссылкам приборов чертежа (номера сохраняются, пропуски закрываются при синхронизации).
                var kips = Scope(tr, db, db.CurrentSpaceId, new Point3d(double.NaN, 0, 0)).Select(f => f.Obj).OfType<Instrument>().ToList();
                foreach (var k in kips.OrderBy(k => k.Ref, Natural.Comparer))
                    foreach (var n in FsaTable.Refs(k.Ref))
                        if (!t.Columns.Any(c => c.Num == n))
                        {
                            var col = new FsaColumn { Num = n, Text = k.Name };
                            col.Marks.Add(k.Form == KipShape.Actuator ? "Управление" : "Измерение");
                            t.Columns.Add(col);
                        }
                t.Columns = t.Columns.OrderBy(c => c.Num, Natural.Comparer).ToList();
            }
            if (t.Columns.Count == 0) for (int i = 1; i <= 10; i++) t.Columns.Add(new FsaColumn { Num = i.ToString(CultureInfo.InvariantCulture) });
            if (!FsaDialog.Edit(t)) return;
            SmartJig jig = null;
            jig = new SmartJig(t, "Левый верхний угол таблицы (графы по ширине схемы):", Point3d.Origin, (x, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at == null) return;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                SmartRef.Create(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), t, at.Value);
                tr.Commit();
            }
        }

        /// <summary>Синхронизация таблицы ФСА с приборами листа: недостающие графы, нумерация подряд, ссылки приборов.</summary>
        [CommandMethod("SLT_FSASYNC", CommandFlags.Modal)]
        public void FsaSyncCmd()
        {
            var db = Doc.Database;
            int tables = 0, refs = 0;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var b in SmartRef.All(tr, db, "fsa"))
                {
                    var t = (FsaTable)SmartRef.Load(b);
                    var scope = Scope(tr, db, b.OwnerId, b.Position).Where(f => f.Obj is Instrument).ToList();
                    var before = scope.Select(f => ((Instrument)f.Obj).Ref).ToList();
                    t.Sync(scope.Select(f => (Instrument)f.Obj).ToList());
                    for (int i = 0; i < scope.Count; i++)
                    {
                        var k = (Instrument)scope[i].Obj;
                        if (k.Ref == before[i]) continue;
                        SmartRef.Save(scope[i].Br, k); SmartRef.Rebuild(tr, scope[i].Br); refs++;
                    }
                    var used = new HashSet<string>(scope.SelectMany(f => FsaTable.Refs(((Instrument)f.Obj).Ref)));
                    var orphan = t.Columns.Where(c => !used.Contains(c.Num)).Select(c => c.Num).ToList();
                    if (orphan.Count > 0) Ed.WriteMessage("\nГрафы без приборов: " + string.Join(", ", orphan) + ".");
                    SmartRef.Save(b, t); SmartRef.Rebuild(tr, b); tables++;
                }
                tr.Commit();
            }
            Ed.WriteMessage($"\nТаблиц ФСА: {tables}, изменено ссылок приборов: {refs}.");
        }

        // ---------------- выгрузка в Excel ----------------

        [CommandMethod("SLT_EXPORT", CommandFlags.Modal)]
        public void ExportCmd()
        {
            var db = Doc.Database;
            var dwg = db.Filename;
            var folder = string.IsNullOrEmpty(dwg) || !File.Exists(dwg) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(dwg);
            var name = string.IsNullOrEmpty(dwg) ? "Чертёж" : Path.GetFileNameWithoutExtension(dwg);
            List<JournalItem> cables; List<SpecItem> spec; List<PosItem> list = new List<PosItem>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var all = new List<Found>();
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout) all.AddRange(Scope(tr, db, id, new Point3d(double.NaN, 0, 0)));
                }
                cables = Cables(all, db).OrderBy(c => c.Mark, Natural.Comparer).ToList();
                spec = SpecItems(all, db);
            }
            var files = new List<string>();
            string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            void Write(string suffix, string[] head, IEnumerable<string[]> rows)
            {
                var path = Path.Combine(folder, name + suffix + ".csv");
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(";", head.Select(h => Csv(h.Replace("\n", "")))));
                foreach (var r in rows) sb.AppendLine(string.Join(";", r.Select(Csv)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                files.Add(path);
            }
            try
            {
                Write("_КЖ", Journal.Headers, Journal.Compose(cables));
                Write("_СО", Spec.Headers, Spec.Compose(spec).Select(r => r.Length == 1 ? new[] { "", r[0] } : r));
            }
            catch (System.Exception ex) { Ed.WriteMessage("\nНе удалось записать: " + ex.Message); return; }
            Ed.WriteMessage("\nВыгружено (открывается в Excel):\n  " + string.Join("\n  ", files));
        }
    }

    /// <summary>Правка таблицы ФСА: графы в сетке, точки — галочками, параметры таблицы — снизу.</summary>
    static class FsaDialog
    {
        public static bool Edit(FsaTable t)
        {
            var rows = t.AllRows();
            using (var form = new Form { Text = "Таблица схемы автоматизации", Width = 1100, Height = 640, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, Font = new Font("Segoe UI", 9f) })
            {
                var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 28, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None };
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "num", HeaderText = "Графа", Width = 50 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "text", HeaderText = "Наименование параметра", Width = 330 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "equip", HeaderText = "Оборудование по месту", Width = 200 });
                foreach (var r in rows) grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "m:" + r, HeaderText = r, Width = Math.Max(44, r.Length * 8) });
                foreach (var c in t.Columns)
                {
                    var vals = new List<object> { c.Num, c.Text, c.Equip };
                    vals.AddRange(rows.Select(r => (object)c.Marks.Contains(r)));
                    grid.Rows.Add(vals.ToArray());
                }
                var props = new PropertyGrid { Dock = DockStyle.Bottom, Height = 150, ToolbarVisible = false, HelpVisible = true, SelectedObject = new FsaView(t) };
                var hint = new Label { Dock = DockStyle.Top, Height = 34, Text = "Строка сетки — графа таблицы (слева направо). Номер графы — то, что пишется в ссылке прибора («Номер ссылки»). " +
                    "Одинаковое «Оборудование по месту» в соседних графах объединяется в одну ячейку. «Синхр. с приборами» (SLT_FSASYNC) — нумерация подряд и правка ссылок." };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(6) };
                var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
                var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Width = 90 };
                var renum = new Button { Text = "Нумеровать подряд", Width = 140 };
                renum.Click += (s, e) => { int n = 1; foreach (DataGridViewRow r in grid.Rows) if (!r.IsNewRow) r.Cells[0].Value = (n++).ToString(CultureInfo.InvariantCulture); };
                bar.Controls.Add(cancel); bar.Controls.Add(ok); bar.Controls.Add(renum);
                form.Controls.Add(grid); form.Controls.Add(hint); form.Controls.Add(props); form.Controls.Add(bar);
                form.CancelButton = cancel;
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                ((FsaView)props.SelectedObject).To(t);
                var newRows = t.AllRows();
                t.Columns = new List<FsaColumn>();
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (r.IsNewRow) continue;
                    var c = new FsaColumn { Num = Convert.ToString(r.Cells[0].Value) ?? "", Text = Convert.ToString(r.Cells[1].Value) ?? "", Equip = Convert.ToString(r.Cells[2].Value) ?? "" };
                    for (int i = 0; i < rows.Count; i++) if (r.Cells[3 + i].Value is bool b && b && newRows.Contains(rows[i])) c.Marks.Add(rows[i]);
                    t.Columns.Add(c);
                }
                return true;
            }
        }
    }

    public sealed class FsaView
    {
        [System.ComponentModel.DisplayName("Строки (группы)"), System.ComponentModel.Description("«Группа:строка,строка|Группа:…». Например «Функции контроллера:Измерение,Регулирование|Интерфейсы:Modbus,Ethernet». Новые строки появятся в сетке при следующем открытии.")]
        public string Rows { get; set; }
        [System.ComponentModel.DisplayName("Ширина графы, мм"), System.ComponentModel.Description("Подберите под шаг приборов на схеме.")] public double ColW { get; set; }
        [System.ComponentModel.DisplayName("Высота наименования, мм")] public double DescH { get; set; }
        [System.ComponentModel.DisplayName("Высота «Оборудование по месту», мм")] public double EquipH { get; set; }
        [System.ComponentModel.DisplayName("Высота строки, мм")] public double RowH { get; set; }
        [System.ComponentModel.DisplayName("Высота текста, мм")] public double H { get; set; }
        public FsaView(FsaTable t) { Rows = t.RowSpec; ColW = t.ColW; DescH = t.DescH; EquipH = t.EquipH; RowH = t.RowH; H = t.TextH; }
        public void To(FsaTable t) { t.RowSpec = Rows ?? t.RowSpec; t.ColW = Math.Max(5, ColW); t.DescH = Math.Max(15, DescH); t.EquipH = Math.Max(6, EquipH); t.RowH = Math.Max(4, RowH); t.TextH = H > 0 ? H : 2.5; }
    }
}
