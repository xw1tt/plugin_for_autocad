// Библиотека условных обозначений: текстовый файл (встроенный + пользовательский), символ — умный объект.
// Формат файла (единицы — мм листа, начало координат — точка вставки):
//   [Система / Группа]                 — раздел дерева
//   = id | Наименование | префикс поз.  — новый символ (префикс — для автонумерации, например «ЦН»)
//   L x1 y1 x2 y2 [t]                   — отрезок (t — толстая линия)
//   P x y x y … [c] [t]                 — полилиния (c — замкнутая)
//   C x y r [t]   A x y r a0 a1 [t]     — окружность, дуга (углы в градусах, против часовой)
//   F x y x y x y …                     — заливка   D x y r — залитая точка
//   T x y h выравнивание "текст"        — текст: lb cb rb lm cm rm lt ct rt; {pos} — позиция
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Slt.Core
{
    public sealed class SymbolDef
    {
        public string Id, Name, Group, Prefix = "";
        public readonly List<Shape> Prims = new List<Shape>();
        public string Source = "";

        /// <summary>Рисует символ: масштаб k, поворот angle (рад); тексты остаются горизонтальными.</summary>
        public void Draw(Sketch s, double angle, IDictionary<string, string> vars, P2 at = default(P2))
        {
            double c = Math.Cos(angle), sn = Math.Sin(angle), k = s.K;
            P2 T(P2 p) => new P2(at.X + (p.X * c - p.Y * sn) * k, at.Y + (p.X * sn + p.Y * c) * k);
            foreach (var p in Prims)
            {
                var sh = new Shape { Kind = p.Kind, Closed = p.Closed, Weight = p.Weight, R = p.R * k, A0 = p.A0 + angle, A1 = p.A1 + angle, H = p.H * k, Ha = p.Ha, Va = p.Va, Pts = p.Pts.Select(T).ToArray() };
                if (p.Kind == ShapeKind.Text)
                {
                    var t = p.Text;
                    if (vars != null) foreach (var kv in vars) t = t.Replace("{" + kv.Key + "}", kv.Value ?? "");
                    if (t.Length == 0) continue;
                    sh.Text = t;
                }
                s.Shapes.Add(sh);
            }
        }

        /// <summary>Габарит в мм листа (для просмотра).</summary>
        public double[] Bounds()
        {
            var sk = new Sketch(1); Draw(sk, 0, null);
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var sh in sk.Shapes)
                foreach (var p in sh.Pts)
                {
                    var r = sh.Kind == ShapeKind.Circle || sh.Kind == ShapeKind.Arc ? sh.R : sh.Kind == ShapeKind.Text ? sh.H : 0;
                    x0 = Math.Min(x0, p.X - r); y0 = Math.Min(y0, p.Y - r); x1 = Math.Max(x1, p.X + r); y1 = Math.Max(y1, p.Y + r);
                }
            return x0 > x1 ? new double[] { -5, -5, 5, 5 } : new[] { x0, y0, x1, y1 };
        }
    }

    public sealed class Library
    {
        public readonly List<SymbolDef> Symbols = new List<SymbolDef>();
        public readonly List<string> Errors = new List<string>();
        public SymbolDef Get(string id) => Symbols.LastOrDefault(x => x.Id == id);

        static Library current;
        /// <summary>Действующая библиотека: встроенная + пользовательская (символы с тем же id заменяют встроенные).</summary>
        public static Library Current { get { return current ?? (current = Default()); } set { current = value; } }

        public static string DefaultText()
        {
            using (var s = typeof(Library).Assembly.GetManifestResourceStream("Slt.Core.library.txt"))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        public static Library Default() { var l = new Library(); l.Parse(DefaultText(), "встроенная"); return l; }

        public void Parse(string text, string source)
        {
            string group = "Прочее";
            SymbolDef cur = null;
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                var line = lines[n].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                try
                {
                    if (line.StartsWith("[")) { group = line.Trim('[', ']').Trim(); cur = null; continue; }
                    if (line.StartsWith("="))
                    {
                        var f = line.Substring(1).Split('|').Select(x => x.Trim()).ToArray();
                        cur = new SymbolDef { Id = f[0], Name = f.Length > 1 ? f[1] : f[0], Prefix = f.Length > 2 ? f[2] : "", Group = group, Source = source };
                        Symbols.RemoveAll(x => x.Id == cur.Id);
                        Symbols.Add(cur);
                        continue;
                    }
                    if (cur == null) throw new FormatException("команда вне символа");
                    cur.Prims.Add(Prim(line));
                }
                catch (Exception ex) { Errors.Add($"{source}, строка {n + 1}: {ex.Message} — «{line}»"); }
            }
        }

        static Shape Prim(string line)
        {
            string text = null;
            var q = line.IndexOf('"');
            if (q >= 0) { var q2 = line.LastIndexOf('"'); text = q2 > q ? line.Substring(q + 1, q2 - q - 1) : line.Substring(q + 1); line = line.Substring(0, q); }
            var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = tok[0].ToUpperInvariant();
            var nums = new List<double>(); var flags = new HashSet<string>();
            foreach (var t in tok.Skip(1))
            {
                if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) nums.Add(v);
                else flags.Add(t.ToLowerInvariant());
            }
            var w = flags.Contains("t") ? Weight.Thick : Weight.Thin;
            P2[] Pts(int from, int to) { var r = new List<P2>(); for (int i = from; i + 1 < to; i += 2) r.Add(new P2(nums[i], nums[i + 1])); return r.ToArray(); }
            void Need(int k) { if (nums.Count < k) throw new FormatException("мало чисел"); }
            const double Deg = Math.PI / 180;
            switch (cmd)
            {
                case "L": Need(4); return new Shape { Kind = ShapeKind.Line, Pts = Pts(0, 4), Weight = w };
                case "P": Need(4); return new Shape { Kind = ShapeKind.Polyline, Pts = Pts(0, nums.Count), Closed = flags.Contains("c"), Weight = w };
                case "C": Need(3); return new Shape { Kind = ShapeKind.Circle, Pts = Pts(0, 2), R = nums[2], Weight = w };
                case "A": Need(5); return new Shape { Kind = ShapeKind.Arc, Pts = Pts(0, 2), R = nums[2], A0 = nums[3] * Deg, A1 = nums[4] * Deg, Weight = w };
                case "F": Need(6); return new Shape { Kind = ShapeKind.Fill, Pts = Pts(0, nums.Count), Closed = true };
                case "D":
                    Need(3);
                    var c = new P2(nums[0], nums[1]); var pts = new P2[12];
                    for (int i = 0; i < 12; i++) pts[i] = c + new P2(Math.Cos(i * Math.PI / 6), Math.Sin(i * Math.PI / 6)) * nums[2];
                    return new Shape { Kind = ShapeKind.Fill, Pts = pts, Closed = true };
                case "T":
                    Need(3);
                    var al = flags.FirstOrDefault(x => x.Length == 2) ?? "cm";
                    var ha = al[0] == 'l' ? HAlign.Left : al[0] == 'r' ? HAlign.Right : HAlign.Center;
                    var va = al[1] == 'b' ? VAlign.Bottom : al[1] == 't' ? VAlign.Top : VAlign.Middle;
                    return new Shape { Kind = ShapeKind.Text, Pts = Pts(0, 2), H = nums[2], Ha = ha, Va = va, Text = text ?? "" };
            }
            throw new FormatException("неизвестная команда " + cmd);
        }
    }

    /// <summary>Символ библиотеки на чертеже: оборудование, арматура, извещатели…</summary>
    public sealed class Symbol : Smart
    {
        public string Sym = "vent.damper";
        public string Pos = "", Name = "", Note = "";
        public double Qty = 1;
        public double Angle;           // радианы
        public bool ShowPos = true;
        public P2 PosAt = new P2(0, 6); // единицы чертежа; по умолчанию — в мм, команда умножает на K
        public double TextH = 2.5;
        public override string Kind => "sym";
        public override string Title => Def?.Name ?? "Условное обозначение";
        public SymbolDef Def => Library.Current.Get(Sym);

        public override void Build(Sketch s)
        {
            var d = Def;
            if (d == null)
            {
                s.Poly(true, Weight.Thin, new P2(-s.U(3), -s.U(3)), new P2(s.U(3), -s.U(3)), new P2(s.U(3), s.U(3)), new P2(-s.U(3), s.U(3)));
                s.Text(new P2(0, 0), "?", s.U(2.5), HAlign.Center, VAlign.Middle);
            }
            else d.Draw(s, Angle, new Dictionary<string, string> { { "pos", Pos } });
            if (ShowPos && Pos != "" && !(d != null && d.Prims.Any(x => x.Kind == ShapeKind.Text && x.Text.Contains("{pos}")))) s.Text(PosAt, Pos, s.U(TextH), HAlign.Center, VAlign.Bottom);
        }

        /// <summary>Место позиции по умолчанию — над символом (в единицах чертежа).</summary>
        public P2 DefaultPosAt() { var d = Def; var b = d == null ? new double[] { -3, -3, 3, 3 } : d.Bounds(); return new P2(0, (b[3] + 1.5) * K); }

        public override IList<P2> Grips() => new[] { new P2(0, 0), PosAt };
        public override P2 MoveGrip(int i, P2 to)
        {
            if (i == 0) { PosAt = PosAt - to; return to; }
            PosAt = to; return new P2(0, 0);
        }

        public override Dictionary<string, string> Save() => new Dictionary<string, string>
        {
            { "sym", Sym }, { "pos", Pos }, { "name", Name }, { "note", Note }, { "qty", F(Qty) }, { "angle", F(Angle) }, { "showpos", ShowPos ? "1" : "0" },
            { "posat", Pts(new[] { PosAt }) }, { "h", F(TextH) },
        };

        public override void Load(IDictionary<string, string> d)
        {
            Sym = S(d, "sym", Sym); Pos = S(d, "pos"); Name = S(d, "name"); Note = S(d, "note"); Qty = D(d, "qty", 1); Angle = D(d, "angle", 0);
            ShowPos = B(d, "showpos", true); TextH = D(d, "h", 2.5);
            var p = Pts(S(d, "posat")); if (p.Count > 0) PosAt = p[0];
        }
    }
}
