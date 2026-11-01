// Отрисовка примитивов в SVG — превью объектов для сверки с ГОСТ и документации.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Slt.Core
{
    public static class Svg
    {
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Группа примитивов со смещением (ox, oy); ось Y — вверх, как в чертеже.</summary>
        public static string Group(IEnumerable<Shape> shapes, double ox, double oy, double thin = 0.25, double thick = 0.5)
        {
            var sb = new StringBuilder($"<g transform=\"translate({F(ox)} {F(oy)}) scale(1 -1)\" fill=\"none\" stroke=\"#000\" stroke-linecap=\"round\">");
            foreach (var s in shapes)
            {
                var sw = s.Weight == Weight.Thick ? thick : thin;
                switch (s.Kind)
                {
                    case ShapeKind.Line: sb.Append($"<line x1=\"{F(s.Pts[0].X)}\" y1=\"{F(s.Pts[0].Y)}\" x2=\"{F(s.Pts[1].X)}\" y2=\"{F(s.Pts[1].Y)}\" stroke-width=\"{F(sw)}\"/>"); break;
                    case ShapeKind.Polyline: sb.Append($"<{(s.Closed ? "polygon" : "polyline")} points=\"{Pts(s.Pts)}\" stroke-width=\"{F(sw)}\"/>"); break;
                    case ShapeKind.Fill: sb.Append($"<polygon points=\"{Pts(s.Pts)}\" fill=\"#000\" stroke=\"none\"/>"); break;
                    case ShapeKind.Circle: sb.Append($"<circle cx=\"{F(s.Pts[0].X)}\" cy=\"{F(s.Pts[0].Y)}\" r=\"{F(s.R)}\" stroke-width=\"{F(sw)}\"/>"); break;
                    case ShapeKind.Arc:
                        var c = s.Pts[0];
                        sb.Append($"<path d=\"M{F(c.X + s.R * Math.Cos(s.A0))} {F(c.Y + s.R * Math.Sin(s.A0))} A{F(s.R)} {F(s.R)} 0 {((s.A1 - s.A0) % (2 * Math.PI) > Math.PI ? 1 : 0)} 1 {F(c.X + s.R * Math.Cos(s.A1))} {F(c.Y + s.R * Math.Sin(s.A1))}\" stroke-width=\"{F(sw)}\"/>");
                        break;
                    case ShapeKind.Text:
                        var p = s.Pts[0];
                        var anchor = s.Ha == HAlign.Left ? "start" : s.Ha == HAlign.Center ? "middle" : "end";
                        var dy = s.Va == VAlign.Bottom ? 0 : s.Va == VAlign.Middle ? s.H / 2 : s.H;
                        sb.Append($"<text transform=\"translate({F(p.X)} {F(p.Y)}) rotate({F(-s.Rotation * 180 / Math.PI)}) scale(1 -1)\" y=\"{F(dy)}\" font-size=\"{F(s.H * 1.25)}\" font-family=\"ISOCPEUR, 'Liberation Sans Narrow', 'Arial Narrow', Arial\" textLength=\"{F(TextMeasure.Width(s.Text, s.H))}\" lengthAdjust=\"spacingAndGlyphs\" font-style=\"italic\" text-anchor=\"{anchor}\" fill=\"#000\" stroke=\"none\">{System.Security.SecurityElement.Escape(s.Text)}</text>");
                        break;
                }
            }
            return sb.Append("</g>").ToString();
        }

        static string Pts(P2[] p) => string.Join(" ", p.Select(x => F(x.X) + "," + F(x.Y)));

        public static string Document(double w, double h, string body, double zoom = 4) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w * zoom)}\" height=\"{F(h * zoom)}\" viewBox=\"0 0 {F(w)} {F(h)}\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>{body}</svg>";
    }
}
