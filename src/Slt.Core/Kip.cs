// КИП и А (ГОСТ 21.208-2013): прибор — окружность (по месту), с чертой (на щите), квадрат (цифровая техника), ромб (ПАЗ),
// исполнительный механизм — малая окружность «M». Линия отбора с точкой на технологической линии, ссылка на графу таблицы.
// Перечень «Поз. обозначение — Наименование — Кол-во» собирается из приборов и символов.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Slt.Core
{
    public enum KipShape { Field, Panel, Digital, Paz, Actuator }

    public sealed class Instrument : Smart
    {
        public KipShape Form = KipShape.Field;
        public string Pos = "TE1";
        /// <summary>Текст внутри обозначения; пусто — позиция («TE10»). У ИМ — «M», позиция («Y2») выносится.</summary>
        public string Inner = "";
        public bool TwoLines;           // буквы над чертой, номер под ней (на щите — всегда)
        public string Ref = "";         // номер графы таблицы / клеммы
        public bool HasTap = true, TapDot = true, HasRef;
        public P2 Tap = new P2(0, -15), RefAt = new P2(0, 15), PosAt = new P2(-6, 6); // единицы чертежа
        public string Name = "", Note = "";
        public string Type = "", Maker = "", Code = "", Unit = "шт.";
        public double Qty = 1, Mass;
        public double Diameter = 10, TextH = 2.5;

        public override string Kind => "kip";
        public override string Title => Form == KipShape.Actuator ? "Исполнительный механизм" : "Прибор КИП";
        bool PosOutside => Inner != "";

        // ---- геометрия обозначения ----
        string[] InnerLines()
        {
            var t = PosOutside ? Inner : Pos;
            if (!(TwoLines || Form == KipShape.Panel) || PosOutside) return new[] { t };
            var m = Regex.Match(t ?? "", @"^(\D*)(.*)$");
            return m.Groups[2].Value.Length > 0 && m.Groups[1].Value.Length > 0 ? new[] { m.Groups[1].Value, m.Groups[2].Value } : new[] { t };
        }

        /// <summary>Половина прямого участка «стадиона» (для длинного текста), радиус — мм листа.</summary>
        void Size(out double hs, out double r)
        {
            r = Diameter / 2;
            var w = InnerLines().Max(l => TextMeasure.Width(l, TextH)) + 2.4;
            hs = Form == KipShape.Paz ? 0 : Math.Max(0, (w - Diameter) / 2);
            if (Form == KipShape.Paz) r = Math.Max(Diameter / 2, w / 2) * 1.25;
        }

        double Dist(P2 p, double hs, double r)
        {
            switch (Form)
            {
                case KipShape.Digital: return Math.Max(Math.Abs(p.X) - hs - r, Math.Abs(p.Y) - r);
                case KipShape.Paz: return (Math.Abs(p.X) + Math.Abs(p.Y)) / Math.Sqrt(2) - r / Math.Sqrt(2);
                default: var x = Math.Max(0, Math.Abs(p.X) - hs); return Math.Sqrt(x * x + p.Y * p.Y) - r;
            }
        }

        /// <summary>Точка контура в направлении to (в единицах чертежа).</summary>
        public P2 Edge(P2 to)
        {
            Size(out var hs, out var r); hs *= K; r *= K;
            var d = to.Unit;
            double lo = 0, hi = (hs + r) * 3;
            for (int i = 0; i < 50; i++) { var m = (lo + hi) / 2; if (Dist(d * m, hs, r) < 0) lo = m; else hi = m; }
            return d * lo;
        }

        public override void Build(Sketch s)
        {
            Size(out var hs, out var r); hs = s.U(hs); r = s.U(r);
            var th = Weight.Thick;
            switch (Form)
            {
                case KipShape.Digital:
                    s.Poly(true, th, new P2(-hs - r, -r), new P2(hs + r, -r), new P2(hs + r, r), new P2(-hs - r, r));
                    break;
                case KipShape.Paz:
                    s.Poly(true, th, new P2(-r, 0), new P2(0, r), new P2(r, 0), new P2(0, -r));
                    break;
                default:
                    if (hs < 1e-9) s.Circle(new P2(0, 0), r, th);
                    else
                    {
                        s.Line(new P2(-hs, r), new P2(hs, r), th); s.Line(new P2(-hs, -r), new P2(hs, -r), th);
                        s.Shapes.Add(new Shape { Kind = ShapeKind.Arc, Pts = new[] { new P2(hs, 0) }, R = r, A0 = -Math.PI / 2, A1 = Math.PI / 2, Weight = th });
                        s.Shapes.Add(new Shape { Kind = ShapeKind.Arc, Pts = new[] { new P2(-hs, 0) }, R = r, A0 = Math.PI / 2, A1 = 3 * Math.PI / 2, Weight = th });
                    }
                    break;
            }
            var lines = InnerLines();
            if (Form == KipShape.Panel) s.Line(new P2(-hs - r, 0), new P2(hs + r, 0));
            if (lines.Length == 2)
            {
                s.Text(new P2(0, s.U(0.8)), lines[0], s.U(TextH), HAlign.Center, VAlign.Bottom);
                s.Text(new P2(0, -s.U(0.8)), lines[1], s.U(TextH), HAlign.Center, VAlign.Top);
            }
            else s.Text(new P2(0, 0), lines[0], s.U(TextH), HAlign.Center, VAlign.Middle);

            if (HasTap && Tap.Length > 1e-9)
            {
                s.Line(Edge(Tap), Tap);
                if (TapDot) Dot(s, Tap, s.U(0.6));
            }
            if (HasRef && RefAt.Length > 1e-9)
            {
                s.Line(Edge(RefAt), RefAt);
                Label(s, RefAt, RefAt - Edge(RefAt), Ref);
            }
            if (PosOutside && Pos != "") s.Text(PosAt, Pos, s.U(TextH), HAlign.Center, VAlign.Bottom);
        }

        static void Dot(Sketch s, P2 c, double r)
        {
            var p = new P2[12];
            for (int i = 0; i < 12; i++) p[i] = c + new P2(Math.Cos(i * Math.PI / 6), Math.Sin(i * Math.PI / 6)) * r;
            s.Fill(p);
        }

        /// <summary>Подпись в конце линии: по направлению линии — над, под, слева или справа.</summary>
        void Label(Sketch s, P2 at, P2 dir, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var g = s.U(1); var h = s.U(TextH);
            if (Math.Abs(dir.Y) >= Math.Abs(dir.X))
                s.Text(at + new P2(0, dir.Y > 0 ? g : -g), text, h, HAlign.Center, dir.Y > 0 ? VAlign.Bottom : VAlign.Top);
            else
                s.Text(at + new P2(dir.X > 0 ? g : -g, 0), text, h, dir.X > 0 ? HAlign.Left : HAlign.Right, VAlign.Middle);
        }

        // Ручки: центр (отбор, ссылка и позиция остаются на месте), точка отбора, конец ссылки, позиция ИМ — только включённые.
        List<int> GripKinds()
        {
            var k = new List<int> { 0 };
            if (HasTap) k.Add(1);
            if (HasRef) k.Add(2);
            if (PosOutside) k.Add(3);
            return k;
        }

        public override IList<P2> Grips() => GripKinds().Select(k => k == 0 ? new P2(0, 0) : k == 1 ? Tap : k == 2 ? RefAt : PosAt).ToList();

        public override P2 MoveGrip(int i, P2 to)
        {
            var kinds = GripKinds();
            if (i < 0 || i >= kinds.Count) return new P2(0, 0);
            switch (kinds[i])
            {
                case 0: Tap = Tap - to; RefAt = RefAt - to; PosAt = PosAt - to; return to;
                case 1: Tap = to; break;
                case 2: RefAt = to; break;
                case 3: PosAt = to; break;
            }
            return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "shape", Form.ToString() }, { "pos", Pos }, { "inner", Inner }, { "two", TwoLines ? "1" : "0" }, { "ref", Ref },
            { "hastap", HasTap ? "1" : "0" }, { "dot", TapDot ? "1" : "0" }, { "hasref", HasRef ? "1" : "0" },
            { "pts", Pts(new[] { Tap, RefAt, PosAt }) }, { "name", Name }, { "note", Note }, { "qty", F(Qty) }, { "d", F(Diameter) }, { "type", Type }, { "maker", Maker }, { "code", Code }, { "unit", Unit }, { "mass", F(Mass) }, { "h", F(TextH) },
        };

        public override void Load(IDictionary<string, string> d)
        {
            Form = Enum.TryParse(S(d, "shape", "Field"), out KipShape sh) ? sh : KipShape.Field;
            Pos = S(d, "pos"); Inner = S(d, "inner"); TwoLines = B(d, "two", false); Ref = S(d, "ref");
            HasTap = B(d, "hastap", true); TapDot = B(d, "dot", true); HasRef = B(d, "hasref", false);
            var p = Pts(S(d, "pts")); if (p.Count >= 3) { Tap = p[0]; RefAt = p[1]; PosAt = p[2]; }
            Name = S(d, "name"); Note = S(d, "note"); Qty = D(d, "qty", 1); Type = S(d, "type"); Maker = S(d, "maker"); Code = S(d, "code"); Unit = S(d, "unit", "шт."); Mass = D(d, "mass", 0); Diameter = D(d, "d", 10); TextH = D(d, "h", 2.5);
        }

        /// <summary>Заготовки: датчик по месту, прибор на щите, ИМ.</summary>
        public static Instrument Actuator(string pos) => new Instrument { Form = KipShape.Actuator, Pos = pos, Inner = "M", Diameter = 6, TapDot = false, Tap = new P2(0, 8), PosAt = new P2(-5, 9) };
    }

    /// <summary>Расшифровка буквенного кода по ГОСТ 21.208-2013, табл. 2 (подсказка при вводе позиции).</summary>
    public static class KipLetters
    {
        static readonly Dictionary<char, string> First = new Dictionary<char, string>
        {
            ['A'] = "анализ (состав, концентрация)", ['B'] = "пламя, горение", ['E'] = "напряжение", ['F'] = "расход", ['H'] = "ручное воздействие",
            ['I'] = "ток", ['J'] = "мощность", ['K'] = "время, временная программа", ['L'] = "уровень", ['P'] = "давление, разрежение", ['Q'] = "количество",
            ['R'] = "радиоактивность", ['S'] = "скорость, частота", ['T'] = "температура", ['U'] = "несколько разнородных величин", ['V'] = "вибрация",
            ['W'] = "вес, сила, масса", ['Y'] = "событие, состояние", ['Z'] = "размер, положение, перемещение",
            ['C'] = "по выбору пользователя", ['D'] = "по выбору пользователя", ['G'] = "по выбору пользователя", ['M'] = "по выбору пользователя (часто — влажность)",
            ['N'] = "по выбору пользователя", ['O'] = "по выбору пользователя",
        };
        static readonly Dictionary<char, string> Modifier = new Dictionary<char, string>
        { ['D'] = "разность, перепад", ['F'] = "соотношение, доля", ['J'] = "автоматическое переключение, обегание", ['Q'] = "интегрирование, суммирование по времени" };
        static readonly Dictionary<char, string> Func = new Dictionary<char, string>
        {
            ['A'] = "сигнализация", ['C'] = "автоматическое регулирование", ['E'] = "чувствительный элемент (первичное преобразование)", ['G'] = "первичный показывающий прибор",
            ['I'] = "показание", ['K'] = "станция управления", ['R'] = "регистрация", ['S'] = "включение, отключение, переключение, блокировка",
            ['T'] = "преобразование, дистанционная передача", ['Y'] = "вспомогательное вычислительное устройство", ['Z'] = "ПАЗ",
            ['H'] = "верхний предел", ['L'] = "нижний предел",
        };

        static readonly Dictionary<char, char> Cyr = new Dictionary<char, char> { ['А'] = 'A', ['В'] = 'B', ['С'] = 'C', ['Е'] = 'E', ['Н'] = 'H', ['К'] = 'K', ['М'] = 'M', ['О'] = 'O', ['Р'] = 'P', ['Т'] = 'T', ['Х'] = 'X', ['У'] = 'Y' };

        public static string Letters(string pos)
        {
            var m = Regex.Match((pos ?? "").Trim().ToUpperInvariant(), @"^([A-ZА-Я]+)");
            return new string(m.Groups[1].Value.Select(c => Cyr.TryGetValue(c, out var l) ? l : c).ToArray());
        }

        public static string Explain(string pos)
        {
            var code = Letters(pos);
            if (code.Length == 0) return "";
            var parts = new List<string>();
            int i = 0;
            // «DP», «DT» — перепад (так часто пишут по ISA: DPS — реле перепада давления).
            if (code.Length >= 2 && code[0] == 'D' && First.ContainsKey(code[1]) && code[1] != 'D') { parts.Add("перепад: " + First[code[1]]); i = 2; }
            else { parts.Add(First.TryGetValue(code[0], out var f) ? f : "«" + code[0] + "» — нет в табл. 2"); i = 1; if (i < code.Length && Modifier.ContainsKey(code[i]) && code.Length > 2) { parts.Add(Modifier[code[i]]); i++; } }
            for (; i < code.Length; i++) parts.Add(Func.TryGetValue(code[i], out var fn) ? fn : "«" + code[i] + "» — ?");
            return string.Join("; ", parts);
        }
    }

    /// <summary>Строка перечня: что посчитать.</summary>
    public sealed class PosItem
    {
        public string Pos = "", Name = "";
        public double Qty = 1;
        public PosItem() { }
        public PosItem(string pos, string name, double qty) { Pos = pos ?? ""; Name = name ?? ""; Qty = qty; }
    }

    /// <summary>Перечень элементов: «Поз. обозначение | Наименование | Кол-во» — как на схемах автоматизации.</summary>
    public sealed class PosList : Smart
    {
        public string Caption = "";
        public List<string[]> Rows = new List<string[]>();      // поз., наименование, кол-во
        public bool Auto = true;
        public double[] Widths = { 20, 85, 15 };
        public double TextH = 2.5;
        public override string Kind => "list";
        public override string Title => "Перечень элементов";

        /// <summary>Группировка по наименованию: позиции подряд (≥3, один префикс) — диапазоном «Y1–Y3», иначе через запятую.</summary>
        public static List<string[]> Compose(IEnumerable<PosItem> items)
        {
            var groups = items.Where(i => i.Name.Trim() != "" || i.Pos.Trim() != "")
                .GroupBy(i => i.Name.Trim())
                .Select(g => new { name = g.Key, pos = g.Select(i => i.Pos.Trim()).Where(p => p != "").Distinct().OrderBy(p => p, Natural.Comparer).ToList(), qty = g.Sum(i => i.Qty) })
                .OrderBy(g => g.pos.FirstOrDefault() ?? "￿", Natural.Comparer).ThenBy(g => g.name)
                .ToList();
            return groups.Select(g => new[] { Join(g.pos), g.name, Qty(g.qty) }).ToList();
        }

        static string Qty(double q) => q.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));

        public static string Join(List<string> pos)
        {
            if (pos.Count >= 3)
            {
                var ms = pos.Select(p => Regex.Match(p, @"^(.*?)(\d+)$")).ToList();
                if (ms.All(m => m.Success) && ms.Select(m => m.Groups[1].Value).Distinct().Count() == 1)
                {
                    var n = ms.Select(m => int.Parse(m.Groups[2].Value)).ToList();
                    if (n.Zip(n.Skip(1), (a, b) => b - a).All(d => d == 1)) return pos[0] + "–" + pos[pos.Count - 1];
                }
            }
            return string.Join(", ", pos);
        }

        public override void Build(Sketch s)
        {
            double H = TextH, x = 0;
            var xs = new List<double> { 0 }; foreach (var w in Widths) xs.Add(x += w);
            double W = xs[xs.Count - 1];
            P2 P(double a, double b) => new P2(s.U(a), s.U(b));
            // Начало координат — левый верхний угол. Шапка 15 мм.
            double y = 0;
            if (Caption != "") s.Text(P(0, 2), Caption, s.U(3.5), HAlign.Left, VAlign.Bottom);
            var heads = new[] { new[] { "Поз.", "обозначение" }, new[] { "Наименование" }, new[] { "Кол-во" } };
            for (int c = 0; c < 3; c++)
            {
                var hl = heads[c];
                for (int k = 0; k < hl.Length; k++) s.Text(P((xs[c] + xs[c + 1]) / 2, -7.5 + (hl.Length - 1) * 2.25 - k * 4.5), hl[k], s.U(H), HAlign.Center, VAlign.Middle);
            }
            y = -15;
            s.Line(P(0, y), P(W, y), Weight.Thick);
            foreach (var r in Rows)
            {
                var cells = new List<List<string>>();
                for (int c = 0; c < 3; c++) cells.Add(Wrap(c < r.Length ? r[c] : "", Widths[c] - 2, H));
                int n = cells.Max(l => l.Count);
                double rh = 8 + (n - 1) * 4.5;
                for (int c = 0; c < 3; c++)
                {
                    var l = cells[c];
                    double top = y - rh / 2 + (l.Count - 1) * 2.25;
                    for (int k = 0; k < l.Count; k++)
                    {
                        var ha = c == 1 ? HAlign.Left : HAlign.Center;
                        var tx = c == 1 ? xs[c] + 1 : (xs[c] + xs[c + 1]) / 2;
                        s.Text(P(tx, top - k * 4.5), l[k], s.U(H), ha, VAlign.Middle);
                    }
                }
                y -= rh;
                s.Line(P(0, y), P(W, y));
            }
            s.Poly(true, Weight.Thick, P(0, 0), P(W, 0), P(W, y), P(0, y));
            for (int c = 1; c < 3; c++) s.Line(P(xs[c], 0), P(xs[c], y), Weight.Thick);
        }

        static List<string> Wrap(string text, double width, double h)
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

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;

        public override Dictionary<string, string> Save()
        {
            var d = new Dictionary<string, string> { { "caption", Caption }, { "h", F(TextH) }, { "w", string.Join(";", Widths.Select(F)) }, { "n", Rows.Count.ToString(CultureInfo.InvariantCulture) }, { "auto", Auto ? "1" : "0" } };
            for (int i = 0; i < Rows.Count; i++) d["r" + i] = string.Join("¦", Rows[i].Select(x => (x ?? "").Replace("¦", "|")));
            return d;
        }

        public override void Load(IDictionary<string, string> d)
        {
            Caption = S(d, "caption"); TextH = D(d, "h", 2.5); Auto = B(d, "auto", true);
            var w = S(d, "w").Split(';').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
            if (w.Length == 3 && w.All(v => v > 0)) Widths = w;
            int n = (int)D(d, "n", 0);
            Rows = new List<string[]>();
            for (int i = 0; i < n; i++) Rows.Add(S(d, "r" + i).Split('¦'));
        }
    }

    /// <summary>Естественный порядок: TE2 < TE10, Y1 < Y4.</summary>
    public sealed class Natural : IComparer<string>
    {
        public static readonly Natural Comparer = new Natural();
        public int Compare(string a, string b)
        {
            var ra = Regex.Split(a ?? "", @"(\d+)"); var rb = Regex.Split(b ?? "", @"(\d+)");
            for (int i = 0; i < Math.Min(ra.Length, rb.Length); i++)
            {
                if (ra[i] == rb[i]) continue;
                if (long.TryParse(ra[i], out var x) && long.TryParse(rb[i], out var y)) return x.CompareTo(y);
                return string.Compare(ra[i], rb[i], StringComparison.OrdinalIgnoreCase);
            }
            return ra.Length.CompareTo(rb.Length);
        }
    }
}
