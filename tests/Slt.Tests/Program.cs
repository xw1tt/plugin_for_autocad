using System;
using System.Collections.Generic;
using System.Linq;
using Slt.Core;

int fails = 0, total = 0;
void Eq<T>(string name, T expected, T actual)
{
    total++;
    if (Equals(expected, actual)) return;
    fails++; Console.WriteLine($"ПРОВАЛ {name}: ожидалось «{expected}», получено «{actual}»");
}
void True(string name, bool ok) => Eq(name, true, ok);

// Отметки
Eq("отметка +", "+3,500", Levels.Format(3.5));
Eq("отметка 0", "±0,000", Levels.Format(0));
Eq("отметка -0", "±0,000", Levels.Format(-0.0001));
Eq("отметка -", "-0,450", Levels.Format(-0.45));

// Масштабы
Eq("масштаб 1:100", 100.0, Scales.Parse("1:100"));
Eq("масштаб 2,5", 2.5, Scales.Parse("2,5"));
Eq("имя 1:2,5", "1:2,5", Scales.Name(2.5));

// Оси
Eq("ось 1→2", "2", AxisNames.Next("1"));
Eq("ось Ж→И", "И", AxisNames.Next("Ж"));
Eq("ось Н→П", "П", AxisNames.Next("Н"));
Eq("ось Я→АА", "АА", AxisNames.Next("Я"));
Eq("ось А1→А2", "А2", AxisNames.Next("А1"));
Eq("ось нестандартная", "Q1", AxisNames.Next("Q"));
Eq("ряд", "А,Б,В,Г", string.Join(",", AxisNames.Sequence("А", 4)));
True("нет запрещённых букв", !AxisNames.Sequence("А", 22).Any(n => "ЁЗЙОХЦЧЩЪЫЬ".Contains(n)));
Eq("шаги", "6000,6000,3000", string.Join(",", AxisNames.Spans("6000*2, 3000")));
Eq("шаги с x", 4, AxisNames.Spans("1500x4").Count);

// Сериализация всех объектов
foreach (var kind in new[] { "leader", "level", "levelplan", "axis", "axes", "format" })
{
    var o = Smart.Create(kind); o.K = 100;
    var d = o.SaveAll();
    var back = Smart.From(d);
    Eq("вид " + kind, kind, back.Kind);
    Eq("K " + kind, 100.0, back.K);
    Eq("сохранение " + kind, string.Join(";", d.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)), string.Join(";", back.SaveAll().OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
    True("рисуется " + kind, back.Shapes().Count > 0);
}

// Выноска: перенос излома не сдвигает концы выносок
var l = new Leader { Targets = new List<P2> { new P2(-10, -5) } };
var shift = l.MoveGrip(0, new P2(3, 4));
Eq("излом: сдвиг", "(3; 4)", shift.ToString());
Eq("излом: конец на месте", "(-10; -5)", (l.Targets[0] + shift).ToString());
True("полка влево при выноске вправо", !new Leader { Targets = new List<P2> { new P2(10, -5) } }.ShelfRight);

// Отметка: ручка полки меняет сторону
var m = new LevelMark(); m.MoveGrip(1, new P2(-5, -3));
True("отметка влево-вниз", !m.Right && !m.Up);

// Высота текста зависит от масштаба
var lm = new Leader { K = 100, Top = "Т" };
Eq("высота текста ×K", 250.0, lm.Shapes().First(s => s.Kind == ShapeKind.Text).H);

// Форматы
Eq("А4 книжный", "210x297", string.Join("x", new SheetFormat { Size = "A4", Landscape = true }.Paper));
Eq("А3 альбомный", "420x297", string.Join("x", new SheetFormat { Size = "A3" }.Paper));
Eq("А4×3", "630x297", string.Join("x", new SheetFormat { Size = "A4", Multiple = 3 }.Paper));
var sheets = new List<SheetFormat> { new SheetFormat(), new SheetFormat { Stamp = StampForm.Form6 }, new SheetFormat { AutoNumber = false }, new SheetFormat { Stamp = StampForm.Form6 } };
sheets[2].Fields["sheet"] = "10";
SheetFormat.Number(sheets);
Eq("лист 1", "1", sheets[0].Field("sheet"));
Eq("листов", "3", sheets[0].Field("sheets"));
Eq("лист 3 (пропуск ручного)", "3", sheets[3].Field("sheet"));
Eq("ручной не тронут", "10", sheets[2].Field("sheet"));
Eq("форма 6 без «листов»", "", sheets[1].Field("sheets"));

// Длинный текст в XData-подобной сериализации
var f = new SheetFormat(); f.Fields["object"] = new string('Ж', 700);
Eq("длинное поле", 700, ((SheetFormat)Smart.From(f.SaveAll())).Field("object").Length);

Console.WriteLine($"Проверок: {total}, провалено: {fails}");
return fails == 0 ? 0 : 1;
