// Линии связи и кабели: вид линии (как в условных обозначениях проекта), обозначение «К20», марка, начало/конец, длина.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    public sealed class LineKind
    {
        public string Id, Name;
        public int Color;          // ACI, 0 — по слою/блоку
        public Weight Weight;
        public double[] Dash = new double[0]; // мм листа: штрих, пробел, штрих…
    }

    public static class LineKinds
    {
        /// <summary>По условным обозначениям линий связи проекта СУДИО (лист 4) + силовые и контрольные.</summary>
        public static readonly List<LineKind> All = new List<LineKind>
        {
            new LineKind { Id = "an", Name = "Аналоговые и дискретные линии связи", Weight = Weight.Thick },
            new LineKind { Id = "rs485", Name = "Цифровые линии связи RS485", Color = 8 },
            new LineKind { Id = "eth", Name = "Цифровые линии связи Ethernet", Color = 1, Weight = Weight.Thick },
            new LineKind { Id = "ecat", Name = "Цифровая линия связи EtherCAT", Color = 9 },
            new LineKind { Id = "fo", Name = "ВОЛС", Color = 4, Weight = Weight.Thick },
            new LineKind { Id = "knx", Name = "Шина KNX", Color = 3 },
            new LineKind { Id = "pwr", Name = "Линия электропитания", Weight = Weight.Thick, Dash = new double[] { 10, 2 } },
            new LineKind { Id = "dash", Name = "Штриховая (проектируемая другим разделом)", Dash = new double[] { 6, 2 } },
        };
        public static LineKind Get(string id) => All.FirstOrDefault(k => k.Id == id) ?? All[0];
    }

    public sealed class Cable : Smart
    {
        public List<P2> Path = new List<P2> { new P2(0, 0), new P2(50, 0) }; // единицы чертежа, первая — начало координат
        public string Mark = "К1", Type = "", Start = "", To = "", Note = "";
        public string Line = "an";
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
                var mid = (a + b) * 0.5 + n * s.U(1);
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
            Mark = S(d, "mark"); Type = S(d, "type"); Start = S(d, "from"); To = S(d, "to"); Note = S(d, "note"); Line = S(d, "line", "an");
            ShowMark = B(d, "showmark", true); ShowType = B(d, "showtype", false); Reserve = D(d, "reserve", 0); Meters = D(d, "m", 0); TextH = D(d, "h", 2.5);
        }
    }

    /// <summary>Условные обозначения линий связи — таблица-легенда (как на структурных схемах).</summary>
    public sealed class LineLegend : Smart
    {
        public string Kinds = "an,rs485,eth,ecat,fo";
        public double TextH = 2.5;
        public override string Kind => "legend";
        public override string Title => "Условные обозначения линий связи";

        public override void Build(Sketch s)
        {
            double y = 0;
            s.Text(new P2(0, 0), "Условные обозначения линий связи:", s.U(3.5), HAlign.Left, VAlign.Bottom);
            foreach (var id in Kinds.Split(',').Select(x => x.Trim()).Where(x => x != ""))
            {
                y -= 7;
                var k = LineKinds.Get(id);
                var c = new Cable { K = s.K, Line = k.Id, ShowMark = false, Path = new List<P2> { new P2(0, s.U(y)), new P2(s.U(25), s.U(y)) } };
                c.Build(s);
                s.Text(new P2(s.U(30), s.U(y)), k.Name, s.U(TextH), HAlign.Left, VAlign.Middle);
            }
        }

        public override IList<P2> Grips() => new[] { new P2(0, 0) };
        public override P2 MoveGrip(int i, P2 to) => to;
        public override Dictionary<string, string> Save() => new Dictionary<string, string> { { "kinds", Kinds }, { "h", F(TextH) } };
        public override void Load(IDictionary<string, string> d) { Kinds = S(d, "kinds", Kinds); TextH = D(d, "h", 2.5); }
    }
}
