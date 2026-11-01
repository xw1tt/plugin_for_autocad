// Формат листа (ГОСТ 2.301) с рамкой и основной надписью (ГОСТ 21.101-2020, формы 3 и 6) и дополнительными графами.
// Начало координат — левый нижний угол листа. Размеры — мм листа × масштаб (в пространстве листа масштаб 1).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Slt.Core
{
    public enum StampForm { None, Form3, Form6 }

    public sealed class SheetFormat : Smart
    {
        public static readonly Dictionary<string, double[]> Sizes = new Dictionary<string, double[]>
        {
            { "A4", new[] { 210.0, 297 } }, { "A3", new[] { 297.0, 420 } }, { "A2", new[] { 420.0, 594 } }, { "A1", new[] { 594.0, 841 } }, { "A0", new[] { 841.0, 1189 } },
        };

        public string Size = "A3";
        /// <summary>Кратность (ГОСТ 2.301: А4×3 = 297×630 — короткая сторона основного формата × n).</summary>
        public int Multiple = 1;
        public bool Landscape = true;
        public StampForm Stamp = StampForm.Form3;
        /// <summary>Дополнительные графы слева: «Инв. № подл.», «Подп. и дата», «Взам. инв. №».</summary>
        public bool Extra = true;
        /// <summary>Графы основной надписи: code, object, section, title, stage, sheet, sheets, org, r1…r5, n1…n5, d1…d5.</summary>
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        /// <summary>Номер листа — автонумерация (команда нумерации листов); иначе — из поля.</summary>
        public bool AutoNumber = true;
        public override string Kind => "format";
        public override string Title => "Формат";

        public static readonly string[] DefaultRoles = { "Разраб.", "Пров.", "", "Н.контр.", "ГИП" };

        public string Field(string k) => Fields.TryGetValue(k, out var v) ? v ?? "" : "";

        /// <summary>Размер листа, мм: ширина × высота.</summary>
        public double[] Paper
        {
            get
            {
                var b = Sizes.TryGetValue(Size, out var v) ? v : Sizes["A3"];
                double a = b[1], c = b[0] * Math.Max(1, Multiple);
                double lo = Math.Min(a, c), hi = Math.Max(a, c);
                // А4 без кратности — только книжный (основная надпись вдоль короткой стороны).
                if (Size == "A4" && Multiple <= 1) return new[] { lo, hi };
                return Landscape ? new[] { hi, lo } : new[] { lo, hi };
            }
        }

        public string Caption => Size + (Multiple > 1 ? "×" + Multiple : "");

        public override void Build(Sketch s)
        {
            var wh = Paper; double w = wh[0], h = wh[1];
            P2 P(double x, double y) => new P2(s.U(x), s.U(y));
            void L(double x0, double y0, double x1, double y1, Weight wt = Weight.Thin) => s.Line(P(x0, y0), P(x1, y1), wt);
            void T(double x, double y, string t, double hh, HAlign ha = HAlign.Left, VAlign va = VAlign.Middle, double rot = 0) => s.Text(P(x, y), t, s.U(hh), ha, va, rot);
            // Лист и рамка: слева 20, остальные 5 мм.
            s.Poly(true, Weight.Thin, P(0, 0), P(w, 0), P(w, h), P(0, h));
            s.Poly(true, Weight.Thick, P(20, 5), P(w - 5, 5), P(w - 5, h - 5), P(20, h - 5));
            T(w - 5, 1.5, "Формат " + Caption, 2.5, HAlign.Right, VAlign.Middle);
            double x0 = w - 5 - 185, y0 = 5;
            if (Stamp == StampForm.Form3) Form3(s, x0, y0, L, T, Fit);
            else if (Stamp == StampForm.Form6) Form6(x0, y0, L, T);
            if (Extra)
            {
                // Дополнительные графы на поле подшивки: 12 мм (5 + 7), снизу вверх — 25, 35, 25.
                double ex = 8, hgt = 0;
                var lens = new[] { 25.0, 35, 25 }; var labels = new[] { "Инв. № подл.", "Подп. и дата", "Взам. инв. №" };
                for (int gi = 0; gi < 3; gi++)
                {
                    double len = lens[gi]; var label = labels[gi];
                    L(ex, 5 + hgt, ex, 5 + hgt + len, Weight.Thick); L(ex + 5, 5 + hgt, ex + 5, 5 + hgt + len, Weight.Thick);
                    L(ex, 5 + hgt, 20, 5 + hgt, Weight.Thick);
                    T(ex + 2.5, 5 + hgt + len / 2, label, 2.0, HAlign.Center, VAlign.Middle, Math.PI / 2);
                    hgt += len;
                }
                L(ex, 5 + hgt, 20, 5 + hgt, Weight.Thick);
            }
        }

        delegate void LineFn(double x0, double y0, double x1, double y1, Weight w = Weight.Thin);
        delegate void TextFn(double x, double y, string t, double h, HAlign ha = HAlign.Left, VAlign va = VAlign.Middle, double rot = 0);

        /// <summary>Высота шрифта, при которой строка помещается в ширину, мм.</summary>
        static double Fit(string t, double width, double h)
        {
            var w = TextMeasure.Width(t ?? "", h);
            return w <= width || w <= 0 ? h : Math.Max(1.5, h * width / w);
        }

        /// <summary>Форма 3 (185×55): слева 11 строк по 5 мм — изменения, заголовок, подписи; справа — обозначение, объект, наименование, стадия/лист/листов, организация.</summary>
        void Form3(Sketch s, double x, double y, LineFn L, TextFn T, Func<string, double, double, double> fit)
        {
            L(x, y, x, y + 55, Weight.Thick); L(x, y + 55, x + 185, y + 55, Weight.Thick);
            double[] cw = { 10, 10, 10, 10, 15, 10 };
            for (int r = 1; r < 11; r++) L(x, y + r * 5, x + 65, y + r * 5, r == 5 || r == 6 ? Weight.Thick : Weight.Thin);
            double cx = x;
            for (int i = 0; i < cw.Length; i++)
            {
                cx += cw[i];
                // В строках подписей «Изм.+Кол.уч» — должность, «Лист+№ док.» — фамилия.
                L(cx, y + (i <= 2 ? 25 : 0), cx, y + 55, Weight.Thick);
                if (i == 3 || i == 4 || i == 5) L(cx, y, cx, y + 25, Weight.Thick);
            }
            L(x + 20, y, x + 20, y + 25, Weight.Thick);
            string[] head = { "Изм.", "Кол.уч", "Лист", "№ док.", "Подп.", "Дата" };
            cx = x;
            for (int i = 0; i < head.Length; i++) { T(cx + cw[i] / 2, y + 27.5, head[i], fit(head[i], cw[i] - 1, 2.0), HAlign.Center); cx += cw[i]; }
            for (int i = 0; i < 5; i++)
            {
                var yy = y + 22.5 - i * 5;
                var role = Fields.ContainsKey("r" + (i + 1)) ? Field("r" + (i + 1)) : DefaultRoles[i];
                T(x + 1, yy, role, fit(role, 18, 2.0));
                T(x + 21, yy, Field("n" + (i + 1)), fit(Field("n" + (i + 1)), 18, 2.0));
                T(x + 60, yy, Field("d" + (i + 1)), fit(Field("d" + (i + 1)), 9, 1.8), HAlign.Center);
            }
            double rx = x + 65;
            // Правая часть (120 мм): обозначение 10 | объект 15 | раздел + стадия/лист/листов 15 | наименование листа + организация 15.
            L(rx, y + 45, x + 185, y + 45, Weight.Thick);
            L(rx, y + 30, x + 185, y + 30, Weight.Thick);
            L(rx, y + 15, x + 185, y + 15, Weight.Thick);
            L(rx + 70, y, rx + 70, y + 30, Weight.Thick);
            L(rx + 70, y + 25, x + 185, y + 25, Weight.Thin);
            L(rx + 85, y + 15, rx + 85, y + 30, Weight.Thin);
            L(rx + 100, y + 15, rx + 100, y + 30, Weight.Thin);
            T(rx + 60, y + 50, Field("code"), fit(Field("code"), 116, 5), HAlign.Center);
            Block(T, rx + 60, y + 37.5, Field("object"), 116, 15, 3);
            Block(T, rx + 35, y + 22.5, Field("section"), 68, 15, 3);
            Block(T, rx + 35, y + 7.5, Field("title"), 68, 15, 3);
            T(rx + 77.5, y + 27.5, "Стадия", 2.0, HAlign.Center); T(rx + 92.5, y + 27.5, "Лист", 2.0, HAlign.Center); T(rx + 110, y + 27.5, "Листов", 2.0, HAlign.Center);
            T(rx + 77.5, y + 20, Field("stage"), 3, HAlign.Center); T(rx + 92.5, y + 20, Field("sheet"), 3, HAlign.Center); T(rx + 110, y + 20, Field("sheets"), 3, HAlign.Center);
            Block(T, rx + 95, y + 7.5, Field("org"), 48, 15, 3);
        }

        /// <summary>Текст графы по центру ячейки: одной строкой, если помещается; иначе — по словам в 2–3 строки; иначе мельче.</summary>
        static void Block(TextFn T, double cx, double cy, string text, double width, double height, double h)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = new List<string> { text };
            for (int n = 1; n <= 3; n++)
            {
                lines = Wrap(text, width - 2, h, n);
                if (lines.All(l => TextMeasure.Width(l, h) <= width - 2) && lines.Count * h * 1.6 <= height) break;
            }
            var hh = h;
            var widest = lines.Max(l => TextMeasure.Width(l, h));
            if (widest > width - 2) hh = h * (width - 2) / widest;
            if (lines.Count * hh * 1.6 > height) hh = height / (lines.Count * 1.6);
            hh = Math.Max(1.5, hh);
            var step = hh * 1.6;
            for (int i = 0; i < lines.Count; i++) T(cx, cy + (lines.Count - 1) * step / 2 - i * step, lines[i], hh, HAlign.Center);
        }

        /// <summary>Перенос по словам на n строк примерно равной ширины.</summary>
        static List<string> Wrap(string text, double width, double h, int n)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (n <= 1 || words.Length <= 1) return new List<string> { text };
            var target = TextMeasure.Width(text, h) / n;
            var res = new List<string>(); var cur = "";
            foreach (var w in words)
            {
                var next = cur == "" ? w : cur + " " + w;
                if (cur != "" && TextMeasure.Width(next, h) > Math.Max(target, width * 0.5) && res.Count < n - 1) { res.Add(cur); cur = w; }
                else cur = next;
            }
            res.Add(cur);
            return res;
        }

        /// <summary>Форма 6 (185×15) для последующих листов: изменения (3 строки), обозначение, номер листа.</summary>
        void Form6(double x, double y, LineFn L, TextFn T)
        {
            L(x, y, x, y + 15, Weight.Thick); L(x, y + 15, x + 185, y + 15, Weight.Thick);
            double[] cw = { 10, 10, 10, 10, 15, 10 };
            for (int r = 1; r < 3; r++) L(x, y + r * 5, x + 65, y + r * 5, r == 1 ? Weight.Thick : Weight.Thin);
            double cx = x;
            foreach (var w in cw) { cx += w; L(cx, y, cx, y + 15, Weight.Thick); }
            string[] head = { "Изм.", "Кол.уч", "Лист", "№ док.", "Подп.", "Дата" };
            cx = x;
            for (int i = 0; i < head.Length; i++) { T(cx + cw[i] / 2, y + 2.5, head[i], Fit(head[i], cw[i] - 1, 2.0), HAlign.Center); cx += cw[i]; }
            L(x + 175, y, x + 175, y + 15, Weight.Thick); L(x + 175, y + 8, x + 185, y + 8, Weight.Thin);
            T(x + 120, y + 7.5, Field("code"), Fit(Field("code"), 106, 5), HAlign.Center);
            T(x + 180, y + 11.5, "Лист", 2.0, HAlign.Center);
            T(x + 180, y + 4, Field("sheet"), 3, HAlign.Center);
        }

        public override Dictionary<string, string> Save()
        {
            var d = new Dictionary<string, string>
            {
                { "size", Size }, { "mult", Multiple.ToString(CultureInfo.InvariantCulture) }, { "land", Landscape ? "1" : "0" }, { "stamp", Stamp.ToString() },
                { "extra", Extra ? "1" : "0" }, { "auto", AutoNumber ? "1" : "0" },
            };
            foreach (var f in Fields) d["f." + f.Key] = f.Value;
            return d;
        }

        public override void Load(IDictionary<string, string> d)
        {
            Size = S(d, "size", "A3"); Multiple = (int)D(d, "mult", 1); Landscape = B(d, "land", true);
            Stamp = Enum.TryParse(S(d, "stamp", "Form3"), out StampForm f) ? f : StampForm.Form3;
            Extra = B(d, "extra", true); AutoNumber = B(d, "auto", true);
            Fields = d.Where(kv => kv.Key.StartsWith("f.")).ToDictionary(kv => kv.Key.Substring(2), kv => kv.Value);
        }

        /// <summary>Автонумерация: листы по порядку (как отдаст CAD — по вкладкам листов или слева направо в модели).</summary>
        public static void Number(IList<SheetFormat> sheets)
        {
            var auto = sheets.Where(s => s.AutoNumber).ToList();
            for (int i = 0; i < auto.Count; i++)
            {
                auto[i].Fields["sheet"] = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (auto[i].Stamp == StampForm.Form3) auto[i].Fields["sheets"] = auto.Count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
