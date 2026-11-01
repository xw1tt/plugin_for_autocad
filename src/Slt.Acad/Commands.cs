// Команды «СЛТ Графика»: вставка умных объектов с просмотром, правка свойств, масштаб оформления, нумерация листов.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Slt.Core;
using Shape = Slt.Core.Shape;
using Leader = Slt.Core.Leader;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Slt.Acad.Plugin))]
[assembly: CommandClass(typeof(Slt.Acad.Commands))]

namespace Slt.Acad
{
    public sealed class Plugin : IExtensionApplication
    {
        static SmartDraw draw;
        static SmartGrips grips;
        static ContextMenuExtension menu;

        public void Initialize()
        {
            try
            {
                Draw.HookMeasure();
                var cls = RXObject.GetClass(typeof(BlockReference));
                draw = new SmartDraw(); draw.SetXDataFilter(Data.App);
                grips = new SmartGrips(); grips.SetXDataFilter(Data.App);
                Overrule.AddOverrule(cls, draw, false);
                Overrule.AddOverrule(cls, grips, false);
                Overrule.Overruling = true;
                Sync.AttachAll();
                DoubleClick.Attach();

                menu = new ContextMenuExtension { Title = "СЛТ" };
                var item = new MenuItem("Свойства СЛТ…");
                item.Click += (s, e) => Commands.Send("_SLT_EDIT ");
                menu.MenuItems.Add(item);
                AcApp.AddObjectContextMenuExtension(cls, menu);

                Ui.Init();
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nСЛТ Графика загружена. Вкладка «СЛТ» на ленте, палитра — SLT_PALETTE.");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nСЛТ Графика: ошибка загрузки — " + ex.Message);
            }
        }

        public void Terminate() { }
    }

    /// <summary>Двойной щелчок по умному объекту — окно свойств вместо редактора блоков.</summary>
    static class DoubleClick
    {
        static object saved;

        public static void Attach()
        {
            AcApp.BeginDoubleClick += (s, e) =>
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var sel = doc.Editor.SelectImplied();
                if (sel.Status != PromptStatus.OK || sel.Value.Count != 1) return;
                bool smart;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                    smart = Data.IsSmart(tr.GetObject(sel.Value[0].ObjectId, OpenMode.ForRead));
                if (!smart) return;
                // Стандартное действие (BEDIT) отключается на время щелчка и возвращается после нашей команды.
                try { saved = AcApp.GetSystemVariable("DBLCLKEDIT"); AcApp.SetSystemVariable("DBLCLKEDIT", 0); } catch { saved = null; }
                Commands.Send("_SLT_EDIT ");
            };
            AcApp.Idle += (s, e) =>
            {
                if (saved == null) return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null && !doc.CommandInProgress.Equals("", StringComparison.Ordinal)) return;
                try { AcApp.SetSystemVariable("DBLCLKEDIT", saved); } catch { }
                saved = null;
            };
        }
    }

    public class Commands
    {
        static string lastAxis;

        public static void Send(string cmd) => AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(cmd, true, false, false);

        static Document Doc => AcApp.DocumentManager.MdiActiveDocument;
        static Editor Ed => Doc.Editor;
        static Point3d W(Point3d ucs) => ucs.TransformBy(Ed.CurrentUserCoordinateSystem);

        static Point3d? GetPoint(string msg, Point3d? basePt = null)
        {
            var o = new PromptPointOptions("\n" + msg) { AllowNone = true };
            if (basePt.HasValue) { o.UseBasePoint = true; o.BasePoint = basePt.Value.TransformBy(Ed.CurrentUserCoordinateSystem.Inverse()); }
            var r = Ed.GetPoint(o);
            return r.Status == PromptStatus.OK ? W(r.Value) : (Point3d?)null;
        }

        static double K(Database db) => Settings.Read(db).KFor(db, db.CurrentSpaceId);

        static bool InModel(Database db) => db.CurrentSpaceId == SymbolUtilityServices.GetBlockModelSpaceId(db);

        /// <summary>Запись нового объекта в текущее пространство.</summary>
        static ObjectId Insert(Smart o, Point3d at)
        {
            var db = Doc.Database;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var br = SmartRef.Create(tr, db, space, o, at);
                tr.Commit();
                return br.ObjectId;
            }
        }

        // ---------------- выноски ----------------

        [CommandMethod("SLT_LEADER", CommandFlags.Modal)]
        public void LeaderCmd() => PlaceLeader(false);

        [CommandMethod("SLT_POS", CommandFlags.Modal)]
        public void PosCmd() => PlaceLeader(true);

        void PlaceLeader(bool position)
        {
            var db = Doc.Database;
            var target = GetPoint(position ? "Точка на элементе (позиция):" : "Точка, на которую указывает выноска:");
            if (target == null) return;
            var l = new Leader { K = K(db), Position = position, End = LeaderEnd.Dot };
            if (position) { l.Top = NextPosition(db); l.TextH = 3.5; }
            else l.Top = "Текст";
            var t0 = target.Value;
            var knee = MakeLeaderJig(l, t0).Run(Ed);
            if (knee == null) return;
            l.Targets = new List<P2> { new P2(t0.X - knee.Value.X, t0.Y - knee.Value.Y) };
            if (!position && !Dialogs.Edit(l)) return;
            // Дополнительные выноски от той же полки.
            while (true)
            {
                var more = GetPoint("Ещё точка для выноски от этой полки <готово>:", knee);
                if (more == null) break;
                l.Targets.Add(new P2(more.Value.X - knee.Value.X, more.Value.Y - knee.Value.Y));
            }
            Insert(l, knee.Value);
        }

        static SmartJig MakeLeaderJig(Leader l, Point3d target)
        {
            SmartJig jig = null;
            jig = new SmartJig(l, "Точка излома (полки):", target, (o, p) =>
            {
                ((Leader)o).Targets = new List<P2> { new P2(target.X - p.X, target.Y - p.Y) };
                jig.Origin = p;
            }, target);
            return jig;
        }

        static string NextPosition(Database db)
        {
            int max = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                foreach (var br in SmartRef.All(tr, db, "leader"))
                {
                    var d = Data.Read(br);
                    if (d.TryGetValue("pos", out var p) && p == "1" && d.TryGetValue("top", out var t) && int.TryParse(t, out var n)) max = Math.Max(max, n);
                }
            return (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------- отметки уровня ----------------

        [CommandMethod("SLT_LEVELBASE", CommandFlags.Modal)]
        public void LevelBaseCmd() => SetBase();

        bool SetBase()
        {
            var db = Doc.Database;
            if (!InModel(db)) { Ed.WriteMessage("\nБазовая отметка задаётся в пространстве модели."); return false; }
            var p = GetPoint("Точка уровня с известной отметкой (обычно ±0,000):");
            if (p == null) return false;
            var o = new PromptDoubleOptions("\nОтметка этой точки, м <0>:") { AllowNone = true, DefaultValue = 0, UseDefaultValue = true };
            var r = Ed.GetDouble(o);
            if (r.Status != PromptStatus.OK) return false;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var s = Settings.Read(db);
                s.BaseSet = true; s.BaseY = p.Value.Y; s.BaseValue = r.Value;
                s.Write(tr, db);
                // Ассоциативные отметки пересчитываются.
                foreach (var br in SmartRef.All(tr, db, "level")) if (br.OwnerId == db.CurrentSpaceId) SmartRef.Rebuild(tr, br);
                tr.Commit();
            }
            Ed.WriteMessage("\nБазовая отметка: " + Levels.Format(r.Value) + ". Ассоциативные отметки считают высоту от неё.");
            return true;
        }

        [CommandMethod("SLT_LEVEL", CommandFlags.Modal)]
        public void LevelCmd()
        {
            var db = Doc.Database;
            var m = new LevelMark { K = K(db) };
            if (InModel(db))
            {
                if (!Settings.Read(db).BaseSet && !SetBase()) return;
            }
            else
            {
                m.Associative = false;
                var r = Ed.GetDouble(new PromptDoubleOptions("\nОтметка, м <0>:") { AllowNone = true, DefaultValue = 0, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return;
                m.Value = r.Value;
            }
            var s = Settings.Read(db); var upm = Settings.UnitsPerMm(db);
            SmartJig jig = null;
            jig = new SmartJig(m, "Точка отметки (остриё):", Point3d.Origin, (o, p) =>
            {
                jig.Origin = p;
                if (m.Associative && s.BaseSet) m.Value = s.BaseValue + (p.Y - s.BaseY) / upm / 1000.0;
            });
            while (true)
            {
                var at = jig.Run(Ed);
                if (at == null) return;
                Insert(m, at.Value);
                // Следующая отметка с теми же настройками — серия на разрезе.
                m = new LevelMark { K = m.K, Associative = m.Associative, Value = m.Value, Up = m.Up, Right = m.Right, TextH = m.TextH };
                var keep = m;
                jig = new SmartJig(keep, "Следующая отметка <готово>:", at.Value, (o, p) =>
                {
                    jig.Origin = p;
                    if (keep.Associative && s.BaseSet) keep.Value = s.BaseValue + (p.Y - s.BaseY) / upm / 1000.0;
                });
            }
        }

        [CommandMethod("SLT_LEVELPLAN", CommandFlags.Modal)]
        public void LevelPlanCmd()
        {
            var db = Doc.Database;
            var r = Ed.GetDouble(new PromptDoubleOptions("\nОтметка на плане, м <0>:") { AllowNone = true, DefaultValue = 0, UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return;
            var m = new PlanLevel { K = K(db), Value = r.Value };
            SmartJig jig = null;
            jig = new SmartJig(m, "Точка вставки:", Point3d.Origin, (o, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at != null) Insert(m, at.Value);
        }

        // ---------------- оси ----------------

        [CommandMethod("SLT_AXIS", CommandFlags.Modal)]
        public void AxisCmd()
        {
            var db = Doc.Database;
            var name = lastAxis == null ? "1" : AxisNames.Next(lastAxis);
            var nr = Ed.GetString(new PromptStringOptions("\nМарка оси <" + name + ">:") { AllowSpaces = false, DefaultValue = name, UseDefaultValue = true });
            if (nr.Status != PromptStatus.OK) return;
            name = string.IsNullOrWhiteSpace(nr.StringResult) ? name : nr.StringResult.Trim();
            var a = GetPoint("Начало оси:");
            if (a == null) return;
            var ax = new Axis { K = K(db), Name = name };
            var a0 = a.Value;
            var jig = new SmartJig(ax, "Конец оси (сторона кружка):", a0, (o, p) => ((Axis)o).End = new P2(p.X - a0.X, p.Y - a0.Y), a0);
            var b = jig.Run(Ed);
            if (b == null) return;
            ax.End = new P2(b.Value.X - a0.X, b.Value.Y - a0.Y);
            Insert(ax, a0);
            lastAxis = name;
        }

        [CommandMethod("SLT_AXES", CommandFlags.Modal)]
        public void AxesCmd()
        {
            var db = Doc.Database;
            var g = new AxisGrid { K = K(db) };
            if (!Dialogs.Edit(g)) return;
            SmartJig jig = null;
            jig = new SmartJig(g, "Точка пересечения первых осей (1/А):", Point3d.Origin, (o, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at != null) Insert(g, at.Value);
        }

        // ---------------- форматы ----------------

        [CommandMethod("SLT_FORMAT", CommandFlags.Modal)]
        public void FormatCmd()
        {
            var db = Doc.Database;
            var f = new SheetFormat { K = K(db) };
            // Поля штампа — как у последнего формата в чертеже.
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var last = SmartRef.All(tr, db, "format").LastOrDefault();
                if (last != null)
                {
                    var prev = (SheetFormat)SmartRef.Load(last);
                    foreach (var k in new[] { "code", "object", "section", "stage", "org", "r1", "r2", "r3", "r4", "r5", "n1", "n2", "n3", "n4", "n5", "d1", "d2", "d3", "d4", "d5" })
                        f.Fields[k] = prev.Field(k);
                    f.Size = prev.Size; f.Multiple = prev.Multiple; f.Landscape = prev.Landscape; f.Extra = prev.Extra;
                    f.Stamp = prev.Stamp == StampForm.Form3 ? StampForm.Form6 : prev.Stamp; // следующий лист — форма 6
                }
            }
            if (!Dialogs.Edit(f)) return;
            SmartJig jig = null;
            jig = new SmartJig(f, "Левый нижний угол листа:", Point3d.Origin, (o, p) => jig.Origin = p);
            var at = jig.Run(Ed);
            if (at == null) return;
            Insert(f, at.Value);
            if (f.AutoNumber) Number(db, quiet: true);
        }

        [CommandMethod("SLT_SHEETS", CommandFlags.Modal)]
        public void SheetsCmd() => Number(Doc.Database, quiet: false);

        /// <summary>Нумерация: на листах — по порядку вкладок, в модели — сверху вниз рядами, в ряду слева направо.</summary>
        static void Number(Database db, bool quiet)
        {
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var refs = SmartRef.All(tr, db, "format");
                var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                int Tab(BlockReference b)
                {
                    if (b.OwnerId == ms) return int.MaxValue;
                    var btr = (BlockTableRecord)tr.GetObject(b.OwnerId, OpenMode.ForRead);
                    return ((Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead)).TabOrder;
                }
                var items = refs.Select(b => new { b, o = (SheetFormat)SmartRef.Load(b), tab = Tab(b) }).ToList();
                double rowTol = items.Count == 0 ? 1 : items.Max(i => i.o.Paper[1] * i.o.K) / 2;
                var ordered = items.OrderBy(i => i.tab)
                    .ThenBy(i => -Math.Round(i.b.Position.Y / rowTol))
                    .ThenBy(i => i.b.Position.X).ToList();
                SheetFormat.Number(ordered.Select(i => i.o).ToList());
                foreach (var i in ordered)
                {
                    SmartRef.Save(i.b, i.o);
                    SmartRef.Rebuild(tr, i.b);
                }
                tr.Commit();
                if (!quiet) Ed.WriteMessage("\nПронумеровано листов: " + ordered.Count(i => i.o.AutoNumber) + ".");
            }
        }

        // ---------------- масштаб, правка ----------------

        [CommandMethod("SLT_SCALE", CommandFlags.Modal)]
        public void ScaleCmd()
        {
            var db = Doc.Database;
            var s = Settings.Read(db);
            var r = Ed.GetString(new PromptStringOptions("\nМасштаб оформления модели (1:100, 100…) <" + Scales.Name(s.Scale) + ">:") { AllowSpaces = false, DefaultValue = Scales.Name(s.Scale), UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return;
            double n;
            try { n = Scales.Parse(r.StringResult); } catch (System.Exception ex) { Ed.WriteMessage("\n" + ex.Message); return; }
            if (!Scales.Gost.Contains(n)) Ed.WriteMessage("\nВнимание: " + Scales.Name(n) + " нет в ряду ГОСТ 2.302.");
            var kw = new PromptKeywordOptions("\nПрименить к уже начерченным объектам в модели? [Да/Нет]", "Да Нет") { AllowNone = true };
            kw.Keywords.Default = "Нет";
            var k = Ed.GetKeywords(kw);
            if (k.Status != PromptStatus.OK && k.Status != PromptStatus.None) return;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                s.Scale = n; s.Write(tr, db);
                if (k.StringResult == "Да")
                {
                    var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var newK = s.KFor(db, ms);
                    foreach (var br in SmartRef.All(tr, db).Where(b => b.OwnerId == ms))
                    {
                        var o = SmartRef.Load(br, s);
                        // Геометрия, заданная в единицах чертежа (концы выносок, ось), остаётся; меняются размеры оформления.
                        o.K = newK;
                        SmartRef.Save(br, o);
                        SmartRef.Rebuild(tr, br);
                    }
                }
                tr.Commit();
            }
            Ed.WriteMessage("\nМасштаб оформления: " + Scales.Name(n) + ".");
            Ui.Refresh();
        }

        [CommandMethod("SLT_EDIT", CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void EditCmd()
        {
            var db = Doc.Database;
            ObjectId id = ObjectId.Null;
            var imp = Ed.SelectImplied();
            if (imp.Status == PromptStatus.OK && imp.Value.Count >= 1) id = imp.Value[0].ObjectId;
            if (id.IsNull)
            {
                var o = new PromptEntityOptions("\nОбъект СЛТ:");
                o.SetRejectMessage("\nНужен блок.");
                o.AddAllowedClass(typeof(BlockReference), false);
                var r = Ed.GetEntity(o);
                if (r.Status != PromptStatus.OK) return;
                id = r.ObjectId;
            }
            Ed.SetImpliedSelection(new ObjectId[0]);
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br) || !Data.IsSmart(br)) { Ed.WriteMessage("\nЭто не объект СЛТ."); return; }
                var obj = SmartRef.Load(br);
                if (!Dialogs.Edit(obj)) return;
                SmartRef.Save(br, obj);
                SmartRef.Rebuild(tr, br);
                tr.Commit();
            }
        }

        [CommandMethod("SLT_PALETTE", CommandFlags.Modal)]
        public void PaletteCmd() => Ui.ShowPalette();

        [CommandMethod("SLT_REBUILD", CommandFlags.Modal)]
        public void RebuildCmd()
        {
            var db = Doc.Database;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var all = SmartRef.All(tr, db);
                foreach (var br in all) SmartRef.Rebuild(tr, br);
                tr.Commit();
                Ed.WriteMessage("\nПерестроено объектов: " + all.Count + ".");
            }
        }
    }
}
