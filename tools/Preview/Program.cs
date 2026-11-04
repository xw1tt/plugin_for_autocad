using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Slt.Core;

var dir = args.Length > 0 ? args[0] : "docs";
Directory.CreateDirectory(dir);
string Label(double x, double y, string t) => $"<text x=\"{x}\" y=\"{y}\" font-size=\"2.6\" font-family=\"Arial\" fill=\"#06c\">{System.Security.SecurityElement.Escape(t)}</text>";

// Лист 1: выноски, отметки, оси — в мм листа (K = 1).
var b = new StringBuilder();
void Put(Smart o, double x, double y, string cap) { b.Append(Svg.Group(o.Shapes(), x, y)); b.Append(Label(x - 14, y + 22, cap)); }
Put(new Leader { Targets = { }, Top = "Кабельный лоток 200×50", Bottom = "h=2500", End = LeaderEnd.Dot }.With(l => l.Targets = new List<P2> { new P2(-12, -10) }), 30, 30, "Выноска, точка (внутри контура)");
Put(new Leader { Top = "Лоток", End = LeaderEnd.Arrow }.With(l => l.Targets = new List<P2> { new P2(-12, -10), new P2(-4, -12) }), 110, 30, "Две выноски, стрелки (на контур)");
Put(new Leader { Top = "12", Position = true, TextH = 3.5, End = LeaderEnd.Dot }.With(l => l.Targets = new List<P2> { new P2(10, -10) }), 175, 30, "Позиционная, полка влево");
Put(new LevelMark { Value = 3.5 }, 30, 70, "Отметка +3,500");
Put(new LevelMark { Value = 0 }, 80, 70, "±0,000");
Put(new LevelMark { Value = -0.45, Up = false, Note = "низ лотка" }, 130, 70, "Вниз, с примечанием");
Put(new LevelMark { Value = 12.6, Right = false }, 185, 70, "Полка влево");
Put(new PlanLevel { Value = 3.2 }, 30, 100, "На плане");
Put(new Axis { Name = "5", End = new P2(0, 30) }, 80, 85, "Ось");
Put(new Axis { Name = "Б", End = new P2(40, 0), BubbleAtStart = true }, 120, 100, "Ось, кружки с двух сторон");
File.WriteAllText(Path.Combine(dir, "objects.svg"), Svg.Document(210, 125, b.ToString()));

// Лист 2: массив осей в масштабе 1:200 на листе (K = 200, шаги в мм натуры) — показываем в мм листа: делим на 200.
var g = new AxisGrid { XSpans = "6000*3,3000", YSpans = "6000,4500", K = 1 };
var gs = new AxisGrid { XSpans = "30*3,15", YSpans = "30,22.5", K = 1 };
File.WriteAllText(Path.Combine(dir, "axes.svg"), Svg.Document(170, 110, Svg.Group(gs.Shapes(), 45, 75)));

// Лист 3: формат А3 с формой 3, лист 2 — А4 с формой 6.
var f = new SheetFormat { Size = "A3", Landscape = true };
foreach (var kv in new Dictionary<string, string> {
    { "code", "АТ-4182-03-РД3.9.15-СУДИО" }, { "object", "Аэропорт Оренбург (центральный) им. Ю.А.Гагарина. Новый аэровокзальный комплекс" },
    { "section", "Система управления и диспетчеризации инженерного оборудования" }, { "title", "План кабельных трасс на отм. 0.000" },
    { "stage", "Р" }, { "org", "ООО «ТехноИнжиниринг»" }, { "r1", "Глав. инженер" }, { "n1", "Рябов" }, { "r2", "Нач. отдела автоматизации" }, { "n2", "Петров" }, { "d1", "11.26" } }) f.Fields[kv.Key] = kv.Value;
var f6 = new SheetFormat { Size = "A4", Stamp = StampForm.Form6 };
f6.Fields["code"] = f.Fields["code"];
SheetFormat.Number(new[] { f, f6 });
File.WriteAllText(Path.Combine(dir, "format-a3.svg"), Svg.Document(420, 297, Svg.Group(f.Shapes(), 0, 297), 3));
File.WriteAllText(Path.Combine(dir, "format-a4-form6.svg"), Svg.Document(210, 297, Svg.Group(f6.Shapes(), 0, 297), 3));
// Лист 4: библиотека символов — таблица.
var lib = Library.Current;
foreach (var e in lib.Errors) Console.WriteLine("ОШИБКА библиотеки: " + e);
var lb = new StringBuilder(); int col = 0; double ly = 15; string grp = null;
foreach (var d in lib.Symbols)
{
    if (d.Group != grp) { if (col != 0) { ly += 30; col = 0; } grp = d.Group; lb.Append($"<text x=\"5\" y=\"{ly - 7}\" font-size=\"4\" font-family=\"Arial\" font-weight=\"bold\">{grp}</text>"); }
    double x = 18 + col * 34; if (d.Bounds()[3] > 9) ly += 0;
    var sy = new Symbol { Sym = d.Id, Pos = d.Prefix == "" ? "" : d.Prefix + "1" }; sy.PosAt = sy.DefaultPosAt(); lb.Append(Svg.Group(sy.Shapes(), x, ly + 4));
    lb.Append($"<text x=\"{x}\" y=\"{ly + 16}\" font-size=\"2.2\" font-family=\"Arial\" text-anchor=\"middle\" fill=\"#06c\">{System.Security.SecurityElement.Escape(d.Name.Length > 26 ? d.Name.Substring(0, 25) + "…" : d.Name)}</text>");
    if (++col == 6) { col = 0; ly += 30; }
}
File.WriteAllText(Path.Combine(dir, "library.svg"), Svg.Document(210, ly + 30, lb.ToString()));

// Лист 5: фрагмент схемы автоматизации в стиле проекта СУДИО + перечень.
var sb = new StringBuilder();
void L(double x1, double y1, double x2, double y2) => sb.Append(Svg.Group(new List<Shape> { new Shape { Kind = ShapeKind.Line, Pts = new[] { new P2(x1, y1), new P2(x2, y2) }, Weight = Weight.Thick } }, 0, 0));
void Pt(Smart o, double x, double y) => sb.Append(Svg.Group(o.Shapes(), x, -y));
double Y = -60;
L(20, Y, 230, Y);
Pt(new Symbol { Sym = "vent.flow", Angle = 0 }, 20, Y);
Pt(new Instrument { Pos = "TE1", Tap = new P2(0, -14), HasRef = true, Ref = "1", RefAt = new P2(0, 18) }, 40, Y + 14);
Pt(new Symbol { Sym = "vent.damper", Pos = "Y1", PosAt = new P2(0, 4) }, 60, Y);
Pt(new Instrument { Form = KipShape.Actuator, Pos = "", Inner = "M", Diameter = 6, TapDot = false, Tap = new P2(0, 9), HasRef = true, Ref = "15,22", RefAt = new P2(0, -10) }, 60, Y - 9);
Pt(new Symbol { Sym = "vent.filter" }, 85, Y);
Pt(new Instrument { Pos = "DPS1", HasTap = false, HasRef = true, Ref = "25", RefAt = new P2(0, -10) }, 85, Y - 16);
Pt(new Symbol { Sym = "vent.heater" }, 115, Y);
Pt(new Instrument { Pos = "TE7", Tap = new P2(0, -14), HasRef = true, Ref = "7", RefAt = new P2(0, 18) }, 140, Y + 14);
Pt(new Symbol { Sym = "vent.fan", Pos = "M1", PosAt = new P2(0, 7) }, 165, Y);
Pt(new Instrument { Form = KipShape.Panel, Pos = "TIC7", Tap = new P2(0, -14), HasRef = false }, 190, Y + 14);
Pt(new Instrument { Form = KipShape.Paz, Pos = "TSZ1", Tap = new P2(0, -14) }, 215, Y + 14);
Pt(new Symbol { Sym = "vent.flow", Angle = Math.PI }, 233, Y);
var list = new PosList { Rows = PosList.Compose(new[] {
    new PosItem("DPS1", "Реле перепада давления DPS-500", 1), new PosItem("DPS2", "Реле перепада давления DPS-500", 1),
    new PosItem("TE1", "Канальный датчик температуры TS-K250 PRO PT1000", 1), new PosItem("TE3", "Канальный датчик температуры TS-K250 PRO PT1000", 1), new PosItem("TE10", "Канальный датчик температуры TS-K250 PRO PT1000", 1),
    new PosItem("TE7", "Канальный датчик температуры TS-K250 PRO PT1000", 1),
    new PosItem("Y1", "Привод воздушной заслонки ODA-15(24)-M-024-N-A", 1), new PosItem("Y2", "Привод воздушной заслонки ODA-15(24)-M-024-N-A", 1), new PosItem("Y3", "Привод воздушной заслонки ODA-15(24)-M-024-N-A", 1) }) };
Pt(list, 120, -95);
File.WriteAllText(Path.Combine(dir, "kip.svg"), Svg.Document(250, 165, sb.ToString()));
// Лист 6: таблица ФСА, линии связи, журнал, спецификация.
var t = new StringBuilder();
var fsa = new FsaTable();
string[][] cols = {
    new[] { "1", "Температура наружного воздуха Pt1000, -50…+120°С", "Концентраторы IOL-AI8T-M12 x2", "Измерение,IO-Link,EtherCAT" },
    new[] { "2", "Температура воздуха до рекуператора Pt1000", "Концентраторы IOL-AI8T-M12 x2", "Измерение,IO-Link,EtherCAT" },
    new[] { "3", "Температура воздуха после рекуператора Pt1000", "Концентраторы IOL-AI8T-M12 x2", "Измерение,IO-Link,EtherCAT" },
    new[] { "4", "Температура обратной воды 1 подогрев", "Концентраторы IOL-AI8T-M12 x2", "Измерение,IO-Link,EtherCAT" },
    new[] { "5", "Относит. влажность приточного воздуха 0-10В", "Концентратор IOL-AI8U-M12", "Измерение,IO-Link,EtherCAT" },
    new[] { "6", "Перепад давления на прит. вент. 0…1000 Па", "Концентратор IOL-AI8U-M12", "Измерение,IO-Link,EtherCAT" },
    new[] { "7", "Управление приводом заслонки", "", "Управление,Do" },
    new[] { "8", "Авария ПЧ", "", "Сигнализация,Di,Modbus" } };
foreach (var c in cols) fsa.Columns.Add(new FsaColumn { Num = c[0], Text = c[1], Equip = c[2], Marks = new HashSet<string>(c[3].Split(',')) });
t.Append(Svg.Group(fsa.Shapes(), 10, 10));
var leg = new LineLegend();
t.Append(Svg.Group(leg.Shapes(), 150, 20));
var cab = new Cable { Mark = "К20", Type = "КСПЭВПнг(А)-HF 2x2x0,97", ShowType = true, Line = "pipe", Path = new List<P2> { new P2(0, 0), new P2(60, 0), new P2(60, -30) } };
t.Append(Svg.Group(cab.Shapes(), 150, 70));
var cab2 = new Cable { Mark = "К21", Line = "corr", Path = new List<P2> { new P2(0, 0), new P2(-40, 25) } };
t.Append(Svg.Group(cab2.Shapes(), 260, 110));
var jr = new Journal { Rows = Journal.Compose(new[] {
    new JournalItem { Mark = "К21", From = "ШУ-ПВ1", To = "TE1", Type = "КСПЭВПнг(А)-HF 2x2x0,97", Meters = 35 },
    new JournalItem { Mark = "К3", From = "ШУ-ПВ1", To = "Y1", Type = "КСПЭВПнг(А)-HF 2x2x0,97", Meters = 12 } }) };
t.Append(Svg.Group(jr.Shapes(), 10, 178));
var sp = new Spec { Rows = Spec.Compose(new[] {
    new SpecItem { Section = "Приборы и средства автоматизации", Pos = "TE1", Name = "Канальный датчик температуры", Type = "TS-K250 PRO PT1000", Maker = "Регин" },
    new SpecItem { Section = "Приборы и средства автоматизации", Pos = "TE2", Name = "Канальный датчик температуры", Type = "TS-K250 PRO PT1000", Maker = "Регин" },
    new SpecItem { Section = "Приборы и средства автоматизации", Pos = "Y1", Name = "Привод воздушной заслонки", Type = "ODA-15(24)-M-024-N-A" },
    new SpecItem { Section = "Кабельная продукция", Name = "Кабель контрольный", Type = "КСПЭВПнг(А)-HF 2x2x0,97", Unit = "м", Qty = 47 } }) };
t.Append(Svg.Group(sp.Shapes(), 10, 228));
File.WriteAllText(Path.Combine(dir, "tables.svg"), Svg.Document(420, 297, t.ToString(), 3));
Console.WriteLine("ok");

static class Ext { public static T With<T>(this T o, Action<T> a) { a(o); return o; } }
