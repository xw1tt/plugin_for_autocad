// Каркас умных объектов: параметры — в XData блока (копируются с объектом и попадают в клон при перетаскивании ручки),
// геометрия — в анонимном блоке (видна без плагина) и вживую из параметров (с плагином). Настройки чертежа — в словаре.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Slt.Core;
using Shape = Slt.Core.Shape;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Leader = Slt.Core.Leader;
using GiTextStyle = Autodesk.AutoCAD.GraphicsInterface.TextStyle;

namespace Slt.Acad
{
    public static class Data
    {
        public const string App = "SLT";

        public static bool IsSmart(DBObject o) => o is BlockReference && o.GetXDataForApplication(App) != null;

        const string Ext = "SLT_DATA";
        const int Chunk = 100;            // символов в одной строке XData (ограничение 255 байт)
        const int XDataLimit = 4000;      // больше — в словарь расширений (XData ограничена 16 КБ)

        static IEnumerable<string> Lines(IDictionary<string, string> d)
        {
            foreach (var kv in d)
            {
                var v = kv.Value ?? "";
                int part = 0;
                do
                {
                    var chunk = v.Length > Chunk ? v.Substring(0, Chunk) : v;
                    yield return (part == 0 ? "" : "+") + kv.Key + "=" + chunk;
                    v = v.Length > Chunk ? v.Substring(Chunk) : "";
                    part++;
                } while (v.Length > 0);
            }
        }

        static void Parse(IEnumerable<string> lines, Dictionary<string, string> d)
        {
            foreach (var s in lines)
            {
                var cont = s.StartsWith("+");
                var eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var k = s.Substring(cont ? 1 : 0, eq - (cont ? 1 : 0)); var v = s.Substring(eq + 1);
                d[k] = cont && d.TryGetValue(k, out var prev) ? prev + v : v;
            }
        }

        /// <summary>Параметры: XData «ключ=значение» (длинные — продолжениями «+ключ=часть»); большие таблицы — в словаре расширений.</summary>
        public static Dictionary<string, string> Read(DBObject o)
        {
            var d = new Dictionary<string, string>();
            var rb = o.GetXDataForApplication(App);
            if (rb == null) return d;
            Parse(rb.AsArray().Skip(1).Select(tv => tv.Value as string).Where(x => x != null), d);
            if (d.TryGetValue("ext", out var ext) && ext == "1" && !o.ExtensionDictionary.IsNull)
            {
                using (var tr = o.ExtensionDictionary.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
                    if (dict.Contains(Ext) && tr.GetObject(dict.GetAt(Ext), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                        Parse(xr.Data.AsArray().Select(tv => tv.Value as string).Where(x => x != null), d);
                }
                d.Remove("ext");
            }
            return d;
        }

        public static void Write(Entity e, IDictionary<string, string> d)
        {
            var lines = Lines(d).ToList();
            var big = lines.Sum(l => l.Length) > XDataLimit && e.Database != null && !e.ObjectId.IsNull;
            if (big)
            {
                // В XData — только вид и масштаб (для фильтра и копирования), остальное — Xrecord.
                var head = new Dictionary<string, string> { { "kind", d.TryGetValue("kind", out var k) ? k : "" }, { "k", d.TryGetValue("k", out var kk) ? kk : "1" }, { "ext", "1" } };
                e.XData = new ResultBuffer(new[] { new TypedValue((int)DxfCode.ExtendedDataRegAppName, App) }.Concat(Lines(head).Select(l => new TypedValue((int)DxfCode.ExtendedDataAsciiString, l))).ToArray());
                if (e.ExtensionDictionary.IsNull) { if (!e.IsWriteEnabled) e.UpgradeOpen(); e.CreateExtensionDictionary(); }
                var tm = e.Database.TransactionManager;
                var tr = tm.TopTransaction;
                bool own = tr == null;
                if (own) tr = tm.StartTransaction();
                try
                {
                    var dict = (DBDictionary)tr.GetObject(e.ExtensionDictionary, OpenMode.ForWrite);
                    var data = new ResultBuffer(lines.Select(l => new TypedValue((int)DxfCode.Text, l)).ToArray());
                    if (dict.Contains(Ext)) ((Xrecord)tr.GetObject(dict.GetAt(Ext), OpenMode.ForWrite)).Data = data;
                    else { var xr = new Xrecord { Data = data }; dict.SetAt(Ext, xr); tr.AddNewlyCreatedDBObject(xr, true); }
                    if (own) tr.Commit();
                }
                finally { if (own) tr.Dispose(); }
                return;
            }
            e.XData = new ResultBuffer(new[] { new TypedValue((int)DxfCode.ExtendedDataRegAppName, App) }.Concat(lines.Select(l => new TypedValue((int)DxfCode.ExtendedDataAsciiString, l))).ToArray());
        }

        public static void EnsureApp(Transaction tr, Database db)
        {
            var t = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (t.Has(App)) return;
            t.UpgradeOpen();
            var r = new RegAppTableRecord { Name = App };
            t.Add(r); tr.AddNewlyCreatedDBObject(r, true);
        }
    }

    /// <summary>Настройки чертежа: масштаб оформления модели, базовая отметка (±0,000).</summary>
    public sealed class Settings
    {
        public double Scale = 100;
        public bool BaseSet;
        public double BaseY, BaseValue;

        const string Key = "SLT";

        public static Settings Read(Database db)
        {
            var s = new Settings();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(Key)) return s;
                var xr = tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) as Xrecord;
                var a = xr?.Data?.AsArray() ?? new TypedValue[0];
                for (int i = 0; i + 1 < a.Length; i += 2)
                {
                    var k = a[i].Value as string; var v = a[i + 1].Value as string;
                    double D() => double.Parse(v, CultureInfo.InvariantCulture);
                    try
                    {
                        if (k == "scale") s.Scale = D();
                        else if (k == "baseSet") s.BaseSet = v == "1";
                        else if (k == "baseY") s.BaseY = D();
                        else if (k == "baseValue") s.BaseValue = D();
                    }
                    catch (FormatException) { }
                }
            }
            return s;
        }

        public void Write(Transaction tr, Database db)
        {
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            var data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, "scale"), new TypedValue((int)DxfCode.Text, F(Scale)),
                new TypedValue((int)DxfCode.Text, "baseSet"), new TypedValue((int)DxfCode.Text, BaseSet ? "1" : "0"),
                new TypedValue((int)DxfCode.Text, "baseY"), new TypedValue((int)DxfCode.Text, F(BaseY)),
                new TypedValue((int)DxfCode.Text, "baseValue"), new TypedValue((int)DxfCode.Text, F(BaseValue)));
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains(Key)) ((Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite)).Data = data;
            else { nod.UpgradeOpen(); var xr = new Xrecord { Data = data }; nod.SetAt(Key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        }

        /// <summary>Единиц чертежа в 1 мм натуры (по INSUNITS; мм по умолчанию).</summary>
        public static double UnitsPerMm(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Meters: return 0.001;
                case UnitsValue.Centimeters: return 0.1;
                case UnitsValue.Decimeters: return 0.01;
                default: return 1;
            }
        }

        /// <summary>K — единиц чертежа в 1 мм листа: в модели — масштаб оформления, на листе — 1.</summary>
        public double KFor(Database db, ObjectId spaceId) => spaceId == SymbolUtilityServices.GetBlockModelSpaceId(db) ? Scale * UnitsPerMm(db) : 1;
    }

    /// <summary>Примитивы ядра → объекты AutoCAD (для содержимого блока и для живой отрисовки).</summary>
    public static class Draw
    {
        public const string StyleName = "СЛТ_ГОСТ";
        static readonly Dictionary<string, double> widthCache = new Dictionary<string, double>();

        /// <summary>Текстовый стиль ГОСТ 2.304 (тип А, наклонный): шрифт ISOCPEUR из поставки AutoCAD.</summary>
        public static ObjectId Style(Transaction tr, Database db)
        {
            var tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tt.Has(StyleName)) return tt[StyleName];
            tt.UpgradeOpen();
            var r = new TextStyleTableRecord { Name = StyleName, FileName = "isocpeur.ttf", XScale = 1.0 };
            r.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("ISOCPEUR", false, false, 0, 0);
            var id = tt.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
            return id;
        }

        /// <summary>Точная ширина текста по шрифту ISOCPEUR (для полок и рамок); при сбое — оценка ядра.</summary>
        public static void HookMeasure()
        {
            var fallback = TextMeasure.Width;
            TextMeasure.Width = (s, h) =>
            {
                if (string.IsNullOrEmpty(s) || h <= 0) return 0;
                var key = s + "\u0000" + h.ToString("R", CultureInfo.InvariantCulture);
                if (widthCache.TryGetValue(key, out var w)) return w;
                try
                {
                    var ts = new GiTextStyle { Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("ISOCPEUR", false, false, 0, 0), TextSize = h, XScale = 1 };
                    var ext = ts.ExtentsBox(s, true, false, null);
                    w = ext.MaxPoint.X - ext.MinPoint.X;
                    if (w <= 0) w = fallback(s, h);
                }
                catch { w = fallback(s, h); }
                if (widthCache.Count > 5000) widthCache.Clear();
                widthCache[key] = w;
                return w;
            };
        }

        static LineWeight Lw(Weight w) => w == Weight.Thick ? LineWeight.LineWeight050 : LineWeight.LineWeight025;
        static Point3d P(P2 p) => new Point3d(p.X, p.Y, 0);

        public static IEnumerable<Entity> Entities(IEnumerable<Shape> shapes, ObjectId style)
        {
            foreach (var s in shapes)
                foreach (var e in One(s, style))
                {
                    e.Color = s.Color == 0 ? Color.FromColorIndex(ColorMethod.ByBlock, 0) : Color.FromColorIndex(ColorMethod.ByAci, (short)s.Color);
                    yield return e;
                }
        }

        static IEnumerable<Entity> One(Shape s, ObjectId style)
        {
            switch (s.Kind)
            {
                case ShapeKind.Line: yield return new Line(P(s.Pts[0]), P(s.Pts[1])) { LineWeight = Lw(s.Weight) }; break;
                case ShapeKind.Polyline:
                    var pl = new Polyline(s.Pts.Length) { Closed = s.Closed, LineWeight = Lw(s.Weight) };
                    for (int i = 0; i < s.Pts.Length; i++) pl.AddVertexAt(i, new Point2d(s.Pts[i].X, s.Pts[i].Y), 0, 0, 0);
                    yield return pl;
                    break;
                case ShapeKind.Circle: yield return new Circle(P(s.Pts[0]), Vector3d.ZAxis, s.R) { LineWeight = Lw(s.Weight) }; break;
                case ShapeKind.Arc: yield return new Arc(P(s.Pts[0]), s.R, s.A0, s.A1) { LineWeight = Lw(s.Weight) }; break;
                case ShapeKind.Fill:
                    for (int i = 1; i + 1 < s.Pts.Length; i++) yield return new Solid(P(s.Pts[0]), P(s.Pts[i]), P(s.Pts[i + 1]));
                    break;
                case ShapeKind.Text:
                    var t = new DBText { TextString = s.Text, Height = s.H, Rotation = s.Rotation, WidthFactor = s.WidthFactor > 0 ? s.WidthFactor : 1 };
                    if (!style.IsNull) t.TextStyleId = style;
                    var at = P(s.Pts[0]);
                    if (s.Ha == HAlign.Left && s.Va == VAlign.Bottom) t.Position = at;
                    else
                    {
                        t.HorizontalMode = s.Ha == HAlign.Left ? TextHorizontalMode.TextLeft : s.Ha == HAlign.Center ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                        t.VerticalMode = s.Va == VAlign.Bottom ? TextVerticalMode.TextBottom : s.Va == VAlign.Middle ? TextVerticalMode.TextVerticalMid : TextVerticalMode.TextTop;
                        t.Position = at;
                        t.AlignmentPoint = at;
                    }
                    yield return t;
                    break;
            }
        }
    }

    /// <summary>Умный объект в чертеже: создание, чтение с живыми значениями, перестройка блока.</summary>
    public static class SmartRef
    {
        /// <summary>Объект из блока; у ассоциативной отметки — значение по текущему положению относительно базовой.</summary>
        public static Smart Load(BlockReference br, Settings s = null)
        {
            var o = Smart.From(Data.Read(br));
            if (o is LevelMark lm && lm.Associative && br.Database != null)
            {
                s = s ?? Settings.Read(br.Database);
                if (s.BaseSet && br.OwnerId == SymbolUtilityServices.GetBlockModelSpaceId(br.Database))
                    lm.Value = s.BaseValue + (br.Position.Y - s.BaseY) / Settings.UnitsPerMm(br.Database) / 1000.0;
            }
            return o;
        }

        public static BlockReference Create(Transaction tr, Database db, BlockTableRecord space, Smart o, Point3d at, double rotation = 0)
        {
            Data.EnsureApp(tr, db);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var btr = new BlockTableRecord { Name = "*U" };
            var bid = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            var br = new BlockReference(at, bid) { Rotation = rotation };
            br.SetDatabaseDefaults(db);
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            Data.Write(br, o.SaveAll());
            Fill(tr, db, btr, Load(br));
            return br;
        }

        /// <summary>Содержимое блока по параметрам (то, что видно без плагина).</summary>
        public static void Rebuild(Transaction tr, BlockReference br)
        {
            var db = br.Database;
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            foreach (ObjectId id in btr) tr.GetObject(id, OpenMode.ForWrite).Erase();
            Fill(tr, db, btr, Load(br));
        }

        static void Fill(Transaction tr, Database db, BlockTableRecord btr, Smart o)
        {
            var style = Draw.Style(tr, db);
            foreach (var e in Draw.Entities(o.Shapes(), style))
            {
                var col = e.Color; var lw = e.LineWeight;
                e.SetDatabaseDefaults(db);
                e.Color = col; e.LineWeight = lw;
                e.Layer = "0";
                if (e is DBText t) { t.TextStyleId = style; if (t.HorizontalMode != TextHorizontalMode.TextLeft || t.VerticalMode != TextVerticalMode.TextBase) t.AdjustAlignment(db); }
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }
        }

        public static void Save(BlockReference br, Smart o)
        {
            if (!br.IsWriteEnabled) br.UpgradeOpen();
            Data.Write(br, o.SaveAll());
        }

        /// <summary>Все умные объекты чертежа (модель и листы).</summary>
        public static List<BlockReference> All(Transaction tr, Database db, string kind = null, OpenMode mode = OpenMode.ForRead)
        {
            var res = new List<BlockReference>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId bid in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
                if (!btr.IsLayout) continue;
                foreach (ObjectId id in btr)
                {
                    if (id.ObjectClass.DxfName != "INSERT") continue;
                    var br = (BlockReference)tr.GetObject(id, mode);
                    if (!Data.IsSmart(br)) continue;
                    if (kind == null || Data.Read(br).TryGetValue("kind", out var k) && k == kind) res.Add(br);
                }
            }
            return res;
        }
    }
}
