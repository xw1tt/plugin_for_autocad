// Объекты оформления по ГОСТ 21.101-2020 (с отсылками к ГОСТ 2.301, 2.303, 2.307): выноска, позиционная выноска,
// отметка уровня (разрез, план), координационная ось, массив осей, формат со штампом. Каждый объект — параметры +
// построение в локальных координатах (начало — точка вставки) + ручки. Размеры знаков — в мм листа × масштаб.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    /// <summary>Умный объект: параметры, построение, ручки. Начало локальных координат — точка вставки блока.</summary>
    public abstract class Smart
    {
        public abstract string Kind { get; }
        public abstract string Title { get; }
        /// <summary>Единиц чертежа в 1 мм листа (масштаб оформления).</summary>
        public double K = 1;
        public abstract void Build(Sketch s);
        public List<Shape> Shapes() { var s = new Sketch(K); Build(s); return s.Shapes; }
        /// <summary>Ручки в локальных координатах.</summary>
        public virtual IList<P2> Grips() => new P2[0];
        /// <summary>Перемещение ручки i в точку to (локально). Возвращает сдвиг начала координат (точки вставки) — если ручка задаёт его.</summary>
        public virtual P2 MoveGrip(int i, P2 to) => new P2(0, 0);
        public abstract Dictionary<string, string> Save();
        public abstract void Load(IDictionary<string, string> d);

        // ---- сериализация ----
        protected static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        protected static double D(IDictionary<string, string> d, string k, double def) => d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : def;
        protected static string S(IDictionary<string, string> d, string k, string def = "") => d.TryGetValue(k, out var v) ? v : def;
        protected static bool B(IDictionary<string, string> d, string k, bool def) => d.TryGetValue(k, out var v) ? v == "1" : def;
        protected static string Pts(IEnumerable<P2> p) => string.Join("|", p.Select(x => F(x.X) + ";" + F(x.Y)));
        protected static List<P2> Pts(string s) => (s ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(';')).Where(a => a.Length == 2).Select(a => new P2(double.Parse(a[0], CultureInfo.InvariantCulture), double.Parse(a[1], CultureInfo.InvariantCulture))).ToList();

        public static Smart Create(string kind)
        {
            switch (kind)
            {
                case "leader": return new Leader();
                case "level": return new LevelMark();
                case "levelplan": return new PlanLevel();
                case "axis": return new Axis();
                case "axes": return new AxisGrid();
                case "format": return new SheetFormat();
                case "kip": return new Instrument();
                case "sym": return new Symbol();
                case "list": return new PosList();
                default: throw new ArgumentException("Неизвестный объект: " + kind);
            }
        }

        public static Smart From(IDictionary<string, string> d)
        {
            var o = Create(S(d, "kind"));
            o.K = D(d, "k", 1);
            o.Load(d);
            return o;
        }

        public Dictionary<string, string> SaveAll()
        {
            var d = Save();
            d["kind"] = Kind;
            d["k"] = F(K);
            return d;
        }
    }

    public enum LeaderEnd { Arrow, Dot, None }

    /// <summary>
    /// Линия-выноска с полкой (ГОСТ 21.101 п.5.4, ГОСТ 2.316): выноска заканчивается стрелкой на линии контура, точкой внутри
    /// контура, без знака — на линии. Несколько выносок от одной полки. Позиционная выноска — номер позиции на полке крупнее.
    /// Начало координат — излом (начало полки); полка уходит в сторону от выносок.
    /// </summary>
    public sealed class Leader : Smart
    {
        public List<P2> Targets = new List<P2> { new P2(-10, -10) };
        public string Top = "", Bottom = "";
        public double TextH = 2.5;
        public LeaderEnd End = LeaderEnd.Dot;
        public bool Position;
        public override string Kind => "leader";
        public override string Title => Position ? "Позиционная выноска" : "Выноска";

        /// <summary>Полка вправо, если выноски слева от излома.</summary>
        public bool ShelfRight => Targets.Count == 0 || Targets.Average(t => t.X) <= 0;

        public double ShelfLength
        {
            get
            {
                var w = Math.Max(TextMeasure.Width(Top, TextH * K), TextMeasure.Width(Bottom, TextH * 0.8 * K));
                return Math.Max(w + 2 * 1.0 * K, 5 * K);
            }
        }

        public override void Build(Sketch s)
        {
            var o = new P2(0, 0);
            foreach (var t in Targets)
            {
                s.Line(o, t);
                var dir = (t - o).Unit;
                if (End == LeaderEnd.Arrow)
                {
                    // Стрелка как размерная: длина 2,5 мм, угол ~20°.
                    var back = t - dir * s.U(2.5); var n = new P2(-dir.Y, dir.X) * s.U(0.45);
                    s.Fill(t, back + n, back - n);
                }
                else if (End == LeaderEnd.Dot)
                {
                    var r = s.U(0.5);
                    s.Fill(Enumerable.Range(0, 12).Select(i => t + new P2(Math.Cos(i * Math.PI / 6), Math.Sin(i * Math.PI / 6)) * r).ToArray());
                }
            }
            var len = ShelfLength * (ShelfRight ? 1 : -1);
            s.Line(o, new P2(len, 0));
            var x0 = ShelfRight ? s.U(1) : len + s.U(1);
            s.Text(new P2(x0, s.U(1)), Top, s.U(TextH));
            s.Text(new P2(x0, -s.U(1)), Bottom, s.U(TextH * 0.8), HAlign.Left, VAlign.Top);
        }

        // Ручки: 0 — излом (точка вставки), 1… — концы выносок, последняя — конец полки (перетаскивание добавляет выноску? нет — только показ).
        public override IList<P2> Grips() => new[] { new P2(0, 0) }.Concat(Targets).ToList();

        public override P2 MoveGrip(int i, P2 to)
        {
            if (i == 0)
            {
                // Излом двигается, концы выносок остаются на месте: сдвиг начала координат.
                for (int k = 0; k < Targets.Count; k++) Targets[k] = Targets[k] - to;
                return to;
            }
            Targets[i - 1] = to;
            return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "targets", Pts(Targets) }, { "top", Top }, { "bottom", Bottom }, { "h", F(TextH) }, { "end", End.ToString() }, { "pos", Position ? "1" : "0" },
        };

        public override void Load(IDictionary<string, string> d)
        {
            Targets = Pts(S(d, "targets"));
            Top = S(d, "top"); Bottom = S(d, "bottom"); TextH = D(d, "h", 2.5);
            End = Enum.TryParse(S(d, "end", "Dot"), out LeaderEnd e) ? e : LeaderEnd.Dot;
            Position = B(d, "pos", false);
        }
    }

    /// <summary>
    /// Отметка уровня на разрезе, фасаде, схеме (ГОСТ 21.101 п.5.5, ГОСТ 2.307): знак — стрелка 90° длиной сторон 2…4 мм
    /// остриём на линии уровня, полка, над полкой — значение «+3,500». Ассоциативная — значение по положению относительно
    /// базовой отметки чертежа (пересчитывается при переносе). Начало координат — остриё.
    /// </summary>
    public sealed class LevelMark : Smart
    {
        public double Value;
        public bool Associative = true;
        /// <summary>Своё значение вместо вычисленного (например, «+3,500 (низ лотка)»).</summary>
        public string Override = "";
        public string Note = "";
        public bool Up = true, Right = true;
        public double TextH = 2.5;
        public override string Kind => "level";
        public override string Title => "Отметка уровня";
        public string Text => Override != "" ? Override : Levels.Format(Value);

        public override void Build(Sketch s)
        {
            double a = s.U(2), sy = Up ? 1 : -1, dir = Right ? 1 : -1;
            var tip = new P2(0, 0);
            s.Line(tip, new P2(-a, a * sy), Weight.Thick);
            s.Line(tip, new P2(a, a * sy), Weight.Thick);
            var w = Math.Max(TextMeasure.Width(Text, s.U(TextH)), TextMeasure.Width(Note, s.U(TextH * 0.8)));
            var shelfEnd = dir * (a + s.U(0.8) + w);
            s.Line(new P2(-a * dir, a * sy), new P2(shelfEnd, a * sy));
            var tx = Right ? a + s.U(0.4) : shelfEnd;
            s.Text(new P2(tx, a * sy + s.U(0.8) * sy), Text, s.U(TextH), HAlign.Left, Up ? VAlign.Bottom : VAlign.Top);
            s.Text(new P2(tx, a * sy - s.U(0.8) * sy), Note, s.U(TextH * 0.8), HAlign.Left, Up ? VAlign.Top : VAlign.Bottom);
        }

        // Ручки: 0 — остриё (точка вставки), 1 — конец полки: перетаскивание влево/вниз меняет сторону.
        public override IList<P2> Grips()
        {
            var a = 2 * K;
            return new[] { new P2(0, 0), new P2((Right ? 1 : -1) * (a + 0.8 * K + TextMeasure.Width(Text, TextH * K)), (Up ? 1 : -1) * a) };
        }

        public override P2 MoveGrip(int i, P2 to)
        {
            if (i == 0) return to;
            Right = to.X >= 0; Up = to.Y >= 0;
            return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "value", F(Value) }, { "assoc", Associative ? "1" : "0" }, { "override", Override }, { "note", Note }, { "up", Up ? "1" : "0" }, { "right", Right ? "1" : "0" }, { "h", F(TextH) },
        };

        public override void Load(IDictionary<string, string> d)
        {
            Value = D(d, "value", 0); Associative = B(d, "assoc", true); Override = S(d, "override"); Note = S(d, "note");
            Up = B(d, "up", true); Right = B(d, "right", true); TextH = D(d, "h", 2.5);
        }
    }

    /// <summary>Отметка уровня на плане (ГОСТ 21.101 п.5.5.3): значение в прямоугольной рамке. Начало координат — центр.</summary>
    public sealed class PlanLevel : Smart
    {
        public double Value;
        public string Override = "";
        public double TextH = 2.5;
        public override string Kind => "levelplan";
        public override string Title => "Отметка на плане";
        public string Text => Override != "" ? Override : Levels.Format(Value);

        public override void Build(Sketch s)
        {
            var w = TextMeasure.Width(Text, s.U(TextH)) / 2 + s.U(0.8);
            var h = s.U(TextH) / 2 + s.U(0.8);
            s.Poly(true, Weight.Thin, new P2(-w, -h), new P2(w, -h), new P2(w, h), new P2(-w, h));
            s.Text(new P2(0, 0), Text, s.U(TextH), HAlign.Center, VAlign.Middle);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string> { { "value", F(Value) }, { "override", Override }, { "h", F(TextH) } };
        public override void Load(IDictionary<string, string> d) { Value = D(d, "value", 0); Override = S(d, "override"); TextH = D(d, "h", 2.5); }
    }

    /// <summary>Буквы и цифры координационных осей (ГОСТ 21.101 п.5.6): буквы без Ё, З, Й, О, Х, Ц, Ч, Щ, Ъ, Ы, Ь.</summary>
    public static class AxisNames
    {
        const string Letters = "АБВГДЕЖИКЛМНПРСТУФШЭЮЯ";

        public static string Next(string name, int step = 1)
        {
            if (int.TryParse(name, out var n)) return (n + step).ToString(CultureInfo.InvariantCulture);
            // Буква с цифрой («А1») — меняется число; одиночная буква — по ряду, за Я — «АА», «АБ»…
            var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^(.*?)(\d+)$");
            if (m.Success) return m.Groups[1].Value + (int.Parse(m.Groups[2].Value) + step);
            var seq = Sequence(name, step + 1);
            return seq[step];
        }

        public static List<string> Sequence(string first, int count)
        {
            var res = new List<string>();
            if (int.TryParse(first, out var n)) { for (int i = 0; i < count; i++) res.Add((n + i).ToString(CultureInfo.InvariantCulture)); return res; }
            var f = (first ?? "А").ToUpperInvariant();
            int idx = Index(f);
            if (idx < 0) { for (int i = 0; i < count; i++) res.Add(i == 0 ? f : f + i.ToString(CultureInfo.InvariantCulture)); return res; }
            for (int i = 0; i < count; i++) res.Add(Name(idx + i));
            return res;
        }

        static int Index(string s)
        {
            if (s.Length == 1) return Letters.IndexOf(s[0]);
            if (s.Length == 2 && Letters.IndexOf(s[0]) >= 0 && Letters.IndexOf(s[1]) >= 0) return (Letters.IndexOf(s[0]) + 1) * Letters.Length + Letters.IndexOf(s[1]);
            return -1;
        }

        static string Name(int i) => i < Letters.Length ? Letters[i].ToString() : Letters[i / Letters.Length - 1].ToString() + Letters[i % Letters.Length];

        /// <summary>Шаги «6000*4, 3000» → 6000, 6000, 6000, 6000, 3000.</summary>
        public static List<double> Spans(string text)
        {
            var res = new List<double>();
            foreach (var part in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Split('*', 'x', 'х', '×');
                var v = double.Parse(p[0].Replace(',', '.'), CultureInfo.InvariantCulture);
                var n = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 1;
                for (int i = 0; i < n; i++) res.Add(v);
            }
            return res;
        }
    }

    static class Dash
    {
        /// <summary>Штрихпунктирная тонкая (ГОСТ 2.303): штрих 12, промежуток 1,5, точка 0,5 — явными отрезками (не зависит от LTSCALE).</summary>
        public static void DashDot(Sketch s, P2 a, P2 b)
        {
            var len = (b - a).Length; var u = (b - a).Unit;
            double dash = s.U(12), gap = s.U(1.5), dot = s.U(0.5), t = 0;
            // Штрих начинается и кончается штрихом — оси читаются с обоих концов.
            while (t < len)
            {
                var e = Math.Min(len, t + dash);
                s.Line(a + u * t, a + u * e);
                t = e + gap;
                if (t >= len) break;
                var d = Math.Min(len, t + dot);
                s.Line(a + u * t, a + u * d);
                t = d + gap;
            }
        }
    }

    /// <summary>Координационная ось (ГОСТ 21.101 п.5.6): штрихпунктирная линия, марка в кружке Ø6…12 мм. Начало — начало оси.</summary>
    public sealed class Axis : Smart
    {
        public P2 End = new P2(0, 50);
        public string Name = "1";
        public double Diameter = 8;
        public bool BubbleAtStart;
        public double TextH = 3.5;
        public override string Kind => "axis";
        public override string Title => "Координационная ось";

        public override void Build(Sketch s)
        {
            Dash.DashDot(s, new P2(0, 0), End);
            var u = End.Unit; var r = s.U(Diameter / 2);
            void Bubble(P2 at, P2 dir) { var c = at + dir * r; s.Circle(c, r); s.Text(c, Name, s.U(TextH), HAlign.Center, VAlign.Middle); }
            Bubble(End, u);
            if (BubbleAtStart) Bubble(new P2(0, 0), u * -1);
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0), End };
        public override P2 MoveGrip(int i, P2 to)
        {
            if (i == 0) { End = End - to; return to; }
            End = to; return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "end", Pts(new[] { End }) }, { "name", Name }, { "d", F(Diameter) }, { "both", BubbleAtStart ? "1" : "0" }, { "h", F(TextH) },
        };
        public override void Load(IDictionary<string, string> d)
        {
            var e = Pts(S(d, "end")); End = e.Count > 0 ? e[0] : new P2(0, 50);
            Name = S(d, "name", "1"); Diameter = D(d, "d", 8); BubbleAtStart = B(d, "both", false); TextH = D(d, "h", 3.5);
        }
    }

    /// <summary>
    /// Массив осей: цифровые по X (слева направо), буквенные по Y (снизу вверх), шаги в единицах чертежа («6000*4, 3000»),
    /// кружки и размерные цепочки (шаги и общий) с выбранных сторон. Начало — пересечение первых осей.
    /// </summary>
    public sealed class AxisGrid : Smart
    {
        public string XSpans = "6000*4", YSpans = "6000*3";
        public string XFirst = "1", YFirst = "А";
        public bool Bottom = true, Left = true, Top, Right;
        public bool Dimensions = true;
        /// <summary>Выход осей за крайние, мм листа (до кружка).</summary>
        public double Overshoot = 20;
        public double Diameter = 8;
        public double TextH = 3.5;
        public override string Kind => "axes";
        public override string Title => "Массив осей";

        public List<double> Xs() { var l = new List<double> { 0 }; foreach (var v in AxisNames.Spans(XSpans)) l.Add(l[l.Count - 1] + v); return l; }
        public List<double> Ys() { var l = new List<double> { 0 }; foreach (var v in AxisNames.Spans(YSpans)) l.Add(l[l.Count - 1] + v); return l; }

        public override void Build(Sketch s)
        {
            var xs = Xs(); var ys = Ys();
            double x0 = xs.First(), x1 = xs.Last(), y0 = ys.First(), y1 = ys.Last();
            double ov = s.U(Overshoot), r = s.U(Diameter / 2);
            var xn = AxisNames.Sequence(XFirst, xs.Count); var yn = AxisNames.Sequence(YFirst, ys.Count);
            for (int i = 0; i < xs.Count; i++)
            {
                var x = xs[i];
                Dash.DashDot(s, new P2(x, Bottom ? y0 - ov : y0 - s.U(5)), new P2(x, Top ? y1 + ov : y1 + s.U(5)));
                if (Bottom) { s.Circle(new P2(x, y0 - ov - r), r); s.Text(new P2(x, y0 - ov - r), xn[i], s.U(TextH), HAlign.Center, VAlign.Middle); }
                if (Top) { s.Circle(new P2(x, y1 + ov + r), r); s.Text(new P2(x, y1 + ov + r), xn[i], s.U(TextH), HAlign.Center, VAlign.Middle); }
            }
            for (int i = 0; i < ys.Count; i++)
            {
                var y = ys[i];
                Dash.DashDot(s, new P2(Left ? x0 - ov : x0 - s.U(5), y), new P2(Right ? x1 + ov : x1 + s.U(5), y));
                if (Left) { s.Circle(new P2(x0 - ov - r, y), r); s.Text(new P2(x0 - ov - r, y), yn[i], s.U(TextH), HAlign.Center, VAlign.Middle); }
                if (Right) { s.Circle(new P2(x1 + ov + r, y), r); s.Text(new P2(x1 + ov + r, y), yn[i], s.U(TextH), HAlign.Center, VAlign.Middle); }
            }
            if (!Dimensions) return;
            // Цепочки: шаги — ближе к осям, общий размер — дальше; засечки 45° (ГОСТ 2.307 для строительных чертежей).
            if (Bottom || Top)
            {
                var yb = Bottom ? y0 : y1; var sg = Bottom ? -1 : 1;
                Chain(s, xs.Select(x => new P2(x, yb + sg * s.U(Overshoot * 0.35))).ToList(), true);
                if (xs.Count > 2) Chain(s, new List<P2> { new P2(x0, yb + sg * s.U(Overshoot * 0.7)), new P2(x1, yb + sg * s.U(Overshoot * 0.7)) }, true);
            }
            if (Left || Right)
            {
                var xb = Left ? x0 : x1; var sg = Left ? -1 : 1;
                Chain(s, ys.Select(y => new P2(xb + sg * s.U(Overshoot * 0.35), y)).ToList(), false);
                if (ys.Count > 2) Chain(s, new List<P2> { new P2(xb + sg * s.U(Overshoot * 0.7), y0), new P2(xb + sg * s.U(Overshoot * 0.7), y1) }, false);
            }
        }

        void Chain(Sketch s, List<P2> pts, bool horizontal)
        {
            var t = s.U(1.5);
            s.Line(pts.First() - (pts.Last() - pts.First()).Unit * s.U(2), pts.Last() + (pts.Last() - pts.First()).Unit * s.U(2));
            foreach (var p in pts) s.Line(p + new P2(-t, -t), p + new P2(t, t), Weight.Thick);
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                var v = horizontal ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y);
                var txt = Math.Round(v).ToString(CultureInfo.InvariantCulture);
                var m = (a + b) * 0.5;
                if (horizontal) s.Text(m + new P2(0, s.U(0.8)), txt, s.U(2.5), HAlign.Center, VAlign.Bottom);
                else s.Text(m + new P2(-s.U(0.8), 0), txt, s.U(2.5), HAlign.Center, VAlign.Bottom, Math.PI / 2);
            }
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "xs", XSpans }, { "ys", YSpans }, { "x1", XFirst }, { "y1", YFirst }, { "b", Bottom ? "1" : "0" }, { "l", Left ? "1" : "0" }, { "t", Top ? "1" : "0" }, { "r", Right ? "1" : "0" },
            { "dims", Dimensions ? "1" : "0" }, { "ov", F(Overshoot) }, { "d", F(Diameter) }, { "h", F(TextH) },
        };
        public override void Load(IDictionary<string, string> d)
        {
            XSpans = S(d, "xs", "6000*4"); YSpans = S(d, "ys", "6000*3"); XFirst = S(d, "x1", "1"); YFirst = S(d, "y1", "А");
            Bottom = B(d, "b", true); Left = B(d, "l", true); Top = B(d, "t", false); Right = B(d, "r", false);
            Dimensions = B(d, "dims", true); Overshoot = D(d, "ov", 20); Diameter = D(d, "d", 8); TextH = D(d, "h", 3.5);
        }
    }
}
