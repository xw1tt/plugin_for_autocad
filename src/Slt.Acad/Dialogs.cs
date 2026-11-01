// Окна свойств объектов (двойной щелчок, контекстное меню, сразу после вставки): сетка свойств с русскими названиями,
// группами и подсказками. Модели представления переводят параметры ядра в поля и обратно.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Slt.Core;
using Shape = Slt.Core.Shape;
using Leader = Slt.Core.Leader;

namespace Slt.Acad
{
    public enum Окончание { Точка, Стрелка, Нет }
    public enum Штамп { Форма3, Форма6, Нет }
    public enum Размер { A4, A3, A2, A1, A0 }

    /// <summary>Модель представления: из объекта и обратно в объект.</summary>
    public interface IView { void To(Smart o); }

    public sealed class LeaderView : IView
    {
        [Category("Текст"), DisplayName("Над полкой"), Description("Текст над полкой: наименование, марка, номер позиции.")] public string Top { get; set; }
        [Category("Текст"), DisplayName("Под полкой"), Description("Дополнительный текст под полкой (мельче).")] public string Bottom { get; set; }
        [Category("Текст"), DisplayName("Высота текста, мм"), Description("Высота на листе: 2,5; 3,5; 5 (ГОСТ 2.304). Номер позиции — на размер больше.")] public double H { get; set; }
        [Category("Выноска"), DisplayName("Окончание"), Description("ГОСТ 21.101: точка — внутри контура, стрелка — на линии контура, без знака — на линии.")] public Окончание End { get; set; }
        [Category("Выноска"), DisplayName("Позиционная"), Description("Номер позиции на полке.")] public bool Position { get; set; }
        public LeaderView(Leader l) { Top = l.Top; Bottom = l.Bottom; H = l.TextH; Position = l.Position; End = l.End == LeaderEnd.Arrow ? Окончание.Стрелка : l.End == LeaderEnd.None ? Окончание.Нет : Окончание.Точка; }
        public void To(Smart o)
        {
            var l = (Leader)o; l.Top = Top ?? ""; l.Bottom = Bottom ?? ""; l.TextH = H > 0 ? H : 2.5; l.Position = Position;
            l.End = End == Окончание.Стрелка ? LeaderEnd.Arrow : End == Окончание.Нет ? LeaderEnd.None : LeaderEnd.Dot;
        }
    }

    public sealed class LevelView : IView
    {
        [Category("Значение"), DisplayName("Ассоциативная"), Description("Значение по положению относительно базовой отметки ±0,000 (пересчитывается при переносе).")] public bool Assoc { get; set; }
        [Category("Значение"), DisplayName("Отметка, м"), Description("Для неассоциативной отметки.")] public double Value { get; set; }
        [Category("Значение"), DisplayName("Свой текст"), Description("Вместо значения, например «+3,500 (низ лотка)». Пусто — значение.")] public string Override { get; set; }
        [Category("Значение"), DisplayName("Примечание"), Description("Под полкой мельче: «низ лотка», «ось трубы».")] public string Note { get; set; }
        [Category("Вид"), DisplayName("Полка сверху")] public bool Up { get; set; }
        [Category("Вид"), DisplayName("Полка справа")] public bool Right { get; set; }
        [Category("Вид"), DisplayName("Высота текста, мм")] public double H { get; set; }
        public LevelView(LevelMark m) { Assoc = m.Associative; Value = m.Value; Override = m.Override; Note = m.Note; Up = m.Up; Right = m.Right; H = m.TextH; }
        public void To(Smart o)
        {
            var m = (LevelMark)o; m.Associative = Assoc; m.Value = Value; m.Override = Override ?? ""; m.Note = Note ?? ""; m.Up = Up; m.Right = Right; m.TextH = H > 0 ? H : 2.5;
        }
    }

    public sealed class PlanLevelView : IView
    {
        [DisplayName("Отметка, м")] public double Value { get; set; }
        [DisplayName("Свой текст")] public string Override { get; set; }
        [DisplayName("Высота текста, мм")] public double H { get; set; }
        public PlanLevelView(PlanLevel m) { Value = m.Value; Override = m.Override; H = m.TextH; }
        public void To(Smart o) { var m = (PlanLevel)o; m.Value = Value; m.Override = Override ?? ""; m.TextH = H > 0 ? H : 2.5; }
    }

    public sealed class AxisView : IView
    {
        [DisplayName("Марка оси"), Description("Цифра или буква (кроме Ё, З, Й, О, Х, Ц, Ч, Щ, Ъ, Ы, Ь).")] public string Name { get; set; }
        [DisplayName("Диаметр кружка, мм"), Description("6…12 мм по ГОСТ 21.101.")] public double D { get; set; }
        [DisplayName("Кружки с двух сторон")] public bool Both { get; set; }
        [DisplayName("Высота текста, мм")] public double H { get; set; }
        public AxisView(Axis a) { Name = a.Name; D = a.Diameter; Both = a.BubbleAtStart; H = a.TextH; }
        public void To(Smart o) { var a = (Axis)o; a.Name = Name ?? ""; a.Diameter = Math.Max(6, Math.Min(12, D)); a.BubbleAtStart = Both; a.TextH = H > 0 ? H : 3.5; }
    }

    public sealed class AxesView : IView
    {
        [Category("Шаги"), DisplayName("Шаги по X, мм"), Description("Через запятую, повтор — звёздочкой: «6000*4, 3000».")] public string Xs { get; set; }
        [Category("Шаги"), DisplayName("Шаги по Y, мм")] public string Ys { get; set; }
        [Category("Марки"), DisplayName("Первая ось по X"), Description("Цифровые оси — слева направо.")] public string X1 { get; set; }
        [Category("Марки"), DisplayName("Первая ось по Y"), Description("Буквенные оси — снизу вверх.")] public string Y1 { get; set; }
        [Category("Кружки"), DisplayName("Снизу")] public bool B { get; set; }
        [Category("Кружки"), DisplayName("Слева")] public bool L { get; set; }
        [Category("Кружки"), DisplayName("Сверху")] public bool T { get; set; }
        [Category("Кружки"), DisplayName("Справа")] public bool R { get; set; }
        [Category("Вид"), DisplayName("Размерные цепочки")] public bool Dims { get; set; }
        [Category("Вид"), DisplayName("Выход осей, мм листа")] public double Ov { get; set; }
        [Category("Вид"), DisplayName("Диаметр кружка, мм")] public double D { get; set; }
        public AxesView(AxisGrid g) { Xs = g.XSpans; Ys = g.YSpans; X1 = g.XFirst; Y1 = g.YFirst; B = g.Bottom; L = g.Left; T = g.Top; R = g.Right; Dims = g.Dimensions; Ov = g.Overshoot; D = g.Diameter; }
        public void To(Smart o)
        {
            AxisNames.Spans(Xs); AxisNames.Spans(Ys); // проверка формата — исключение покажет окно
            var g = (AxisGrid)o; g.XSpans = Xs; g.YSpans = Ys; g.XFirst = X1; g.YFirst = Y1; g.Bottom = B; g.Left = L; g.Top = T; g.Right = R;
            g.Dimensions = Dims; g.Overshoot = Math.Max(10, Ov); g.Diameter = Math.Max(6, Math.Min(12, D));
        }
    }

    public sealed class FormatView : IView
    {
        [Category("1. Лист"), DisplayName("Формат")] public Размер Size { get; set; }
        [Category("1. Лист"), DisplayName("Кратность"), Description("А4×3, А3×3… (ГОСТ 2.301). 1 — основной формат.")] public int Mult { get; set; }
        [Category("1. Лист"), DisplayName("Альбомная")] public bool Land { get; set; }
        [Category("1. Лист"), DisplayName("Основная надпись"), Description("Форма 3 — первый лист, форма 6 — последующие (ГОСТ 21.101).")] public Штамп Stamp { get; set; }
        [Category("1. Лист"), DisplayName("Дополнительные графы"), Description("Инв. № подл., Подп. и дата, Взам. инв. № — на поле подшивки.")] public bool Extra { get; set; }
        [Category("2. Основная надпись"), DisplayName("Обозначение (шифр)")] public string Code { get; set; }
        [Category("2. Основная надпись"), DisplayName("Объект")] public string Object { get; set; }
        [Category("2. Основная надпись"), DisplayName("Раздел")] public string Section { get; set; }
        [Category("2. Основная надпись"), DisplayName("Наименование листа")] public string Title { get; set; }
        [Category("2. Основная надпись"), DisplayName("Стадия")] public string Stage { get; set; }
        [Category("2. Основная надпись"), DisplayName("Организация")] public string Org { get; set; }
        [Category("3. Нумерация"), DisplayName("Автонумерация"), Description("Лист и листов — командой «Нумерация листов».")] public bool Auto { get; set; }
        [Category("3. Нумерация"), DisplayName("Лист")] public string Sheet { get; set; }
        [Category("3. Нумерация"), DisplayName("Листов")] public string Sheets { get; set; }
        [Category("4. Подписи"), DisplayName("Разраб.")] public string N1 { get; set; }
        [Category("4. Подписи"), DisplayName("Пров.")] public string N2 { get; set; }
        [Category("4. Подписи"), DisplayName("Строка 3: должность")] public string R3 { get; set; }
        [Category("4. Подписи"), DisplayName("Строка 3: фамилия")] public string N3 { get; set; }
        [Category("4. Подписи"), DisplayName("Н.контр.")] public string N4 { get; set; }
        [Category("4. Подписи"), DisplayName("ГИП")] public string N5 { get; set; }
        [Category("4. Подписи"), DisplayName("Дата"), Description("Для всех подписей: ММ.ГГ.")] public string Date { get; set; }

        public FormatView(SheetFormat f)
        {
            Size = Enum.TryParse(f.Size, out Размер s) ? s : Размер.A3; Mult = f.Multiple; Land = f.Landscape; Extra = f.Extra; Auto = f.AutoNumber;
            Stamp = f.Stamp == StampForm.Form6 ? Штамп.Форма6 : f.Stamp == StampForm.None ? Штамп.Нет : Штамп.Форма3;
            Code = f.Field("code"); Object = f.Field("object"); Section = f.Field("section"); Title = f.Field("title"); Stage = f.Field("stage"); Org = f.Field("org");
            Sheet = f.Field("sheet"); Sheets = f.Field("sheets");
            N1 = f.Field("n1"); N2 = f.Field("n2"); R3 = f.Field("r3"); N3 = f.Field("n3"); N4 = f.Field("n4"); N5 = f.Field("n5"); Date = f.Field("d1");
        }

        public void To(Smart o)
        {
            var f = (SheetFormat)o;
            f.Size = Size.ToString(); f.Multiple = Math.Max(1, Mult); f.Landscape = Land; f.Extra = Extra; f.AutoNumber = Auto;
            f.Stamp = Stamp == Штамп.Форма6 ? StampForm.Form6 : Stamp == Штамп.Нет ? StampForm.None : StampForm.Form3;
            void Set(string k, string v) => f.Fields[k] = v ?? "";
            Set("code", Code); Set("object", Object); Set("section", Section); Set("title", Title); Set("stage", Stage); Set("org", Org);
            Set("sheet", Sheet); Set("sheets", Sheets);
            Set("n1", N1); Set("n2", N2); Set("r3", R3); Set("n3", N3); Set("n4", N4); Set("n5", N5);
            for (int i = 1; i <= 5; i++) Set("d" + i, string.IsNullOrWhiteSpace(f.Field("n" + i)) ? "" : Date);
        }
    }

    public enum ФормаКИП { ПоМесту, НаЩите, ЦифровойБлок, ПАЗ, ИсполнительныйМеханизм }

    public sealed class KipView : IView
    {
        [RefreshProperties(RefreshProperties.All), Category("1. Обозначение"), DisplayName("Позиция"), Description("Позиционное обозначение: буквенный код по ГОСТ 21.208 и номер — TE1, PDT3, Y2.")] public string Pos { get; set; }
        [Category("1. Обозначение"), DisplayName("Расшифровка кода"), Description("По табл. 2 ГОСТ 21.208-2013 — для проверки.")] public string Explain => KipLetters.Explain(Pos);
        [Category("1. Обозначение"), DisplayName("Вид"), Description("По месту — окружность; на щите — с чертой; цифровой блок — квадрат; ПАЗ — ромб; ИМ — малая окружность «M».")] public ФормаКИП Form { get; set; }
        [Category("1. Обозначение"), DisplayName("Текст внутри"), Description("Пусто — позиция внутри. «M» — у исполнительного механизма (позиция выносится, ручкой можно передвинуть).")] public string Inner { get; set; }
        [Category("1. Обозначение"), DisplayName("Буквы и номер в две строки"), Description("Как в ГОСТ: буквы сверху, номер снизу. На щите — всегда.")] public bool Two { get; set; }
        [Category("2. Линии"), DisplayName("Линия отбора"), Description("Линия к технологической линии; ручкой — точка отбора.")] public bool Tap { get; set; }
        [Category("2. Линии"), DisplayName("Точка на конце отбора")] public bool Dot { get; set; }
        [Category("2. Линии"), DisplayName("Ссылка"), Description("Линия к таблице (графе) с номером.")] public bool HasRef { get; set; }
        [Category("2. Линии"), DisplayName("Номер ссылки"), Description("Номер графы таблицы, клеммы: «10», «16,23».")] public string Ref { get; set; }
        [Category("3. Перечень"), DisplayName("Наименование"), Description("Для перечня элементов: «Канальный датчик температуры TS-K250 PRO PT1000».")] public string Name { get; set; }
        [Category("3. Перечень"), DisplayName("Количество")] public double Qty { get; set; }
        [Category("3. Перечень"), DisplayName("Примечание")] public string Note { get; set; }
        [Category("4. Вид"), DisplayName("Диаметр, мм"), Description("10 мм — прибор, 6 мм — ИМ.")] public double D { get; set; }
        [Category("4. Вид"), DisplayName("Высота текста, мм")] public double H { get; set; }
        public KipView(Instrument i)
        {
            Pos = i.Pos; Form = (ФормаКИП)(int)i.Form; Inner = i.Inner; Two = i.TwoLines; Tap = i.HasTap; Dot = i.TapDot; HasRef = i.HasRef; Ref = i.Ref;
            Name = i.Name; Qty = i.Qty; Note = i.Note; D = i.Diameter; H = i.TextH;
        }
        public void To(Smart o)
        {
            var i = (Instrument)o;
            i.Pos = (Pos ?? "").Trim(); i.Form = (KipShape)(int)Form; i.Inner = Inner ?? ""; i.TwoLines = Two; i.HasTap = Tap; i.TapDot = Dot; i.HasRef = HasRef; i.Ref = Ref ?? "";
            i.Name = Name ?? ""; i.Qty = Qty; i.Note = Note ?? ""; i.Diameter = Math.Max(3, D); i.TextH = H > 0 ? H : 2.5;
            if (i.Form == KipShape.Actuator && i.Inner == "") i.Inner = "M";
        }
    }

    public sealed class SymbolView : IView
    {
        [Category("Обозначение"), DisplayName("Символ"), Description("Из библиотеки (SLT_LIBRARY — своя библиотека).")] public string Sym { get; }
        [Category("Обозначение"), DisplayName("Позиция"), Description("Позиционное обозначение: ЦН1, ШУ-ПВ1… Пусто — без подписи.")] public string Pos { get; set; }
        [Category("Обозначение"), DisplayName("Показывать позицию")] public bool ShowPos { get; set; }
        [Category("Обозначение"), DisplayName("Поворот, °")] public double Angle { get; set; }
        [Category("Перечень"), DisplayName("Наименование"), Description("Пусто — наименование символа из библиотеки.")] public string Name { get; set; }
        [Category("Перечень"), DisplayName("Количество")] public double Qty { get; set; }
        [Category("Перечень"), DisplayName("Примечание")] public string Note { get; set; }
        [Category("Вид"), DisplayName("Высота текста, мм")] public double H { get; set; }
        public SymbolView(Symbol y) { Sym = y.Def?.Name ?? y.Sym; Pos = y.Pos; ShowPos = y.ShowPos; Angle = Math.Round(y.Angle * 180 / Math.PI, 2); Name = y.Name; Qty = y.Qty; Note = y.Note; H = y.TextH; }
        public void To(Smart o)
        {
            var y = (Symbol)o; y.Pos = (Pos ?? "").Trim(); y.ShowPos = ShowPos; y.Angle = Angle * Math.PI / 180; y.Name = Name ?? ""; y.Qty = Qty; y.Note = Note ?? ""; y.TextH = H > 0 ? H : 2.5;
        }
    }

    public sealed class ListView : IView
    {
        [DisplayName("Заголовок"), Description("Над таблицей, например «Перечень приборов ПВ1». Пусто — без заголовка.")] public string Caption { get; set; }
        [DisplayName("Ширина «Поз.», мм")] public double W1 { get; set; }
        [DisplayName("Ширина «Наименование», мм")] public double W2 { get; set; }
        [DisplayName("Ширина «Кол-во», мм")] public double W3 { get; set; }
        [DisplayName("Высота текста, мм")] public double H { get; set; }
        public ListView(PosList l) { Caption = l.Caption; W1 = l.Widths[0]; W2 = l.Widths[1]; W3 = l.Widths[2]; H = l.TextH; }
        public void To(Smart o) { var l = (PosList)o; l.Caption = Caption ?? ""; l.Widths = new[] { Math.Max(10, W1), Math.Max(20, W2), Math.Max(10, W3) }; l.TextH = H > 0 ? H : 2.5; }
    }

    public static class Dialogs
    {
        public static IView ViewOf(Smart o)
        {
            switch (o)
            {
                case Leader l: return new LeaderView(l);
                case LevelMark m: return new LevelView(m);
                case PlanLevel p: return new PlanLevelView(p);
                case Axis a: return new AxisView(a);
                case AxisGrid g: return new AxesView(g);
                case SheetFormat f: return new FormatView(f);
                case Instrument i: return new KipView(i);
                case Symbol y: return new SymbolView(y);
                case PosList pl: return new ListView(pl);
            }
            return null;
        }

        /// <summary>Окно свойств; true — применить.</summary>
        public static bool Edit(Smart o)
        {
            var view = ViewOf(o);
            if (view == null) return false;
            using (var form = new Form
            {
                Text = o.Title + " — свойства", Width = 460, Height = o is SheetFormat ? 640 : o is Instrument ? 560 : 420, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.SizableToolWindow, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = new Font("Segoe UI", 9f),
            })
            {
                var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = view, ToolbarVisible = false, PropertySort = PropertySort.Categorized, HelpVisible = true };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
                var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Width = 90 };
                bar.Controls.Add(cancel); bar.Controls.Add(ok);
                form.Controls.Add(grid); form.Controls.Add(bar);
                form.AcceptButton = ok; form.CancelButton = cancel;
                // Сразу в первое текстовое поле — можно печатать.
                form.Shown += (s, e) => { grid.Focus(); };
                while (true)
                {
                    if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != DialogResult.OK) return false;
                    try { view.To(o); return true; }
                    catch (System.Exception ex) { MessageBox.Show(form, ex.Message, "Проверьте значения", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
        }
    }
}
