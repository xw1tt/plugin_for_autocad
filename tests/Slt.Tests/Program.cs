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

// Библиотека
var lib = Library.Default();
Eq("библиотека без ошибок", "", string.Join("; ", lib.Errors));
True("символов > 30", lib.Symbols.Count > 30);
var user = Library.Default(); user.Parse("[Моё]\n= vent.damper | Заслонка моя |\nC 0 0 2", "польз.");
Eq("замена по id", "Заслонка моя", user.Get("vent.damper").Name);
Eq("одна заслонка", 1, user.Symbols.Count(x => x.Id == "vent.damper"));
var bad = new Library(); bad.Parse("= x | X\nQ 1 2", "t");
Eq("ошибка строки", 1, bad.Errors.Count);
foreach (var kind in new[] { "kip", "sym", "list" })
{
    var o = Smart.Create(kind); var back = Smart.From(o.SaveAll());
    Eq("сохранение " + kind, string.Join(";", o.SaveAll().OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)), string.Join(";", back.SaveAll().OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
}

// КИП
var te = new Instrument { Pos = "TE1", K = 100 };
var edge = te.Edge(new P2(0, -1000));
Eq("контур снизу", -500.0, Math.Round(edge.Y, 3));
var wide = new Instrument { Pos = "DPSZ12345" };
True("длинный текст — шире окружности", wide.Edge(new P2(1, 0)).X > 5.01);
var pi = new Instrument { Form = KipShape.Panel, Pos = "TIC7" };
True("на щите — две строки", pi.Shapes().Count(s => s.Kind == ShapeKind.Text) == 2);
var mv = new Instrument { Tap = new P2(0, -15), RefAt = new P2(0, 15), HasRef = true };
var sh = mv.MoveGrip(0, new P2(2, 3));
Eq("центр: отбор на месте", "(0; -15)", (mv.Tap + sh).ToString());
Eq("расшифровка TE", "температура; чувствительный элемент (первичное преобразование)", KipLetters.Explain("TE10"));
Eq("расшифровка PDT", "давление, разрежение; разность, перепад; преобразование, дистанционная передача", KipLetters.Explain("PDT1"));
True("расшифровка DPS", KipLetters.Explain("DPS3").StartsWith("перепад: давление"));
Eq("кириллица", "температура; показание", KipLetters.Explain("ТI1"));

// Перечень
Eq("диапазон", "Y1–Y3", PosList.Join(new List<string> { "Y1", "Y2", "Y3" }));
Eq("два — через запятую", "DPS1, DPS2", PosList.Join(new List<string> { "DPS1", "DPS2" }));
Eq("с разрывом", "TE1, TE3, TE4", PosList.Join(new List<string> { "TE1", "TE3", "TE4" }));
var rows = PosList.Compose(new[] { new PosItem("TE10", "Датчик", 1), new PosItem("TE2", "Датчик", 1), new PosItem("Y1", "Привод", 2), new PosItem("", "Кабель", 30) });
Eq("строк", 3, rows.Count);
Eq("порядок естественный", "TE2, TE10", rows[0][0]);
Eq("кол-во", "2", rows[1][2]);
Eq("без позиции — в конце", "Кабель", rows[2][1]);

// Кабели
var cb = new Cable { Path = new List<P2> { new P2(0, 0), new P2(3000, 0), new P2(3000, 4000) } };
Eq("длина по чертежу, м", 7.0, cb.Length(1));
cb.Reserve = 10; Eq("с запасом 10% вверх до метра", 8.0, cb.Length(1));
cb.Meters = 12; Eq("вручную", 12.0, cb.Length(1));
Eq("на листе без ручной — 0", 0.0, new Cable().Length(0));
var errs = new List<string>();
Eq("встроенные виды линий", "open,pipe,corr,tray,box,hose", string.Join(",", LineKinds.Parse(LineKinds.DefaultText(), "t", errs).Select(k => k.Id)));
Eq("виды без ошибок", 0, errs.Count);
var kinds = LineKinds.Parse(LineKinds.DefaultText(), "t", errs);
LineKinds.Parse("= tray | Лоток свой | 1 | | 8 2 | 0\n= sig | Сигнальная | 3 | | | 0", "u", errs, kinds);
LineKinds.Current = kinds;
Eq("замена по id", "Лоток свой", LineKinds.Get("tray").Name);
Eq("свой вид добавлен", 7, kinds.Count);
var dashed = new Cable { Line = "tray", ShowMark = false, Path = new List<P2> { new P2(0, 0), new P2(100, 0) } };
True("штриховая — много отрезков", dashed.Shapes().Count(x => x.Kind == ShapeKind.Line) >= 8);
True("свой цвет", dashed.Shapes().First().Color == 1);
LineKinds.Current = LineKinds.Parse(LineKinds.DefaultText(), "t", errs);
var piped = new Cable { Line = "pipe", ShowMark = false, Path = new List<P2> { new P2(0, 0), new P2(100, 0) } };
Eq("знаки трубы через 35 мм", 3, piped.Shapes().Count(x => x.Kind == ShapeKind.Circle));
Eq("линия трубы с разрывами", 4, piped.Shapes().Count(x => x.Kind == ShapeKind.Polyline && x.Pts.Length == 2 && Math.Abs(x.Pts[0].Y) < 1e-9 && Math.Abs(x.Pts[1].Y) < 1e-9));
var pieces = LineKinds.Split(new List<P2> { new P2(0, 0), new P2(10, 0), new P2(10, 10) }, new List<double[]> { new[] { 8.0, 12.0 } });
Eq("разрыв через вершину", "2", pieces.Count.ToString());
Eq("конец первого куска", "(8; 0)", pieces[0].Last().ToString());
Eq("начало второго", "(10; 2)", pieces[1][0].ToString());
var waved = new Cable { Line = "corr", ShowMark = false, Path = new List<P2> { new P2(0, 0), new P2(100, 0) } };
Eq("гофра: линия сплошная + 3 волны × 3 знака", 1 + 9, waved.Shapes().Count(x => x.Kind == ShapeKind.Polyline));
var bent = new Cable { Line = "box", ShowMark = false, Path = new List<P2> { new P2(0, 0), new P2(10, 0), new P2(10, 10) } };
Eq("короткие участки — знак посередине", 2, bent.Shapes().Count(x => x.Kind == ShapeKind.Polyline && x.Closed));
var lbl = new Cable { Mark = "К5", Path = new List<P2> { new P2(0, 0), new P2(-50, 0) } }.Shapes().First(x => x.Kind == ShapeKind.Text);
Eq("подпись не вверх ногами", 0.0, Math.Round(lbl.Rotation, 6));
foreach (var kind in new[] { "cable", "journal", "spec", "fsa", "legend" })
{
    var o = Smart.Create(kind); var back = Smart.From(o.SaveAll());
    Eq("сохранение " + kind, string.Join(";", o.SaveAll().OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)), string.Join(";", back.SaveAll().OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
    True("рисуется " + kind, back.Shapes().Count > 0);
}
var jrows = Journal.Compose(new[] { new JournalItem { Mark = "К10", Meters = 5 }, new JournalItem { Mark = "К2", Meters = 7.5 } });
Eq("журнал по порядку", "К2", jrows[0][0]);
Eq("длина с запятой", "7,5", jrows[0][4]);

// Спецификация
var srows = Spec.Compose(new[] {
    new SpecItem { Section = "Кабельная продукция", Name = "Кабель", Type = "UTP", Unit = "м", Qty = 10 },
    new SpecItem { Section = "Приборы и средства автоматизации", Pos = "TE2", Name = "Датчик", Type = "TS" },
    new SpecItem { Section = "Приборы и средства автоматизации", Pos = "TE1", Name = "Датчик", Type = "TS" },
    new SpecItem { Section = "Кабельная продукция", Name = "Кабель", Type = "UTP", Unit = "м", Qty = 15 } });
Eq("раздел приборов первым", "Приборы и средства автоматизации", srows[0][0]);
Eq("позиции", "TE1, TE2", srows[1][0]);
Eq("кол-во датчиков", "2", srows[1][6]);
Eq("кабель суммой", "25", srows[3][6]);

// Таблица ФСА: синхронизация
var ft = new FsaTable();
ft.Columns.Add(new FsaColumn { Num = "1", Text = "a" });
ft.Columns.Add(new FsaColumn { Num = "5", Text = "b" });
var k1 = new Instrument { Pos = "TE1", Ref = "5", Name = "T" };
var k2 = new Instrument { Pos = "Y1", Ref = "3,7", Form = KipShape.Actuator, Name = "Привод" };
ft.Sync(new[] { k1, k2 });
Eq("граф после синхронизации", 4, ft.Columns.Count);
Eq("нумерация подряд", "1,2,3,4", string.Join(",", ft.Columns.Select(c => c.Num)));
Eq("ссылка прибора 5→3", "3", k1.Ref);
Eq("ссылка ИМ 3,7→2,4", "2,4", k2.Ref);
True("ИМ — управление", ft.Columns[1].Marks.Contains("Управление"));
var fb = (FsaTable)Smart.From(ft.SaveAll());
Eq("ФСА сохраняет графы", "1|a|2|Привод|3|b|4|Привод", string.Join("|", fb.Columns.Select(c => c.Num + "|" + c.Text)));

// Оси: ручки меняют расстояние между соседними осями
var ag = new AxisGrid { XSpans = "6000*3", YSpans = "6000" };
Eq("ручек осей", 1 + 3 + 1, ag.Grips().Count);
ag.MoveGrip(2, new P2(13000, 0));
Eq("ось 3 сдвинута", "6000, 7000, 5000", ag.XSpans);
ag.MoveGrip(3, new P2(20000, 0));
Eq("крайняя ось — только её шаг", "6000, 7000*2", ag.XSpans);
Eq("компактно", "6000*3, 3000", AxisGrid.Compact(new List<double> { 6000, 6000, 6000, 3000 }));
var agm = new AxisGrid { XSpans = "6000", YSpans = "6000", Units = 0.001 };
Eq("чертёж в метрах", 6.0, agm.Xs()[1]);
True("марка растёт с кружком", new AxisGrid { Diameter = 30 }.Shapes().Where(x => x.Kind == ShapeKind.Text && x.Text == "1").All(x => x.H > 10));

// Штамп: свои должности, подписи одной высоты
var sf = new SheetFormat(); sf.Fields["r1"] = "Главный инженер проекта"; sf.Fields["n1"] = "Константинопольский";
var stx = sf.Shapes().Where(x => x.Kind == ShapeKind.Text).ToList();
True("своя должность", stx.Any(x => x.Text == "Главный инженер проекта"));
Eq("подписи — 2,5", 2.5, stx.First(x => x.Text == "Главный инженер проекта").H);
Eq("фамилия — 2,5", 2.5, stx.First(x => x.Text == "Константинопольский").H);
True("длинное — сжато по ширине", stx.First(x => x.Text == "Главный инженер проекта").WidthFactor < 1);
var cap = stx.First(x => x.Text.StartsWith("Формат"));
True("«Формат» над краем листа", cap.Va == VAlign.Bottom && cap.Pts[0].Y > 0.5);
var nums = new List<SheetFormat> { new SheetFormat(), new SheetFormat { Stamp = StampForm.Form6 } };
SheetFormat.Number(nums, 5);
Eq("с 5-го", "6", nums[1].Field("sheet")); Eq("листов с учётом начала", "6", nums[0].Field("sheets"));

// Ручные таблицы
var mj = new Journal { Auto = false, Rows = new List<string[]> { new[] { "К1", "ШУ", "Д1", "UTP", "10", "" } } };
var mjb = (Journal)Smart.From(mj.SaveAll());
True("ручной журнал сохраняется", !mjb.Auto && mjb.Rows[0][3] == "UTP");

Console.WriteLine($"Проверок: {total}, провалено: {fails}");
return fails == 0 ? 0 : 1;
