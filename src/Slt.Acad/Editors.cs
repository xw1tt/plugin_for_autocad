// Окна правки: строки таблиц (перечень, журнал, спецификация) с вставкой из Excel, виды кабельных линий, нумерация листов.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Slt.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Font = System.Drawing.Font;
using FlowDirection = System.Windows.Forms.FlowDirection;

[assembly: CommandClass(typeof(Slt.Acad.EditorCommands))]

namespace Slt.Acad
{
    static class Ui2
    {
        public static Form Window(string title, int w, int h) => new Form
        {
            Text = title, Width = w, Height = h, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, Font = new Font("Segoe UI", 9f),
        };

        public static FlowLayoutPanel Buttons(Form f, out Button ok, params Button[] extra)
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
            ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Width = 90 };
            bar.Controls.Add(cancel); bar.Controls.Add(ok);
            foreach (var b in extra) bar.Controls.Add(b);
            f.CancelButton = cancel;
            return bar;
        }

        /// <summary>Ctrl+V из Excel: строки через перевод строки, ячейки через табуляцию — с текущей ячейки, новые строки добавляются.</summary>
        public static void EnablePaste(DataGridView g, int firstTextColumn)
        {
            g.KeyDown += (s, e) =>
            {
                if (!(e.Control && e.KeyCode == Keys.V)) return;
                var text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text)) return;
                e.Handled = true;
                var rows = text.Replace("\r", "").TrimEnd('\n').Split('\n');
                int r0 = g.CurrentCell?.RowIndex ?? 0, c0 = Math.Max(firstTextColumn, g.CurrentCell?.ColumnIndex ?? firstTextColumn);
                for (int i = 0; i < rows.Length; i++)
                {
                    while (r0 + i >= g.Rows.Count - (g.AllowUserToAddRows ? 1 : 0)) g.Rows.Add();
                    var cells = rows[i].Split('\t');
                    for (int j = 0; j < cells.Length && c0 + j < g.Columns.Count; j++)
                    {
                        var col = g.Columns[c0 + j];
                        if (col is DataGridViewCheckBoxColumn) continue;
                        g.Rows[r0 + i].Cells[c0 + j].Value = cells[j].Trim().Trim('"');
                    }
                }
            };
        }
    }

    /// <summary>Строки таблицы: ручной ввод, вставка из Excel, заполнение из чертежа.</summary>
    static class TableDialog
    {
        static string[] Headers(Smart t) => t is Spec ? Spec.Headers : t is Journal ? Journal.Headers : new[] { "Поз. обозначение", "Наименование", "Кол-во" };

        public static bool Edit(Smart t, BlockReference br)
        {
            var heads = Headers(t);
            var rows = TableCommands.RowsOf(t);
            bool auto = t is PosList l ? l.Auto : t is Journal j ? j.Auto : ((Spec)t).Auto;
            string caption = t is PosList l2 ? l2.Caption : t is Journal j2 ? j2.Caption : ((Spec)t).Caption;
            using (var form = Ui2.Window(t.Title, 1150, 620))
            {
                var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 28, SelectionMode = DataGridViewSelectionMode.CellSelect };
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Раздел", Width = 52, ToolTipText = "Строка — заголовок раздела (пишется во второй графе с подчёркиванием)" });
                for (int c = 0; c < heads.Length; c++)
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = heads[c].Replace("-\n", "").Replace("\n", " "), Width = c == 1 ? 330 : t is Spec && c == 2 ? 200 : 110 });
                void Load(List<string[]> rs)
                {
                    grid.Rows.Clear();
                    foreach (var r in rs)
                    {
                        var vals = new List<object> { r.Length == 1 };
                        for (int c = 0; c < heads.Length; c++) vals.Add(r.Length == 1 ? (c == 1 ? r[0] : "") : c < r.Length ? r[c] : "");
                        grid.Rows.Add(vals.ToArray());
                    }
                }
                Load(rows);
                Ui2.EnablePaste(grid, 1);

                var top = new Panel { Dock = DockStyle.Top, Height = 64 };
                var autoBox = new CheckBox { Text = "Заполнять автоматически по объектам СЛТ этого листа (при «Обновить таблицы» ручные правки заменяются)", Checked = auto, AutoSize = true, Left = 8, Top = 8 };
                var capLabel = new Label { Text = "Заголовок:", Left = 8, Top = 38, AutoSize = true };
                var capBox = new TextBox { Text = caption, Left = 80, Top = 35, Width = 500 };
                var hint = new Label { Text = "Ctrl+V — вставка строк из Excel. Del — удалить выделенные строки.", Left = 600, Top = 38, AutoSize = true, ForeColor = System.Drawing.Color.DimGray };
                top.Controls.Add(autoBox); top.Controls.Add(capLabel); top.Controls.Add(capBox); top.Controls.Add(hint);

                var fill = new Button { Text = "Заполнить из чертежа", Width = 160 };
                fill.Click += (s, e) =>
                {
                    var rs = TableCommands.Refill(t, br?.OwnerId ?? ObjectId.Null, br?.Position ?? new Point3d(double.NaN, 0, 0));
                    if (rs.Count == 0) { MessageBox.Show(form, "На листе нет приборов, символов или кабелей СЛТ с заполненными данными.", t.Title); return; }
                    Load(rs);
                };
                var clear = new Button { Text = "Очистить", Width = 90 };
                clear.Click += (s, e) => grid.Rows.Clear();
                var bar = Ui2.Buttons(form, out var ok, fill, clear);
                form.Controls.Add(grid); form.Controls.Add(top); form.Controls.Add(bar);
                // Ручная правка строк выключает автозаполнение, чтобы правки не потерялись.
                grid.CellValueChanged += (s, e) => { if (e.RowIndex >= 0) autoBox.Checked = false; };
                grid.UserDeletedRow += (s, e) => autoBox.Checked = false;
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                grid.EndEdit();

                var res = new List<string[]>();
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (r.IsNewRow) continue;
                    var cells = Enumerable.Range(0, heads.Length).Select(c => Convert.ToString(r.Cells[c + 1].Value) ?? "").ToArray();
                    if (cells.All(string.IsNullOrWhiteSpace)) continue;
                    if (r.Cells[0].Value is bool b && b) res.Add(new[] { cells.Length > 1 && cells[1] != "" ? cells[1] : cells.First(x => x != "") });
                    else res.Add(cells);
                }
                switch (t)
                {
                    case PosList pl: pl.Rows = res; pl.Auto = autoBox.Checked; pl.Caption = capBox.Text; break;
                    case Journal jr: jr.Rows = res; jr.Auto = autoBox.Checked; jr.Caption = capBox.Text; break;
                    case Spec sp: sp.Rows = res; sp.Auto = autoBox.Checked; sp.Caption = capBox.Text; break;
                }
                return true;
            }
        }
    }

    public static class UserLines
    {
        public static string FilePath => Path.Combine(UserLibrary.Folder, "lines.txt");

        public static List<string> Load()
        {
            var errors = new List<string>();
            var list = LineKinds.Parse(LineKinds.DefaultText(), "встроенные", errors);
            if (File.Exists(FilePath)) LineKinds.Parse(File.ReadAllText(FilePath), "пользовательские", errors, list);
            LineKinds.Current = list;
            return errors;
        }

        /// <summary>Сохранить виды: заголовки из окна, знаки — как были (свои знаки правятся в файле).</summary>
        public static void Save(List<LineKind> kinds)
        {
            Directory.CreateDirectory(UserLibrary.Folder);
            var sb = new StringBuilder();
            sb.AppendLine("# Виды кабельных линий «СЛТ Графика» — пользовательские. Формат и примеры — в lines-builtin.txt рядом.");
            sb.AppendLine("# = id | Наименование | цвет ACI | t — толстая | штрихи мм | шаг знака мм; ниже — знак командами L, P, C, A, F, D, T (мм; x — вдоль линии, y — поперёк).");
            sb.AppendLine();
            var raw = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
            foreach (var k in kinds)
            {
                sb.AppendLine(LineKinds.Header(k));
                foreach (var line in MarkLines(k.Id, raw) ?? MarkLines(k.Id, LineKinds.DefaultText()) ?? new List<string>()) sb.AppendLine(line);
                sb.AppendLine();
            }
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(UserLibrary.Folder, "lines-builtin.txt"), LineKinds.DefaultText(), new UTF8Encoding(true));
        }

        /// <summary>Строки знака вида id из текста (между его «=» и следующим «=»); null — вида нет.</summary>
        static List<string> MarkLines(string id, string text)
        {
            List<string> res = null;
            foreach (var l in text.Replace("\r", "").Split('\n'))
            {
                var t = l.Trim();
                if (t.StartsWith("="))
                {
                    if (res != null) break;
                    if (t.Substring(1).Split('|')[0].Trim() == id) res = new List<string>();
                    continue;
                }
                if (res != null && t != "" && !t.StartsWith("#")) res.Add(t);
            }
            return res;
        }
    }

    public class EditorCommands
    {
        static Document Doc => AcApp.DocumentManager.MdiActiveDocument;
        static Editor Ed => Doc.Editor;

        /// <summary>Виды кабельных линий: наименование, цвет, толщина, штрихи, шаг знака; знаки — в файле.</summary>
        [CommandMethod("SLT_LINES", CommandFlags.Modal)]
        public void LinesCmd()
        {
            using (var form = Ui2.Window("Виды кабельных линий", 980, 480))
            {
                var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 28 };
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "id", Width = 80, ToolTipText = "Короткое имя латиницей; по нему кабели знают свой вид" });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Наименование (в легенде)", Width = 300 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Цвет ACI", Width = 70, ToolTipText = "0 — по слою; 1 — красный, 3 — зелёный, 4 — голубой, 5 — синий, 8 — серый" });
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Толстая", Width = 60 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Штрихи, мм", Width = 110, ToolTipText = "«штрих пробел …», например «8 2» или «12 2 1 2»; пусто — сплошная" });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шаг знака, мм", Width = 100, ToolTipText = "Через сколько мм листа повторяется знак способа прокладки; 0 — без знака" });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Разрыв, мм", Width = 80, ToolTipText = "Линия прерывается под знаком на эту длину (излом трубы); 0 — сплошная под знаком" });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Знак", Width = 120, ReadOnly = true, ToolTipText = "Рисунок знака — в файле, кнопка «Знаки в файле…»" });
                foreach (var k in LineKinds.All)
                    grid.Rows.Add(k.Id, k.Name, k.Color.ToString(CultureInfo.InvariantCulture), k.Weight == Weight.Thick,
                        string.Join(" ", k.Dash.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture))), k.Step.ToString("0.##", CultureInfo.InvariantCulture),
                        k.Gap.ToString("0.##", CultureInfo.InvariantCulture), k.Mark.Count == 0 ? "—" : k.Mark.Count + " элем.");
                var hint = new Label
                {
                    Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0),
                    Text = "Строка — вид кабельной линии (способ прокладки). Знак повторяется вдоль линии с шагом. Новый вид без знака — простая линия; свой знак — в файле (кнопка ниже), командами как в библиотеке обозначений.",
                };
                var file = new Button { Text = "Знаки в файле…", Width = 130 };
                var reset = new Button { Text = "Встроенные", Width = 100 };
                bool resetAll = false, fileOpened = false;
                reset.Click += (s, e) =>
                {
                    if (MessageBox.Show(form, "Вернуть встроенные виды линий? Свои виды и правки будут удалены.", "Виды линий", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    resetAll = true; form.DialogResult = DialogResult.OK; form.Close();
                };
                file.Click += (s, e) =>
                {
                    Save(grid); fileOpened = true;
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + UserLines.FilePath + "\"") { UseShellExecute = true }); } catch { }
                    MessageBox.Show(form, "Файл открыт в Блокноте. После правки сохраните его и нажмите здесь ОК — виды перечитаются.", "Знаки линий");
                };
                var bar = Ui2.Buttons(form, out var ok, file, reset);
                form.Controls.Add(grid); form.Controls.Add(hint); form.Controls.Add(bar);
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return;
                if (resetAll) { try { File.Delete(UserLines.FilePath); } catch { } }
                else if (!fileOpened) Save(grid); // после правки в Блокноте главный — файл
                var errors = UserLines.Load();
                foreach (var er in errors) Ed.WriteMessage("\n" + er);
                Ed.WriteMessage($"\nВидов линий: {LineKinds.All.Count}. Кабели перестраиваются…");
                Commands.Send("_SLT_REBUILD ");
            }
        }

        static void Save(DataGridView grid)
        {
            grid.EndEdit();
            var kinds = new List<LineKind>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                var id = (Convert.ToString(r.Cells[0].Value) ?? "").Trim();
                var name = (Convert.ToString(r.Cells[1].Value) ?? "").Trim();
                if (id == "" && name == "") continue;
                if (id == "") id = "l" + (kinds.Count + 1);
                double Num(object v) => double.TryParse((Convert.ToString(v) ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
                var dash = (Convert.ToString(r.Cells[4].Value) ?? "").Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => double.TryParse(x.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).ToArray();
                kinds.Add(new LineKind
                {
                    Id = id.Replace("|", ""), Name = name.Replace("|", "/"), Color = (int)Num(r.Cells[2].Value),
                    Weight = r.Cells[3].Value is bool b && b ? Weight.Thick : Weight.Thin, Dash = dash, Step = Math.Max(0, Num(r.Cells[5].Value)), Gap = Math.Max(0, Num(r.Cells[6].Value)),
                });
            }
            if (kinds.Count > 0) UserLines.Save(kinds);
        }

        /// <summary>Нумерация листов: порядок, начальный номер, «Листов» (в т. ч. с листами других файлов), наименования.</summary>
        [CommandMethod("SLT_SHEETS", CommandFlags.Modal)]
        public void SheetsCmd()
        {
            var db = Doc.Database;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var refs = SmartRef.All(tr, db, "format");
                if (refs.Count == 0)
                {
                    MessageBox.Show("В чертеже нет форматов СЛТ. Вставьте рамку кнопкой «Формат» — тогда листы нумеруются здесь.", "Нумерация листов");
                    return;
                }
                var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                string Where(BlockReference b, out int tab)
                {
                    tab = int.MaxValue;
                    if (b.OwnerId == ms) return $"Модель ({b.Position.X:0}; {b.Position.Y:0})";
                    var btr = (BlockTableRecord)tr.GetObject(b.OwnerId, OpenMode.ForRead);
                    var lay = (Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead);
                    tab = lay.TabOrder;
                    return "Лист «" + lay.LayoutName + "»";
                }
                var items = refs.Select(b => { var w = Where(b, out var tab); return new { b, o = (SheetFormat)SmartRef.Load(b), tab, w }; }).ToList();
                double rowTol = items.Max(i => i.o.Paper[1] * i.o.K) / 2;
                items = items.OrderBy(i => i.tab).ThenBy(i => -Math.Round(i.b.Position.Y / rowTol)).ThenBy(i => i.b.Position.X).ToList();

                using (var form = Ui2.Window("Нумерация листов", 900, 520))
                {
                    var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersWidth = 28 };
                    grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Авто", Width = 50, ToolTipText = "Нумеровать этот лист" });
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Где", Width = 200, ReadOnly = true });
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Штамп", Width = 70, ReadOnly = true });
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Наименование листа", Width = 360 });
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Лист", Width = 60 });
                    foreach (var i in items)
                        grid.Rows.Add(i.o.AutoNumber, i.w, i.o.Stamp == StampForm.Form3 ? "форма 3" : i.o.Stamp == StampForm.Form6 ? "форма 6" : "—", i.o.Field("title"), i.o.Field("sheet"));
                    var top = new Panel { Dock = DockStyle.Top, Height = 68 };
                    var l1 = new Label { Text = "Начать с листа:", Left = 8, Top = 12, AutoSize = true };
                    var start = new NumericUpDown { Left = 120, Top = 9, Width = 70, Minimum = 1, Maximum = 9999, Value = 1 };
                    var l2 = new Label { Text = "«Листов» в форме 3:", Left = 210, Top = 12, AutoSize = true };
                    var total = new TextBox { Left = 340, Top = 9, Width = 70, Text = "" };
                    var l3 = new Label { Text = "пусто — по числу листов здесь (с начальным номером); число — если комплект продолжается в других файлах", Left = 420, Top = 12, Width = 460, Height = 32, ForeColor = System.Drawing.Color.DimGray };
                    var up = new Button { Text = "▲ выше", Left = 8, Top = 38, Width = 80 };
                    var down = new Button { Text = "▼ ниже", Left = 92, Top = 38, Width = 80 };
                    var l4 = new Label { Text = "Порядок: листы — по вкладкам, в модели — рядами сверху вниз, слева направо. Строки можно переставить.", Left = 180, Top = 42, AutoSize = true, ForeColor = System.Drawing.Color.DimGray };
                    void Move(int d)
                    {
                        var r = grid.CurrentRow; if (r == null) return;
                        int i = r.Index, j = i + d; if (j < 0 || j >= grid.Rows.Count) return;
                        var row = grid.Rows[i]; grid.Rows.RemoveAt(i); grid.Rows.Insert(j, row);
                        var it = items[i]; items.RemoveAt(i); items.Insert(j, it);
                        grid.CurrentCell = grid.Rows[j].Cells[3];
                    }
                    up.Click += (s, e) => Move(-1); down.Click += (s, e) => Move(1);
                    top.Controls.AddRange(new Control[] { l1, start, l2, total, l3, up, down, l4 });
                    var bar = Ui2.Buttons(form, out var ok);
                    ok.Text = "Нумеровать";
                    form.Controls.Add(grid); form.Controls.Add(top); form.Controls.Add(bar);
                    if (AcApp.ShowModalDialog(form) != DialogResult.OK) return;
                    grid.EndEdit();

                    int n = (int)start.Value, numbered = 0;
                    var autoCount = Enumerable.Range(0, items.Count).Count(i => grid.Rows[i].Cells[0].Value is bool b && b);
                    var sheets = total.Text.Trim() != "" ? total.Text.Trim() : (autoCount + (int)start.Value - 1).ToString(CultureInfo.InvariantCulture);
                    for (int i = 0; i < items.Count; i++)
                    {
                        var o = items[i].o; var row = grid.Rows[i];
                        o.AutoNumber = row.Cells[0].Value is bool b && b;
                        o.Fields["title"] = Convert.ToString(row.Cells[3].Value) ?? "";
                        if (o.AutoNumber)
                        {
                            o.Fields["sheet"] = (n++).ToString(CultureInfo.InvariantCulture);
                            if (o.Stamp == StampForm.Form3) o.Fields["sheets"] = sheets;
                            numbered++;
                        }
                        else o.Fields["sheet"] = Convert.ToString(row.Cells[4].Value) ?? "";
                        SmartRef.Save(items[i].b, o);
                        SmartRef.Rebuild(tr, items[i].b);
                    }
                    tr.Commit();
                    Ed.WriteMessage($"\nПронумеровано листов: {numbered} (с {start.Value}), «Листов»: {sheets}.");
                }
            }
        }
    }
}
