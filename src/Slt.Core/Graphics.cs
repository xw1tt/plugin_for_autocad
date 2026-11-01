// Графика объектов оформления: примитивы в локальных координатах объекта (единицы чертежа), толщины по ГОСТ 2.303,
// ряд масштабов по ГОСТ 2.302, оценка ширины текста (адаптер CAD подставляет точное измерение).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public P2 Unit => Length < 1e-12 ? new P2(1, 0) : this * (1 / Length);
        public override string ToString() => $"({X:0.###}; {Y:0.###})";
    }

    public enum ShapeKind { Line, Polyline, Circle, Arc, Fill, Text }
    public enum Weight { Thin, Thick }
    public enum HAlign { Left, Center, Right }
    public enum VAlign { Bottom, Middle, Top }

    /// <summary>Примитив. Fill — залитый многоугольник; Arc — углы в радианах против часовой.</summary>
    public sealed class Shape
    {
        public ShapeKind Kind;
        public P2[] Pts = new P2[0];
        public bool Closed;
        public double R, A0, A1;
        public string Text = "";
        public double H;
        public HAlign Ha;
        public VAlign Va;
        public double Rotation;
        public Weight Weight = Weight.Thin;
        /// <summary>Цвет ACI; 0 — по блоку (цвет объекта).</summary>
        public int Color;
    }

    /// <summary>Построитель: размеры задаются в мм листа и умножаются на k — единиц чертежа в 1 мм листа.</summary>
    public sealed class Sketch
    {
        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly double K;
        public Sketch(double k) { K = k; }
        P2 S(P2 p) => p;
        public Sketch Line(P2 a, P2 b, Weight w = Weight.Thin) { Shapes.Add(new Shape { Kind = ShapeKind.Line, Pts = new[] { a, b }, Weight = w }); return this; }
        public Sketch Poly(bool closed, Weight w, params P2[] p) { Shapes.Add(new Shape { Kind = ShapeKind.Polyline, Pts = p, Closed = closed, Weight = w }); return this; }
        public Sketch Circle(P2 c, double r, Weight w = Weight.Thin) { Shapes.Add(new Shape { Kind = ShapeKind.Circle, Pts = new[] { c }, R = r, Weight = w }); return this; }
        public Sketch Fill(params P2[] p) { Shapes.Add(new Shape { Kind = ShapeKind.Fill, Pts = p, Closed = true }); return this; }
        public Sketch Text(P2 at, string text, double h, HAlign ha = HAlign.Left, VAlign va = VAlign.Bottom, double rot = 0)
        {
            if (!string.IsNullOrEmpty(text)) Shapes.Add(new Shape { Kind = ShapeKind.Text, Pts = new[] { at }, Text = text, H = h, Ha = ha, Va = va, Rotation = rot });
            return this;
        }
        /// <summary>Мм листа → единицы чертежа.</summary>
        public double U(double mm) => mm * K;
    }

    /// <summary>Ширина строки текста для полок и рамок. По умолчанию — оценка для ISOCPEUR / ГОСТ тип А; CAD подставляет точную.</summary>
    public static class TextMeasure
    {
        public static Func<string, double, double> Width = (s, h) => (s ?? "").Sum(ch => ch == ' ' ? 0.45 : char.IsDigit(ch) || ch == ',' || ch == '.' ? 0.62 : char.IsUpper(ch) ? 0.75 : 0.62) * h;
    }

    /// <summary>Масштабы по ГОСТ 2.302-68 (уменьшения) — знаменатели.</summary>
    public static class Scales
    {
        public static readonly double[] Gost = { 1, 2, 2.5, 4, 5, 10, 15, 20, 25, 40, 50, 75, 100, 200, 400, 500, 800, 1000, 2000, 5000, 10000, 20000, 25000, 50000 };
        public static string Name(double n) => "1:" + n.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
        public static double Parse(string s)
        {
            var t = (s ?? "").Trim().Replace(',', '.');
            if (t.StartsWith("1:")) t = t.Substring(2);
            return double.Parse(t, CultureInfo.InvariantCulture);
        }
        /// <summary>Единиц чертежа в 1 мм листа: чертёж в мм, масштаб 1:100 → 100.</summary>
        public static double UnitsPerPaperMm(double scale, double unitsPerMm = 1) => scale * unitsPerMm;
    }

    /// <summary>Значение отметки по ГОСТ 21.101: «+3,500», «±0,000», «-0,450».</summary>
    public static class Levels
    {
        public static string Format(double m, int decimals = 3)
        {
            var v = Math.Round(Math.Abs(m), decimals).ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture).Replace('.', ',');
            if (Math.Abs(m) < 0.5 * Math.Pow(10, -decimals)) return "±" + v;
            return (m > 0 ? "+" : "-") + v;
        }
    }
}
