// Линии связи и кабели: вид линии (как в условных обозначениях проекта), обозначение «К20», марка, начало/конец, длина.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    /// <summary>Вид кабельной линии (способ прокладки): линия + знак, повторяемый с шагом вдоль трассы.</summary>
    public sealed class LineKind
    {
        public string Id, Name;
        public int Color;          // ACI, 0 — по слою/блоку
        public Weight Weight = Weight.Thick;
        public double[] Dash = new double[0]; // мм листа: штрих, пробел, штрих…
        public double Step;        // шаг знака, мм листа; 0 — без знаков
        public readonly List<Shape> Mark = new List<Shape>(); // мм листа: x — вдоль линии, y — поперёк (влево)
        public string Source = "";
    }

    public static class LineKinds
    {
        public static List<LineKind> All => Current;
        static List<LineKind> current;
        public static List<LineKind> Current { get { return current ?? (current = Parse(DefaultText(), "встроенные", new List<string>())); } set { current = value; } }
        public static LineKind Get(string id) => All.FirstOrDefault(k => k.Id == id) ?? All[0];

        public static string DefaultText()
        {
            using (var st = typeof(LineKinds).Assembly.GetManifestResourceStream("Slt.Core.lines.txt"))
            using (var r = new System.IO.StreamReader(st, System.Text.Encoding.UTF8)) return r.ReadToEnd();
        }

        /// <summary>Формат: «= id | Наименование | цвет | t (толстая) | штрихи | шаг знака», ниже — знак командами библиотеки (L, P, C, A, F, D, T).</summary>
        public static List<LineKind> Parse(string text, string source, List<string> errors, List<LineKind> into = null)
        {
            var list = into ?? new List<LineKind>();
            LineKind cur = null;
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                var line = lines[n].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                try
                {
                    if (line.StartsWith("="))
                    {
                        var f = line.Substring(1).Split('|').Select(x => x.Trim()).ToArray();
                        double Num(int i, double def) => f.Length > i && double.TryParse(f[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
                        cur = new LineKind
                        {
                            Id = f[0], Name = f.Length > 1 ? f[1] : f[0], Color = (int)Num(2, 0),
                            Weight = f.Length > 3 && f[3].ToLowerInvariant().StartsWith("t") ? Weight.Thick : Weight.Thin,
                            Dash = f.Length > 4 ? f[4].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x.Replace(',', '.'), CultureInfo.InvariantCulture)).Where(x => x > 0).ToArray() : new double[0],
                            Step = Num(5, 0), Source = source,
                        };
                        if (cur.Dash.Length % 2 == 1) cur.Dash = cur.Dash.Concat(new[] { cur.Dash[cur.Dash.Length - 1] }).ToArray();
                        int at = list.FindIndex(x => x.Id == cur.Id);
                        if (at >= 0) list[at] = cur; else list.Add(cur);
                        continue;
                    }
                    if (cur == null) throw new FormatException("команда вне вида линии");
                    cur.Mark.Add(Library.Prim(line));
                }
                catch (Exception ex) { errors.Add($"{source}, строка {n + 1}: {ex.Message} — «{line}»"); }
            }
            return list;
        }

        /// <summary>Обратно в текст (для редактора видов линий): знаки сохраняются как были.</summary>
        public static string Header(LineKind k) =>
            "= " + k.Id + " | " + k.Name + " | " + k.Color + " | " + (k.Weight == Weight.Thick ? "t" : "") + " | " +
            string.Join(" ", k.Dash.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture))) + " | " + k.Step.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Места знаков вдоль ломаной: первый — на половине шага от начала, дальше — через шаг; не ближе clearance к вершине.
        /// Короткий участок, на который не пришлось ни одного знака, получает знак посередине.
        /// </summary>
        public static List<double[]> Places(IList<P2> path, double step, double clearance)
        {
            var res = new List<double[]>();
            if (path == null || path.Count < 2 || step <= 0) return res;
            double next = step / 2, walked = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1]; var b = path[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                var ang = Math.Atan2(dy, dx);
                int placed = 0;
                while (next <= walked + len)
                {
                    var t = next - walked;
                    if (t < clearance) { next = walked + clearance; continue; }
                    if (t > len - clearance) break;
                    res.Add(new[] { a.X + dx * t / len, a.Y + dy * t / len, ang });
                    placed++;
                    next += step;
                }
                if (placed == 0 && len > 2 * clearance) { res.Add(new[] { a.X + dx / 2, a.Y + dy / 2, ang }); next = walked + len / 2 + step; }
                walked += len;
                if (next < walked) next = walked;
            }
            return res;
        }

        /// <summary>Знак вида линии в точке (x, y) под углом ang; k — единиц чертежа в 1 мм листа.</summary>
        public static void DrawMark(Sketch s, LineKind kind, double x, double y, double ang)
        {
            double c = Math.Cos(ang), sn = Math.Sin(ang), k = s.K;
            P2 T(P2 p) => new P2(x + (p.X * c - p.Y * sn) * k, y + (p.X * sn + p.Y * c) * k);
            foreach (var p in kind.Mark)
                s.Shapes.Add(new Shape { Kind = p.Kind, Closed = p.Closed, Weight = p.Weight, R = p.R * k, A0 = p.A0 + ang, A1 = p.A1 + ang, H = p.H * k, Ha = p.Ha, Va = p.Va, Text = p.Text, Rotation = p.Rotation, Pts = p.Pts.Select(T).ToArray(), Color = kind.Color });
        }
    }

    public sealed class Cable : Smart
    {
        public List<P2> Path = new List<P2> { new P2(0, 0), new P2(50, 0) }; // единицы чертежа, первая — начало координат
        public string Mark = "К1", Type = "", Start = "", To = "", Note = "";
        public string Line = "open";
        public bool ShowMark = true, ShowType;
        public double Reserve = 0;       // запас, %
        public double Meters;            // длина вручную; 0 — по чертежу
        public double TextH = 2.5;
        public override string Kind => "cable";
        public override string Title => "Кабель (линия связи)";

        public double Geometry() { double l = 0; for (int i = 1; i < Path.Count; i++) l += (Path[i] - Path[i - 1]).Length; return l; }

        /// <summary>Длина, м: вручную или по чертежу (единиц в 1 мм натуры) с запасом, округлённая вверх до метра.</summary>
        public double Length(double unitsPerMm)
        {
            if (Meters > 0) return Meters;
            if (unitsPerMm <= 0) return 0;
            return Math.Ceiling(Geometry() / unitsPerMm / 1000.0 * (1 + Reserve / 100.0) - 1e-9);
        }

        public string Label => (ShowMark ? Mark : "") + (ShowType && Type != "" ? (ShowMark && Mark != "" ? " " : "") + Type : "");

        public override void Build(Sketch s)
        {
            var k = LineKinds.Get(Line);
            int from = s.Shapes.Count;
            if (k.Dash.Length == 0) s.Poly(false, k.Weight, Path.ToArray());
            else Dashed(s, k);
            for (int i = from; i < s.Shapes.Count; i++) s.Shapes[i].Color = k.Color;
            if (k.Step > 0 && k.Mark.Count > 0)
                foreach (var pl in LineKinds.Places(Path, s.U(k.Step), s.U(4))) LineKinds.DrawMark(s, k, pl[0], pl[1], pl[2]);
            // Обозначение — над самым длинным участком, вдоль него, читаемо.
            var label = Label;
            if (label != "" && Path.Count >= 2)
            {
                int best = 1; double bl = 0;
                for (int i = 1; i < Path.Count; i++) { var l = (Path[i] - Path[i - 1]).Length; if (l > bl) { bl = l; best = i; } }
                var a = Path[best - 1]; var b = Path[best]; var d = (b - a).Unit;
                var ang = Math.Atan2(d.Y, d.X);
                if (ang > Math.PI / 2 + 1e-9) { ang -= Math.PI; d = d * -1; }
                else if (ang <= -Math.PI / 2 + 1e-9) { ang += Math.PI; d = d * -1; }
                var n = new P2(-d.Y, d.X);
                double lift = 1;
                if (k.Step > 0) foreach (var m in k.Mark) foreach (var q in m.Pts) lift = Math.Max(lift, q.Y + (m.Kind == ShapeKind.Circle || m.Kind == ShapeKind.Arc ? m.R : 0) + 0.8);
                var mid = (a + b) * 0.5 + n * s.U(lift);
                s.Text(mid, label, s.U(TextH), HAlign.Center, VAlign.Bottom, ang);
            }
        }

        void Dashed(Sketch s, LineKind k)
        {
            var pat = k.Dash.Select(s.U).ToArray();
            int pi = 0; double left = pat[0]; bool on = true;
            for (int i = 1; i < Path.Count; i++)
            {
                var a = Path[i - 1]; var b = Path[i]; var len = (b - a).Length; var u = (b - a).Unit; double t = 0;
                while (t < len - 1e-9)
                {
                    var step = Math.Min(left, len - t);
                    if (on) s.Line(a + u * t, a + u * (t + step), k.Weight);
                    t += step; left -= step;
                    if (left <= 1e-9) { pi = (pi + 1) % pat.Length; left = pat[pi]; on = pi % 2 == 0; }
                }
            }
        }

        public override IList<P2> Grips() => Path.ToList();
        public override P2 MoveGrip(int i, P2 to)
        {
            if (i == 0) { for (int k = 1; k < Path.Count; k++) Path[k] = Path[k] - to; return to; }
            if (i < Path.Count) Path[i] = to;
            return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "pts", Pts(Path) }, { "mark", Mark }, { "type", Type }, { "from", Start }, { "to", To }, { "note", Note }, { "line", Line },
            { "showmark", ShowMark ? "1" : "0" }, { "showtype", ShowType ? "1" : "0" }, { "reserve", F(Reserve) }, { "m", F(Meters) }, { "h", F(TextH) },
        };

        public override void Load(IDictionary<string, string> d)
        {
            var p = Pts(S(d, "pts")); if (p.Count >= 2) Path = p;
            Mark = S(d, "mark"); Type = S(d, "type"); Start = S(d, "from"); To = S(d, "to"); Note = S(d, "note"); Line = S(d, "line", "open");
            ShowMark = B(d, "showmark", true); ShowType = B(d, "showtype", false); Reserve = D(d, "reserve", 0); Meters = D(d, "m", 0); TextH = D(d, "h", 2.5);
        }
    }

    /// <summary>Условные обозначения линий связи — таблица-легенда (как на структурных схемах).</summary>
    public sealed class LineLegend : Smart
    {
        public string Kinds = "";
        public double TextH = 2.5;
        public override string Kind => "legend";
        public override string Title => "Условные обозначения кабельных линий";

        public override void Build(Sketch s)
        {
            double y = 0;
            s.Text(new P2(0, 0), "Условные обозначения кабельных линий:", s.U(3.5), HAlign.Left, VAlign.Bottom);
            var ids = Kinds.Trim() == "" ? LineKinds.All.Select(x => x.Id).ToList() : Kinds.Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
            foreach (var id in ids)
            {
                y -= 7;
                var k = LineKinds.Get(id);
                var c = new Cable { K = s.K, Line = k.Id, ShowMark = false, Path = new List<P2> { new P2(0, s.U(y)), new P2(s.U(Math.Max(25, k.Step * 1.0 + 10)), s.U(y)) } };
                c.Build(s);
                s.Text(new P2(s.U(Math.Max(25, k.Step + 10) + 5), s.U(y)), k.Name, s.U(TextH), HAlign.Left, VAlign.Middle);
            }
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;
        public override Dictionary<string, string> Save() => new Dictionary<string, string> { { "kinds", Kinds }, { "h", F(TextH) } };
        public override void Load(IDictionary<string, string> d) { Kinds = S(d, "kinds", Kinds); TextH = D(d, "h", 2.5); }
    }
}
