// Живое поведение умных объектов: отрисовка из параметров (DrawableOverrule), ручки (GripOverrule),
// синхронизация содержимого блока после правок и копирования (события базы → после команды), просмотр при вставке (DrawJig).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Slt.Core;
using Shape = Slt.Core.Shape;
using Leader = Slt.Core.Leader;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Slt.Acad
{
    static class Paint
    {
        /// <summary>Рисует объект в локальных координатах через временные объекты (тот же вид, что и в блоке).</summary>
        public static void Shapes(WorldGeometry g, IEnumerable<Shape> shapes, Database db, Matrix3d transform, Entity owner)
        {
            ObjectId style = ObjectId.Null;
            if (db != null)
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    if (tt.Has(Draw.StyleName)) style = tt[Draw.StyleName];
                }
            g.PushModelTransform(transform);
            try
            {
                foreach (var e in Draw.Entities(shapes, style))
                {
                    using (e)
                    {
                        if (owner != null) { e.Color = owner.Color; e.LayerId = owner.LayerId; }
                        if (e is DBText t && db != null && (t.HorizontalMode != TextHorizontalMode.TextLeft || t.VerticalMode != TextVerticalMode.TextBase)) t.AdjustAlignment(db);
                        g.Draw(e);
                    }
                }
            }
            finally { g.PopModelTransform(); }
        }
    }

    /// <summary>Отрисовка из параметров: при перетаскивании ручки клон с новыми параметрами виден сразу.</summary>
    public sealed class SmartDraw : DrawableOverrule
    {
        public override bool WorldDraw(Drawable d, WorldDraw wd)
        {
            if (!(d is BlockReference br)) return base.WorldDraw(d, wd);
            try
            {
                var o = SmartRef.Load(br);
                Paint.Shapes(wd.Geometry, o.Shapes(), br.Database ?? HostApplicationServices.WorkingDatabase, br.BlockTransform, br);
                return true;
            }
            catch { return base.WorldDraw(d, wd); }
        }
    }

    /// <summary>Ручки объекта (концы выносок, излом, остриё отметки, концы оси…) вместо единственной ручки блока.</summary>
    public sealed class SmartGrips : GripOverrule
    {
        public override void GetGripPoints(Entity e, Point3dCollection grips, IntegerCollection snapModes, IntegerCollection geometryIds)
        {
            if (!(e is BlockReference br)) { base.GetGripPoints(e, grips, snapModes, geometryIds); return; }
            try
            {
                foreach (var g in SmartRef.Load(br).Grips()) grips.Add(new Point3d(g.X, g.Y, 0).TransformBy(br.BlockTransform));
            }
            catch { base.GetGripPoints(e, grips, snapModes, geometryIds); }
        }

        public override void MoveGripPointsAt(Entity e, IntegerCollection indices, Vector3d offset)
        {
            if (!(e is BlockReference br)) { base.MoveGripPointsAt(e, indices, offset); return; }
            try
            {
                var o = SmartRef.Load(br);
                var grips = o.Grips();
                var tf = br.BlockTransform; var inv = tf.Inverse();
                foreach (int i in indices)
                {
                    if (i < 0 || i >= grips.Count) continue;
                    var w = new Point3d(grips[i].X, grips[i].Y, 0).TransformBy(tf) + offset;
                    var l = w.TransformBy(inv);
                    var shift = o.MoveGrip(i, new P2(l.X, l.Y));
                    if (Math.Abs(shift.X) > 1e-12 || Math.Abs(shift.Y) > 1e-12)
                    {
                        br.Position = br.Position + new Vector3d(shift.X, shift.Y, 0).TransformBy(tf);
                        tf = br.BlockTransform; inv = tf.Inverse();
                        grips = o.Grips();
                    }
                }
                Data.Write(br, o.SaveAll());
            }
            catch { base.MoveGripPointsAt(e, indices, offset); }
        }
    }

    /// <summary>
    /// Синхронизация блока с параметрами: менять объекты в событиях базы нельзя — изменённые и добавленные копии
    /// запоминаются и обрабатываются после команды. Копия получает собственный анонимный блок.
    /// </summary>
    public sealed class Sync
    {
        static readonly Dictionary<Database, Sync> all = new Dictionary<Database, Sync>();
        readonly Document doc;
        readonly HashSet<ObjectId> dirty = new HashSet<ObjectId>(), added = new HashSet<ObjectId>();
        public static bool Suspended;

        Sync(Document d) { doc = d; }

        public static void AttachAll()
        {
            foreach (Document d in AcApp.DocumentManager) Attach(d);
            AcApp.DocumentManager.DocumentCreated += (s, e) => Attach(e.Document);
            AcApp.DocumentManager.DocumentToBeDestroyed += (s, e) => { if (e.Document != null) all.Remove(e.Document.Database); };
        }

        static void Attach(Document d)
        {
            if (d == null || all.ContainsKey(d.Database)) return;
            var s = new Sync(d);
            all[d.Database] = s;
            d.Database.ObjectModified += (o, e) => { if (!Suspended && Data.IsSmart(e.DBObject)) s.dirty.Add(e.DBObject.ObjectId); };
            d.Database.ObjectAppended += (o, e) => { if (!Suspended && e.DBObject is BlockReference) s.added.Add(e.DBObject.ObjectId); };
            d.CommandEnded += (o, e) => s.Flush(e.GlobalCommandName);
            d.CommandCancelled += (o, e) => s.Flush(e.GlobalCommandName);
        }

        void Flush(string cmd)
        {
            var c = (cmd ?? "").ToUpperInvariant();
            if (c == "U" || c == "UNDO" || c == "REDO" || c == "MREDO") { dirty.Clear(); added.Clear(); return; }
            if (dirty.Count == 0 && added.Count == 0) return;
            Suspended = true;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var s = Settings.Read(doc.Database);
                    // Копии: блок общий с другим объектом — отдельный анонимный блок.
                    var addedRefs = added.Where(id => !id.IsErased).Select(id => tr.GetObject(id, OpenMode.ForRead) as BlockReference).Where(b => b != null && Data.IsSmart(b)).ToList();
                    if (addedRefs.Count > 0)
                    {
                        var users = SmartRef.All(tr, doc.Database).GroupBy(b => b.BlockTableRecord).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList());
                        foreach (var b in addedRefs)
                        {
                            if (!users.ContainsKey(b.BlockTableRecord)) continue;
                            var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
                            var btr = new BlockTableRecord { Name = "*U" };
                            var nid = bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                            b.UpgradeOpen();
                            b.BlockTableRecord = nid;
                            dirty.Add(b.ObjectId);
                        }
                    }
                    foreach (var id in dirty.Where(x => !x.IsErased))
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference b && Data.IsSmart(b)) SmartRef.Rebuild(tr, b);
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\nСЛТ: " + ex.Message); }
            finally { Suspended = false; dirty.Clear(); added.Clear(); }
        }
    }

    /// <summary>Вставка с просмотром: объект рисуется в точке курсора, update настраивает его по точке.</summary>
    public sealed class SmartJig : DrawJig
    {
        readonly Smart obj;
        readonly string prompt;
        readonly Action<Smart, Point3d> update;
        readonly Point3d? basePoint;
        public Point3d Origin;
        Point3d last = new Point3d(double.NaN, 0, 0);

        public SmartJig(Smart o, string prompt, Point3d origin, Action<Smart, Point3d> update, Point3d? basePoint = null)
        { obj = o; this.prompt = prompt; Origin = origin; this.update = update; this.basePoint = basePoint; }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var o = new JigPromptPointOptions("\n" + prompt) { UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted };
            if (basePoint.HasValue) { o.UseBasePoint = true; o.BasePoint = basePoint.Value; o.Cursor = CursorType.RubberBand; }
            var r = prompts.AcquirePoint(o);
            if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (r.Value.IsEqualTo(last)) return SamplerStatus.NoChange;
            last = r.Value;
            update(obj, r.Value);
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            Paint.Shapes(draw.Geometry, obj.Shapes(), HostApplicationServices.WorkingDatabase, Matrix3d.Displacement(Origin - Point3d.Origin), null);
            return true;
        }

        /// <summary>Показ и результат: точка или null при отмене.</summary>
        public Point3d? Run(Editor ed)
        {
            var r = ed.Drag(this);
            return r.Status == PromptStatus.OK ? last : (Point3d?)null;
        }
    }
}
