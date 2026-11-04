// Таблицы на чертеже: общий построитель сетки, кабельный журнал, спецификация (ГОСТ 21.110), таблица ФСА.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    /// <summary>Сетка таблицы: начало — левый верхний угол, размеры — мм листа. Строка из одного элемента — заголовок раздела.</summary>
    public static class Grid
    {
        public static double Draw(Sketch s, double[] widths, string[] headers, double headerH, IList<string[]> rows, HAlign[] align, double h, double minRow = 8)
        {
            P2 P(double a, double b) => new P2(s.U(a), s.U(b));
            var xs = new List<double> { 0 }; foreach (var w in widths) xs.Add(xs[xs.Count - 1] + w);
            double W = xs[xs.Count - 1], y = 0;
            for (int c = 0; c < widths.Length; c++)
            {
                var lines = new List<string>();
                foreach (var part in (headers[c] ?? "").Split('\n')) lines.AddRange(Wrap(part, widths[c] - 1.5, h));
                double top = -headerH / 2 + (lines.Count - 1) * h * 0.9;
                for (int k = 0; k < lines.Count; k++) s.Text(P((xs[c] + xs[c + 1]) / 2, top - k * h * 1.8), lines[k], s.U(h), HAlign.Center, VAlign.Middle);
            }
            y = -headerH;
            s.Line(P(0, y), P(W, y), Weight.Thick);
            foreach (var r in rows)
            {
                if (r.Length == 1)
                {
                    // Заголовок раздела — во второй графе, подчёркнут.
                    double rh = minRow;
                    var tx = widths.Length > 1 ? xs[1] + 1 : 1;
                    s.Text(P(tx, y - rh / 2), r[0], s.U(h), HAlign.Left, VAlign.Middle);
                    s.Line(P(tx, y - rh / 2 - h * 0.7), P(tx + TextMeasure.Width(r[0], h), y - rh / 2 - h * 0.7));
                    y -= rh; s.Line(P(0, y), P(W, y));
                    continue;
                }
                var cells = new List<List<string>>();
                for (int c = 0; c < widths.Length; c++) cells.Add(Wrap(c < r.Length ? r[c] : "", widths[c] - 2, h));
                int n = cells.Max(l => l.Count);
                double rowH = Math.Max(minRow, minRow + (n - 1) * h * 1.8);
                for (int c = 0; c < widths.Length; c++)
                {
                    var l = cells[c];
                    double top = y - rowH / 2 + (l.Count - 1) * h * 0.9;
                    var a = c < align.Length ? align[c] : HAlign.Center;
                    var tx = a == HAlign.Left ? xs[c] + 1 : a == HAlign.Right ? xs[c + 1] - 1 : (xs[c] + xs[c + 1]) / 2;
                    for (int k = 0; k < l.Count; k++) s.Text(P(tx, top - k * h * 1.8), l[k], s.U(h), a, VAlign.Middle);
                }
                y -= rowH;
                s.Line(P(0, y), P(W, y));
            }
            s.Poly(true, Weight.Thick, P(0, 0), P(W, 0), P(W, y), P(0, y));
            for (int c = 1; c < widths.Length; c++) s.Line(P(xs[c], 0), P(xs[c], y), Weight.Thick);
            return -y;
        }

        public static List<string> Wrap(string text, double width, double h)
        {
            var res = new List<string>(); var cur = "";
            foreach (var word in (text ?? "").Split(' '))
            {
                var t = cur == "" ? word : cur + " " + word;
                if (cur != "" && TextMeasure.Width(t, h) > width) { res.Add(cur); cur = word; } else cur = t;
            }
            res.Add(cur);
            return res;
        }

        public static string Num(double v) => v.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));

        internal static Dictionary<string, string> SaveRows(List<string[]> rows, string prefix = "r")
        {
            var d = new Dictionary<string, string> { { prefix + "n", rows.Count.ToString(CultureInfo.InvariantCulture) } };
            for (int i = 0; i < rows.Count; i++) d[prefix + i] = string.Join("¦", rows[i].Select(x => (x ?? "").Replace("¦", "|")));
            return d;
        }

        internal static List<string[]> LoadRows(IDictionary<string, string> d, string prefix = "r")
        {
            var res = new List<string[]>();
            int n = d.TryGetValue(prefix + "n", out var v) && int.TryParse(v, out var x) ? x : 0;
            for (int i = 0; i < n; i++) res.Add(d.TryGetValue(prefix + i, out var r) ? r.Split('¦') : new string[0]);
            return res;
        }
    }

    // ===================== кабельный журнал =====================

    public sealed class JournalItem
    {
        public string Mark = "", From = "", To = "", Type = "", Note = "";
        public double Meters;
    }

    public sealed class Journal : Smart
    {
        public string Caption = "Кабельный журнал";
        public List<string[]> Rows = new List<string[]>();
        /// <summary>Заполнять по кабелям чертежа; выкл. — строки вводятся вручную (окно таблицы).</summary>
        public bool Auto = true;
        public double TextH = 2.5;
        public static readonly double[] Widths = { 25, 45, 45, 70, 15, 30 };
        public static readonly string[] Headers = { "Обозначение кабеля", "Начало", "Конец", "Марка кабеля", "Длина, м", "Примечание" };
        public override string Kind => "journal";
        public override string Title => "Кабельный журнал";

        public static List<string[]> Compose(IEnumerable<JournalItem> items) =>
            items.OrderBy(i => i.Mark, Natural.Comparer)
                 .Select(i => new[] { i.Mark, i.From, i.To, i.Type, i.Meters > 0 ? Grid.Num(i.Meters) : "", i.Note }).ToList();

        public override void Build(Sketch s)
        {
            if (Caption != "") s.Text(new P2(0, s.U(2)), Caption, s.U(3.5), HAlign.Left, VAlign.Bottom);
            Grid.Draw(s, Widths, Headers, 15, Rows, new[] { HAlign.Center, HAlign.Left, HAlign.Left, HAlign.Left, HAlign.Center, HAlign.Left }, TextH);
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;
        public override Dictionary<string, string> Save() { var d = Grid.SaveRows(Rows); d["caption"] = Caption; d["h"] = F(TextH); d["auto"] = Auto ? "1" : "0"; return d; }
        public override void Load(IDictionary<string, string> d) { Rows = Grid.LoadRows(d); Caption = S(d, "caption", "Кабельный журнал"); TextH = D(d, "h", 2.5); Auto = B(d, "auto", true); }
    }

    // ===================== спецификация (ГОСТ 21.110) =====================

    public sealed class SpecItem
    {
        public string Section = "", Pos = "", Name = "", Type = "", Code = "", Maker = "", Unit = "шт.", Note = "";
        public double Qty = 1, Mass;
    }

    public sealed class Spec : Smart
    {
        public string Caption = "";
        public List<string[]> Rows = new List<string[]>();
        public bool Auto = true;
        public double TextH = 2.5;
        public static readonly double[] Widths = { 20, 130, 60, 35, 45, 20, 20, 25, 40 };
        public static readonly string[] Headers = { "Поз.", "Наименование и техническая характеристика", "Тип, марка, обозначение документа, опросного листа", "Код продукции", "Поставщик", "Ед. изме-\nрения", "Коли-\nчество", "Масса 1 ед., кг", "Примечание" };
        public static readonly string[] Sections = { "Приборы и средства автоматизации", "Оборудование", "Кабельная продукция", "Прочее" };
        public override string Kind => "spec";
        public override string Title => "Спецификация оборудования";

        /// <summary>По разделам; одинаковые наименование+тип+поставщик+ед. — одной строкой, позиции через запятую или диапазоном.</summary>
        public static List<string[]> Compose(IEnumerable<SpecItem> items)
        {
            var rows = new List<string[]>();
            var bySection = items.Where(i => i.Name.Trim() != "" || i.Type.Trim() != "").GroupBy(i => i.Section)
                .OrderBy(g => { var k = Array.IndexOf(Sections, g.Key); return k < 0 ? 99 : k; }).ThenBy(g => g.Key);
            foreach (var sec in bySection)
            {
                if (sec.Key != "") rows.Add(new[] { sec.Key });
                var groups = sec.GroupBy(i => new { N = i.Name.Trim(), T = i.Type.Trim(), M = i.Maker.Trim(), U = i.Unit.Trim(), C = i.Code.Trim() })
                    .Select(g => new
                    {
                        g.Key,
                        pos = g.Select(i => i.Pos.Trim()).Where(p => p != "").Distinct().OrderBy(p => p, Natural.Comparer).ToList(),
                        qty = g.Sum(i => i.Qty), mass = g.Select(i => i.Mass).FirstOrDefault(m => m > 0),
                        note = string.Join("; ", g.Select(i => i.Note.Trim()).Where(n => n != "").Distinct()),
                    })
                    .OrderBy(g => g.pos.FirstOrDefault() ?? "￿", Natural.Comparer).ThenBy(g => g.Key.N);
                foreach (var g in groups)
                    rows.Add(new[] { PosList.Join(g.pos), g.Key.N, g.Key.T, g.Key.C, g.Key.M, g.Key.U, Grid.Num(g.qty), g.mass > 0 ? Grid.Num(g.mass) : "", g.note });
            }
            return rows;
        }

        public override void Build(Sketch s)
        {
            if (Caption != "") s.Text(new P2(0, s.U(2)), Caption, s.U(3.5), HAlign.Left, VAlign.Bottom);
            Grid.Draw(s, Widths, Headers, 15, Rows, new[] { HAlign.Center, HAlign.Left, HAlign.Left, HAlign.Center, HAlign.Left, HAlign.Center, HAlign.Center, HAlign.Center, HAlign.Left }, TextH);
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;
        public override Dictionary<string, string> Save() { var d = Grid.SaveRows(Rows); d["caption"] = Caption; d["h"] = F(TextH); d["auto"] = Auto ? "1" : "0"; return d; }
        public override void Load(IDictionary<string, string> d) { Rows = Grid.LoadRows(d); Caption = S(d, "caption"); TextH = D(d, "h", 2.5); Auto = B(d, "auto", true); }
    }

    // ===================== таблица ФСА =====================

    public sealed class FsaColumn
    {
        public string Num = "", Text = "", Equip = "";
        public HashSet<string> Marks = new HashSet<string>();
    }

    /// <summary>
    /// Табличная часть схемы автоматизации (как в проекте СУДИО): графы с номерами (на них ссылаются приборы),
    /// наименование параметра (вертикально), оборудование по месту (одинаковое в соседних графах — одной ячейкой),
    /// строки шкафа автоматизации и интерфейсов с точками.
    /// </summary>
    public sealed class FsaTable : Smart
    {
        public List<FsaColumn> Columns = new List<FsaColumn>();
        public string RowSpec = "Функции контроллера:Измерение,Регулирование,Сигнализация,Управление|Входы/выходы:Ai,Ao,Di,Do|Интерфейсы:IO-Link,Modbus,EtherCAT,Ethernet";
        public double ColW = 10, DescH = 55, EquipH = 14, RowH = 6, TextH = 2.5;
        public const double LeftW = 40, GroupW = 12, NumH = 7;
        public override string Kind => "fsa";
        public override string Title => "Таблица схемы автоматизации";

        public class Group { public string Name; public List<string> Rows = new List<string>(); }
        public List<Group> Groups()
        {
            var res = new List<Group>();
            foreach (var part in (RowSpec ?? "").Split('|'))
            {
                var i = part.IndexOf(':');
                var g = new Group { Name = i >= 0 ? part.Substring(0, i).Trim() : "" };
                g.Rows.AddRange((i >= 0 ? part.Substring(i + 1) : part).Split(',').Select(x => x.Trim()).Where(x => x != ""));
                if (g.Rows.Count > 0) res.Add(g);
            }
            return res;
        }
        public List<string> AllRows() => Groups().SelectMany(g => g.Rows).ToList();

        /// <summary>X центра графы (мм листа от левого края таблицы) — для привязки ссылок приборов.</summary>
        public double ColumnX(int i) => LeftW + (i + 0.5) * ColW;

        public override void Build(Sketch s)
        {
            P2 P(double a, double b) => new P2(s.U(a), s.U(b));
            double h = TextH, n = Columns.Count, W = LeftW + n * ColW;
            var groups = Groups();
            double yNum = 0, yDesc = -NumH, yEq = yDesc - DescH, yRows = yEq - EquipH;
            double yEnd = yRows - groups.Sum(g => g.Rows.Count) * RowH;

            // Левая шапка
            s.Text(P(LeftW / 2, yDesc - DescH / 2), "Наименование параметра", s.U(h), HAlign.Center, VAlign.Middle);
            s.Text(P(LeftW / 2, yNum - NumH / 2), "Графа", s.U(h), HAlign.Center, VAlign.Middle);
            var eq = Grid.Wrap("Оборудование по месту", LeftW - 2, h);
            for (int k = 0; k < eq.Count; k++) s.Text(P(LeftW / 2, yEq - EquipH / 2 + (eq.Count - 1) * h * 0.9 - k * h * 1.8), eq[k], s.U(h), HAlign.Center, VAlign.Middle);
            double y = yRows;
            foreach (var g in groups)
            {
                double gh = g.Rows.Count * RowH;
                var gl = Grid.Wrap(g.Name, gh - 1.5, h);
                int gmax = Math.Max(1, (int)Math.Floor((GroupW - 1) / (h * 1.6)));
                if (gl.Count > gmax) { gl = gl.Take(gmax).ToList(); gl[gmax - 1] += "…"; }
                for (int k = 0; k < gl.Count; k++) s.Text(P(GroupW / 2 - ((gl.Count - 1) / 2.0 - k) * h * 1.6, y - gh / 2), gl[k], s.U(h), HAlign.Center, VAlign.Middle, Math.PI / 2);
                for (int r = 0; r < g.Rows.Count; r++)
                {
                    s.Text(P(GroupW + 1.5, y - RowH * (r + 0.5)), g.Rows[r], s.U(h), HAlign.Left, VAlign.Middle);
                    if (r > 0) s.Line(P(GroupW, y - RowH * r), P(W, y - RowH * r));
                }
                y -= gh;
                s.Line(P(0, y), P(W, y), Weight.Thick);
            }
            s.Line(P(GroupW, yRows), P(GroupW, yEnd));

            // Графы
            for (int i = 0; i < Columns.Count; i++)
            {
                var c = Columns[i]; double x0 = LeftW + i * ColW, xc = x0 + ColW / 2;
                s.Text(P(xc, yNum - NumH / 2), c.Num, s.U(h), HAlign.Center, VAlign.Middle);
                // Наименование — вертикально, снизу вверх, до двух-трёх строк.
                var lines = Grid.Wrap(c.Text, DescH - 2, h);
                int maxLines = Math.Max(1, (int)Math.Floor((ColW - 1) / (h * 1.6)));
                if (lines.Count > maxLines) { lines = lines.Take(maxLines).ToList(); lines[maxLines - 1] += "…"; }
                for (int k = 0; k < lines.Count; k++)
                {
                    double off = ((lines.Count - 1) / 2.0 - k) * h * 1.6;
                    s.Text(P(xc - off, yEq + 1), lines[k], s.U(h), HAlign.Left, VAlign.Middle, Math.PI / 2);
                }
                double yy = yRows;
                foreach (var g in groups)
                    foreach (var r in g.Rows)
                    {
                        if (c.Marks.Contains(r)) Dot(s, P(xc, yy - RowH / 2), s.U(0.8));
                        yy -= RowH;
                    }
                if (i > 0) { s.Line(P(x0, yNum), P(x0, yEq)); s.Line(P(x0, yRows), P(x0, yEnd)); }
            }
            // Оборудование по месту: одинаковое в соседних графах — одна ячейка.
            for (int i = 0; i < Columns.Count;)
            {
                int j = i; var e = Columns[i].Equip ?? "";
                while (j + 1 < Columns.Count && e != "" && Columns[j + 1].Equip == e) j++;
                double x0 = LeftW + i * ColW, x1 = LeftW + (j + 1) * ColW;
                if (i > 0) s.Line(P(x0, yEq), P(x0, yRows));
                if (e != "")
                {
                    var wl = Grid.Wrap(e, x1 - x0 - 1.5, h);
                    if (j == i && wl.Count > 1) s.Text(P((x0 + x1) / 2, yEq - EquipH / 2), e, s.U(h), HAlign.Center, VAlign.Middle, Math.PI / 2);
                    else for (int k = 0; k < wl.Count; k++) s.Text(P((x0 + x1) / 2, yEq - EquipH / 2 + (wl.Count - 1) * h * 0.9 - k * h * 1.8), wl[k], s.U(h), HAlign.Center, VAlign.Middle);
                }
                i = j + 1;
            }
            // Рамки
            s.Poly(true, Weight.Thick, P(0, 0), P(W, 0), P(W, yEnd), P(0, yEnd));
            s.Line(P(0, yDesc), P(W, yDesc)); s.Line(P(0, yEq), P(W, yEq), Weight.Thick); s.Line(P(0, yRows), P(W, yRows), Weight.Thick);
            s.Line(P(LeftW, 0), P(LeftW, yEnd), Weight.Thick);
        }

        static void Dot(Sketch s, P2 c, double r)
        {
            var p = new P2[12];
            for (int i = 0; i < 12; i++) p[i] = c + new P2(Math.Cos(i * Math.PI / 6), Math.Sin(i * Math.PI / 6)) * r;
            s.Fill(p);
        }

        /// <summary>
        /// Синхронизация с приборами: недостающие графы добавляются (по номеру ссылки), затем графы нумеруются подряд 1…N,
        /// и ссылки приборов переписываются по новой нумерации. Возвращает карту «старый → новый» номер.
        /// </summary>
        public Dictionary<string, string> Sync(IList<Instrument> kips)
        {
            var have = new HashSet<string>(Columns.Select(c => c.Num));
            foreach (var k in kips)
                foreach (var n in Refs(k.Ref))
                    if (!have.Contains(n))
                    {
                        have.Add(n);
                        var col = new FsaColumn { Num = n, Text = k.Name };
                        if (k.Form != KipShape.Actuator) col.Marks.Add("Измерение"); else col.Marks.Add("Управление");
                        // На место по номеру среди числовых граф.
                        int at = Columns.FindIndex(c => Natural.Comparer.Compare(c.Num, n) > 0);
                        if (at < 0) Columns.Add(col); else Columns.Insert(at, col);
                    }
            var map = new Dictionary<string, string>();
            for (int i = 0; i < Columns.Count; i++)
            {
                var nn = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (Columns[i].Num != "" && !map.ContainsKey(Columns[i].Num)) map[Columns[i].Num] = nn;
                Columns[i].Num = nn;
            }
            foreach (var k in kips)
            {
                var parts = (k.Ref ?? "").Split(',');
                k.Ref = string.Join(",", parts.Select(p => map.TryGetValue(p.Trim(), out var v) ? v : p.Trim()));
            }
            return map;
        }

        public static IEnumerable<string> Refs(string r) => (r ?? "").Split(',', ';').Select(x => x.Trim()).Where(x => x != "");

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;

        public override Dictionary<string, string> Save()
        {
            var d = Grid.SaveRows(Columns.Select(c => new[] { c.Num, c.Text, c.Equip, string.Join(",", c.Marks) }).ToList(), "c");
            d["rows"] = RowSpec; d["cw"] = F(ColW); d["dh"] = F(DescH); d["eh"] = F(EquipH); d["rh"] = F(RowH); d["h"] = F(TextH);
            return d;
        }

        public override void Load(IDictionary<string, string> d)
        {
            Columns = Grid.LoadRows(d, "c").Select(r => new FsaColumn
            {
                Num = r.Length > 0 ? r[0] : "", Text = r.Length > 1 ? r[1] : "", Equip = r.Length > 2 ? r[2] : "",
                Marks = new HashSet<string>((r.Length > 3 ? r[3] : "").Split(',').Where(x => x != "")),
            }).ToList();
            RowSpec = S(d, "rows", RowSpec); ColW = D(d, "cw", 10); DescH = D(d, "dh", 55); EquipH = D(d, "eh", 14); RowH = D(d, "rh", 6); TextH = D(d, "h", 2.5);
        }
    }
}
