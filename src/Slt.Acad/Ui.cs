// Интерфейс: вкладка «СЛТ» на ленте и палитра. Значки рисуются кодом (векторные контуры), отдельных файлов не нужно.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace Slt.Acad
{
    static class Icons
    {
        // Контуры в поле 32×32 (ось Y вниз): что объект рисует на чертеже.
        static readonly Dictionary<string, string> paths = new Dictionary<string, string>
        {
            ["leader"] = "M5,27 L15,12 L29,12 M5,27 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M17,9 L27,9",
            ["pos"] = "M5,27 L13,13 M5,27 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M13,13 L29,13 M18,4 L18,10 M16,5 L18,4",
            ["level"] = "M16,24 L10,18 L22,18 Z M4,24 L28,24 M10,18 L30,18 M14,8 L26,8 M14,14 L26,14",
            ["levelplan"] = "M5,10 L27,10 L27,22 L5,22 Z M9,16 L23,16",
            ["axis"] = "M16,31 L16,26 M16,23 L16,20 M16,17 L16,15 M16,13 a6,6 0 1,0 0,-12 a6,6 0 1,0 0,12",
            ["axes"] = "M8,8 L8,30 M16,8 L16,30 M24,8 L24,30 M2,14 L30,14 M2,24 L30,24 M8,6 a2.5,2.5 0 1,0 0,-5 a2.5,2.5 0 1,0 0,5 M16,6 a2.5,2.5 0 1,0 0,-5 a2.5,2.5 0 1,0 0,5 M24,6 a2.5,2.5 0 1,0 0,-5 a2.5,2.5 0 1,0 0,5",
            ["format"] = "M2,4 L30,4 L30,28 L2,28 Z M6,6 L28,6 L28,26 L6,26 Z M14,20 L28,20 M14,20 L14,26 M20,20 L20,26",
            ["sheets"] = "M4,6 L20,6 L20,28 L4,28 Z M10,2 L28,2 L28,24 M8,22 L16,22 M12,12 L12,18 M10,13 L12,12",
            ["scale"] = "M3,26 L29,26 M3,22 L3,26 M9,24 L9,26 M15,22 L15,26 M21,24 L21,26 M27,22 L27,26 M6,6 L14,14 M14,6 L6,14 M18,10 L28,10",
            ["edit"] = "M5,27 L8,19 L22,5 L27,10 L13,24 Z M8,19 L13,24 M19,8 L24,13",
            ["kip"] = "M16,4 a8,8 0 1,0 0,16 a8,8 0 1,0 0,-16 M16,20 L16,28 M13.5,28 a2.5,2.5 0 1,0 5,0 a2.5,2.5 0 1,0 -5,0 M2,28 L30,28 M11,12 L21,12",
            ["im"] = "M16,16 a6,6 0 1,0 0,12 a6,6 0 1,0 0,-12 M13,25 L13,19 L16,23 L19,19 L19,25 M16,16 L16,8 M13,5 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M12,9 L20,1",
            ["sym"] = "M2,16 L9,9 L16,16 L9,23 Z M9,11 L9,21 M20,9 a5,5 0 1,0 0,10 M24,20 L30,20 L27,26 Z",
            ["list"] = "M3,5 L29,5 L29,27 L3,27 Z M3,11 L29,11 M10,5 L10,27 M24,5 L24,27 M3,17 L29,17 M3,22 L29,22",
            ["listupd"] = "M3,5 L21,5 L21,27 L3,27 Z M3,11 L21,11 M9,5 L9,27 M25,10 a5,5 0 1,1 -1,8 M25,10 L22,10 M25,10 L25,13",
            ["lib"] = "M5,4 L5,28 L13,28 L13,4 Z M14,4 L14,28 L22,28 L22,4 Z M23,6 L30,26",
            ["fsa"] = "M3,4 L29,4 L29,28 L3,28 Z M3,9 L29,9 M3,19 L29,19 M11,4 L11,28 M17,4 L17,28 M23,4 L23,28 M14,23 a1,1 0 1,0 0.1,0 M20,23 a1,1 0 1,0 0.1,0",
            ["fsasync"] = "M3,6 L17,6 L17,26 L3,26 Z M3,11 L17,11 M10,6 L10,26 M26,8 a6,6 0 1,1 -4,12 M26,8 L23,6 M26,8 L24,11",
            ["cable"] = "M3,26 L12,26 L12,8 L29,8 M3,26 a1.5,1.5 0 1,0 0.1,0 M29,8 a1.5,1.5 0 1,0 0.1,0 M15,5 L22,5",
            ["lines"] = "M3,8 L29,8 M3,16 L29,16 M3,24 L29,24 M12,5 L12,11 M20,5 L20,11 M10,13 L14,13 L14,19 L10,19 Z M18,13 L22,13 L22,19 L18,19 Z M14,22 a2,2 0 1,0 0.1,0",
            ["legend"] = "M3,8 L13,8 M3,16 L13,16 M3,24 L6,24 M9,24 L13,24 M17,8 L29,8 M17,16 L29,16 M17,24 L29,24",
            ["journal"] = "M3,4 L29,4 L29,28 L3,28 Z M3,10 L29,10 M3,16 L29,16 M3,22 L29,22 M9,4 L9,28 M16,4 L16,28 M23,4 L23,28",
            ["spec"] = "M5,3 L27,3 L27,29 L5,29 Z M5,8 L27,8 M9,3 L9,29 M9,13 L24,13 M9,18 L24,18 M9,23 L20,23",
            ["export"] = "M5,4 L19,4 L25,10 L25,28 L5,28 Z M19,4 L19,10 L25,10 M10,15 L20,25 M20,15 L10,25",
            ["base"] = "M16,22 L10,16 L22,16 Z M4,22 L28,22 M14,8 L22,8 M18,4 L18,12",
        };

        public static ImageSource Wpf(string key, int size)
        {
            var g = Geometry.Parse(paths.TryGetValue(key, out var p) ? p : paths["edit"]);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x96)), size >= 32 ? 1.8 : 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(null, pen, g);
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        public static System.Drawing.Bitmap Gdi(string key, int size)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create((BitmapSource)Wpf(key, size)));
            var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms);
        }
    }

    sealed class SendCommand : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) => true;
        public void Execute(object p)
        {
            if (p is RibbonButton b && b.CommandParameter is string cmd) Commands.Send(cmd);
        }
    }

    static class Ui
    {
        // Команда, подпись, значок, подсказка.
        public static readonly string[][] Tools =
        {
            new[] { "Оформление", "_SLT_LEADER ", "Выноска", "leader", "Выноска с полкой: точка/стрелка/без знака (ГОСТ 21.101). Несколько выносок от одной полки." },
            new[] { "Оформление", "_SLT_POS ", "Позиция", "pos", "Позиционная выноска: номер позиции увеличивается автоматически." },
            new[] { "Оформление", "_SLT_LEVEL ", "Отметка", "level", "Отметка уровня на разрезе/схеме; ассоциативная — значение по положению." },
            new[] { "Оформление", "_SLT_LEVELPLAN ", "Отметка на плане", "levelplan", "Отметка уровня на плане — в рамке." },
            new[] { "Оформление", "_SLT_LEVELBASE ", "Базовая отметка", "base", "Точка с известной отметкой (±0,000) для ассоциативных отметок." },
            new[] { "Оси", "_SLT_AXIS ", "Ось", "axis", "Координационная ось с маркировочным кружком." },
            new[] { "Оси", "_SLT_AXES ", "Массив осей", "axes", "Сетка осей по шагам «6000*4, 3000» с размерными цепочками." },
            new[] { "Схемы", "_SLT_KIP ", "Прибор КИП", "kip", "Прибор по ГОСТ 21.208: TE1, PDT3… Линия отбора с точкой, ссылка на графу таблицы. Номера растут сами." },
            new[] { "Схемы", "_SLT_IM ", "Привод (ИМ)", "im", "Исполнительный механизм «M» с позицией Y1, Y2… и линией к заслонке или клапану." },
            new[] { "Схемы", "_SLT_SYM ", "Обозначения", "sym", "Библиотека: вентиляция, арматура, электрооборудование, СКС, ОПС/СОУЭ, СКУД/СОТ, АСКУЭ." },
            new[] { "Схемы", "_SLT_LIST ", "Перечень", "list", "Таблица «Поз. обозначение — Наименование — Кол-во» по элементам листа (рамки формата)." },
            new[] { "Схемы", "_SLT_FSA ", "Таблица ФСА", "fsa", "Табличная часть схемы автоматизации: графы, оборудование по месту, функции контроллера, входы/выходы, интерфейсы." },
            new[] { "Схемы", "_SLT_FSASYNC ", "Синхр. ФСА", "fsasync", "Графы по ссылкам приборов, нумерация подряд, правка ссылок «15,22»." },
            new[] { "Кабели", "_SLT_CABLE ", "Кабель", "cable", "Кабельная линия К1, К2…: способ прокладки (вид линии), марка, начало и конец — по приборам у концов, длина по чертежу." },
            new[] { "Кабели", "_SLT_LINES ", "Виды линий", "lines", "Свои виды кабельных линий: открыто, в трубе, в лотке, в коробе… Наименование, цвет, толщина, штрихи, знак и его шаг." },
            new[] { "Кабели", "_SLT_LEGEND ", "Обозначения линий", "legend", "Условные обозначения кабельных линий (виды, что есть на чертеже)." },
            new[] { "Кабели", "_SLT_JOURNAL ", "Кабельный журнал", "journal", "Таблица кабелей листа: обозначение, начало, конец, марка, длина." },
            new[] { "Таблицы", "_SLT_SPEC ", "Спецификация", "spec", "Спецификация оборудования по ГОСТ 21.110: приборы, оборудование, кабели (по длине)." },
            new[] { "Таблицы", "_SLT_LISTUPDATE ", "Обновить таблицы", "listupd", "Пересобрать перечни, журналы и спецификации после правок." },
            new[] { "Таблицы", "_SLT_EXPORT ", "В Excel", "export", "Кабельный журнал и спецификация всего чертежа — в CSV рядом с DWG (открывается в Excel)." },
            new[] { "Схемы", "_SLT_LIBRARY ", "Своя библиотека", "lib", "Открыть свою библиотеку обозначений (текстовый файл) или перезагрузить её." },
            new[] { "Листы", "_SLT_FORMAT ", "Формат", "format", "Формат А4–А0 (кратные) с рамкой и основной надписью формы 3/6 (ГОСТ 21.101)." },
            new[] { "Листы", "_SLT_SHEETS ", "Нумерация", "sheets", "Нумерация листов: порядок, начальный номер, «Листов», наименования листов — в одном окне." },
            new[] { "Настройки", "_SLT_SCALE ", "Масштаб", "scale", "Масштаб оформления модели (ГОСТ 2.302): размеры текстов и знаков на листе." },
            new[] { "Настройки", "_SLT_EDIT ", "Свойства", "edit", "Окно свойств объекта СЛТ (также двойной щелчок)." },
        };

        static RibbonButton scaleButton;
        static Autodesk.AutoCAD.Windows.PaletteSet palette;
        static WinForms.Label paletteScale;

        public static void Init()
        {
            if (ComponentManager.Ribbon != null) AddTab();
            else ComponentManager.ItemInitialized += OnRibbon;
            AcApp.DocumentManager.DocumentActivated += (s, e) => Refresh();
        }

        static void OnRibbon(object s, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnRibbon;
            AddTab();
        }

        static void AddTab()
        {
            var ribbon = ComponentManager.Ribbon;
            foreach (var t in ribbon.Tabs) if (t.Id == "SLT_TAB") return;
            var tab = new RibbonTab { Title = "СЛТ", Id = "SLT_TAB" };
            var cmd = new SendCommand();
            RibbonPanelSource src = null; string group = null;
            foreach (var t in Tools)
            {
                if (t[0] != group)
                {
                    group = t[0];
                    src = new RibbonPanelSource { Title = group };
                    tab.Panels.Add(new RibbonPanel { Source = src });
                }
                var b = new RibbonButton
                {
                    Text = t[2], ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
                    Image = Icons.Wpf(t[3], 16), LargeImage = Icons.Wpf(t[3], 32), CommandParameter = t[1], CommandHandler = cmd,
                    ToolTip = new RibbonToolTip { Title = t[2], Content = t[4], Command = t[1].Trim().TrimStart('_') },
                };
                if (t[3] == "scale") scaleButton = b;
                src.Items.Add(b);
            }
            ribbon.Tabs.Add(tab);
            Refresh();
        }

        public static void Refresh()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string name;
            try { name = Slt.Core.Scales.Name(Settings.Read(doc.Database).Scale); } catch { return; }
            if (scaleButton != null) scaleButton.Text = "Масштаб " + name;
            if (paletteScale != null) paletteScale.Text = "Масштаб оформления модели: " + name;
        }

        public static void ShowPalette()
        {
            if (palette == null)
            {
                palette = new Autodesk.AutoCAD.Windows.PaletteSet("СЛТ Графика", new Guid("6B0C9E3E-1F3A-4C55-9A0B-5C1D2E7F8A11"))
                {
                    MinimumSize = new System.Drawing.Size(200, 300),
                    Style = Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowCloseButton | Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowAutoHideButton | Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowPropertiesMenu,
                };
                var panel = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, FlowDirection = WinForms.FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new WinForms.Padding(6) };
                paletteScale = new WinForms.Label { AutoSize = true, Margin = new WinForms.Padding(2, 2, 2, 8) };
                panel.Controls.Add(paletteScale);
                string group = null;
                var tips = new WinForms.ToolTip();
                foreach (var t in Tools)
                {
                    if (t[0] != group)
                    {
                        group = t[0];
                        panel.Controls.Add(new WinForms.Label { Text = group, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold), Margin = new WinForms.Padding(2, 8, 2, 2) });
                    }
                    var cmd = t[1];
                    var b = new WinForms.Button
                    {
                        Text = "  " + t[2], Image = Icons.Gdi(t[3], 24), ImageAlign = System.Drawing.ContentAlignment.MiddleLeft, TextImageRelation = WinForms.TextImageRelation.ImageBeforeText,
                        TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 180, Height = 34, FlatStyle = WinForms.FlatStyle.Flat,
                    };
                    b.FlatAppearance.BorderSize = 0;
                    b.Click += (s, e) => Commands.Send(cmd);
                    tips.SetToolTip(b, t[4]);
                    panel.Controls.Add(b);
                }
                palette.Add("Оформление", panel);
            }
            Refresh();
            palette.Visible = true;
        }
    }
}
