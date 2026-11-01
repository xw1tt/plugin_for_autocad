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
    { "stage", "Р" }, { "org", "ООО «ТехноИнжиниринг»" }, { "n1", "Рябов" }, { "n2", "Петров" }, { "d1", "11.26" } }) f.Fields[kv.Key] = kv.Value;
var f6 = new SheetFormat { Size = "A4", Stamp = StampForm.Form6 };
f6.Fields["code"] = f.Fields["code"];
SheetFormat.Number(new[] { f, f6 });
File.WriteAllText(Path.Combine(dir, "format-a3.svg"), Svg.Document(420, 297, Svg.Group(f.Shapes(), 0, 297), 3));
File.WriteAllText(Path.Combine(dir, "format-a4-form6.svg"), Svg.Document(210, 297, Svg.Group(f6.Shapes(), 0, 297), 3));
Console.WriteLine("ok");

static class Ext { public static T With<T>(this T o, Action<T> a) { a(o); return o; } }
