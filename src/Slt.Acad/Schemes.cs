// Схемы автоматизации: приборы КИП, исполнительные механизмы, символы библиотеки, перечень элементов, своя библиотека.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Slt.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Shape = Slt.Core.Shape;
using Symbol = Slt.Core.Symbol;
using Font = System.Drawing.Font;
using FlowDirection = System.Windows.Forms.FlowDirection;

[assembly: CommandClass(typeof(Slt.Acad.SchemeCommands))]

namespace Slt.Acad
{
    public static class UserLibrary
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SltGrafika");
        public static string FilePath => Path.Combine(Folder, "library.txt");

        /// <summary>Встроенная библиотека + пользовательская (%APPDATA%\SltGrafika\library.txt).</summary>
        public static void Load()
        {
            var lib = Library.Default();
            if (File.Exists(FilePath)) lib.Parse(File.ReadAllText(FilePath), "пользовательская");
            Library.Current = lib;
        }
    }

    public class SchemeCommands
    {
        static Document Doc => AcApp.DocumentManager.MdiActiveDocument;
        static Editor Ed => Doc.Editor;
        static Instrument lastKip = new Instrument { Pos = "TE1", Ref = "1" };
        static string lastSym = "vent.damper", lastSymName = "";

        static double K(Database db) => Settings.Read(db).KFor(db, db.CurrentSpaceId);

        static ObjectId Insert(Smart o, Point3d at)
        {
            var db = Doc.Database;
            using (Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = SmartRef.Create(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), o, at);
                tr.Commit();
                return br.ObjectId;
            }
        }

        /// <summary>Следующая свободная позиция с теми же буквами: TE3 → TE4 (по всему чертежу).</summary>
        static string NextPos(Database db, string pos)
        {
            var m = Regex.Match(pos ?? "", @"^(.*?)(\d+)$");
            var prefix = m.Success ? m.Groups[1].Value : pos ?? "";
            int max = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                foreach (var br in SmartRef.All(tr, db))
                {
                    var d = Data.Read(br);
                    if (!d.TryGetValue("pos", out var p)) continue;
                    var pm = Regex.Match(p, @"^(.*?)(\d+)$");
                    if (pm.Success && pm.Groups[1].Value == prefix) max = Math.Max(max, int.Parse(pm.Groups[2].Value));
                }
            return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        static string Inc(string s) { var m = Regex.Match(s ?? "", @"^(.*?)(\d+)$"); return m.Success ? m.Groups[1].Value + (int.Parse(m.Groups[2].Value) + 1) : s; }

        /// <summary>Точка с резиновой нитью от центра; update — живой просмотр. null — Enter (пропуск) или отмена (cancelled).</summary>
        static Point3d? Pick(Smart o, Point3d center, string prompt, Action<P2> update, out bool cancelled)
        {
            var jig = new SmartJig(o, prompt + " <нет>:", center, (x, p) => update(new P2(p.X - center.X, p.Y - center.Y)), center);
            var r = Ed.Drag(jig);
            cancelled = r.Status == PromptStatus.Cancel;
            return r.Status == PromptStatus.OK ? jig.Last : (Point3d?)null;
        }

        // ---------------- КИП ----------------

        [CommandMethod("SLT_KIP", CommandFlags.Modal)]
        public void KipCmd() => PlaceKip(false);

        [CommandMethod("SLT_IM", CommandFlags.Modal)]
        public void ImCmd() => PlaceKip(true);

        void PlaceKip(bool actuator)
        {
            var db = Doc.Database; var k = K(db);
            Instrument proto;
            if (actuator) { proto = Instrument.Actuator(NextPos(db, "Y")); proto.Name = "Привод воздушной заслонки"; }
            else
            {
                proto = (Instrument)Smart.From(lastKip.SaveAll());
                proto.Pos = NextPos(db, proto.Pos);
            }
            proto.K = k;
            if (!Dialogs.Edit(proto)) return;
            while (true)
            {
                var o = (Instrument)Smart.From(proto.SaveAll());
                var tapOn = o.HasTap; var refOn = o.HasRef;
                o.HasTap = false; o.HasRef = false;
                SmartJig cj = null;
                cj = new SmartJig(o, $"Центр обозначения {o.Pos}:", Point3d.Origin, (x, p) => cj.Origin = p);
                var c = cj.Run(Ed);
                if (c == null) return;
                bool cancel;
                if (tapOn)
                {
                    o.HasTap = true;
                    var t = Pick(o, c.Value, actuator ? "Точка на регулирующем органе (заслонке, клапане)" : "Точка отбора на технологической линии", p => { o.Tap = p; if (actuator) o.PosAt = p + p.Unit * (3.5 * k); }, out cancel);
                    if (cancel) return;
                    if (t == null) o.HasTap = false;
                }
                if (refOn)
                {
                    o.HasRef = true;
                    var r = Pick(o, c.Value, $"Конец линии ссылки «{o.Ref}»", p => o.RefAt = p, out cancel);
                    if (cancel) return;
                    if (r == null) o.HasRef = false;
                }
                if (actuator && !o.HasTap) o.PosAt = new P2(-o.Diameter / 2 * k - 1.5 * k, o.Diameter / 2 * k + 1 * k);
                Insert(o, c.Value);
                // Следующий — с номером на 1 больше, та же ссылка +1 (если число).
                proto.Pos = Inc(o.Pos); proto.Ref = Inc(o.Ref);
                proto.Tap = o.Tap; proto.RefAt = o.RefAt;
                if (!actuator) lastKip = (Instrument)Smart.From(proto.SaveAll());
            }
        }

        // ---------------- символы ----------------

        [CommandMethod("SLT_SYM", CommandFlags.Modal)]
        public void SymCmd()
        {
            var db = Doc.Database; var k = K(db);
            var pick = LibraryWindow.Choose(lastSym, lastSymName);
            if (pick == null) return;
            lastSym = pick.Def.Id; lastSymName = pick.Name;
            var pos = pick.Pos;
            double angle = 0;
            while (true)
            {
                var o = new Symbol { K = k, Sym = pick.Def.Id, Pos = pos, Name = pick.Name, Angle = angle };
                o.PosAt = o.DefaultPosAt();
                SmartJig jig = null;
                jig = new SmartJig(o, $"Точка вставки «{pick.Def.Name}»{(pos != "" ? " " + pos : "")}:", Point3d.Origin, (x, p) => jig.Origin = p);
                var at = jig.Run(Ed);
                if (at == null) return;
                var ao = new PromptAngleOptions("\nПоворот <" + Math.Round(angle * 180 / Math.PI) + "°>:") { AllowNone = true, UseBasePoint = true, BasePoint = at.Value.TransformBy(Ed.CurrentUserCoordinateSystem.Inverse()), UseDashedLine = true };
                var ar = Ed.GetAngle(ao);
                if (ar.Status == PromptStatus.Cancel) return;
                if (ar.Status == PromptStatus.OK) angle = ar.Value;
                o.Angle = angle;
                Insert(o, at.Value);
                pos = pos == "" ? "" : Inc(pos);
            }
        }

        // ---------------- перечень ----------------

        [CommandMethod("SLT_LIST", CommandFlags.Modal)]
        public void ListCmd() => TableCommands.Place(new PosList(), "Левый верхний угол перечня:");

        [CommandMethod("SLT_LISTUPDATE", CommandFlags.Modal)]
        public void ListUpdateCmd() => Ed.WriteMessage("\nОбновлено таблиц: " + TableCommands.UpdateAll(Doc.Database, ObjectId.Null) + ".");

        // ---------------- своя библиотека ----------------

        [CommandMethod("SLT_LIBRARY", CommandFlags.Modal)]
        public void LibraryCmd()
        {
            var kw = new PromptKeywordOptions("\nБиблиотека обозначений [Открыть/Перезагрузить]", "Открыть Перезагрузить") { AllowNone = true };
            kw.Keywords.Default = "Открыть";
            var r = Ed.GetKeywords(kw);
            if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return;
            if (r.StringResult == "Перезагрузить")
            {
                UserLibrary.Load();
                foreach (var e in Library.Current.Errors) Ed.WriteMessage("\n" + e);
                Ed.WriteMessage($"\nСимволов: {Library.Current.Symbols.Count}, ошибок: {Library.Current.Errors.Count}.");
                Send("_SLT_REBUILD ");
                return;
            }
            Directory.CreateDirectory(UserLibrary.Folder);
            if (!File.Exists(UserLibrary.FilePath))
            {
                var text = "# Пользовательская библиотека «СЛТ Графика». Символ с тем же id, что во встроенной, заменяет её символ.\n" +
                           "# После правки: SLT_LIBRARY → Перезагрузить. Встроенная библиотека для образца — library-builtin.txt рядом.\n" +
                           "# Формат: [Система / Группа], = id | Наименование | префикс; L/P/C/A/F/D/T — см. README.\n\n" +
                           "[Мои обозначения]\n= my.example | Пример: датчик в квадрате | ДТ\nP -3 -3 3 -3 3 3 -3 3 c t\nT 0 0 2.5 cm \"t°\"\n";
                File.WriteAllText(UserLibrary.FilePath, text, new System.Text.UTF8Encoding(true));
            }
            File.WriteAllText(Path.Combine(UserLibrary.Folder, "library-builtin.txt"), Library.DefaultText(), new System.Text.UTF8Encoding(true));
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + UserLibrary.FilePath + "\"") { UseShellExecute = true }); }
            catch (System.Exception ex) { Ed.WriteMessage("\n" + ex.Message); }
            Ed.WriteMessage("\nФайл: " + UserLibrary.FilePath + ". После правки — SLT_LIBRARY → Перезагрузить.");
        }

        static void Send(string c) => Commands.Send(c);
    }

    /// <summary>Окно выбора символа: дерево разделов, просмотр, позиция и наименование.</summary>
    sealed class LibraryWindow : Form
    {
        public sealed class Result { public SymbolDef Def; public string Pos, Name; }

        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, Font = new Font("Segoe UI", 9f) };
        readonly Panel view = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly TextBox pos = new TextBox { Dock = DockStyle.Top }, name = new TextBox { Dock = DockStyle.Top };
        readonly TextBox find = new TextBox { Dock = DockStyle.Top };
        SymbolDef cur;

        LibraryWindow(string selectId, string lastName)
        {
            Text = "Условные обозначения"; Width = 760; Height = 520; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            MinimizeBox = false; Font = new Font("Segoe UI", 9f);
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(tree); left.Controls.Add(find); left.Controls.Add(new Label { Text = "Поиск:", Dock = DockStyle.Top, Height = 18 });
            split.Panel1.Controls.Add(left);
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var fields = new Panel { Dock = DockStyle.Bottom, Height = 110 };
            fields.Controls.Add(name); fields.Controls.Add(new Label { Text = "Наименование для перечня (пусто — без строки в перечне, если нет позиции):", Dock = DockStyle.Top, Height = 18 });
            fields.Controls.Add(pos); fields.Controls.Add(new Label { Text = "Позиция (пусто — без подписи; дальше номер растёт сам):", Dock = DockStyle.Top, Height = 18 });
            right.Controls.Add(view); right.Controls.Add(fields);
            split.Panel2.Controls.Add(right);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
            var ok = new Button { Text = "Вставить", DialogResult = DialogResult.OK, Width = 90 };
            bar.Controls.Add(cancel); bar.Controls.Add(ok);
            Controls.Add(split); Controls.Add(bar);
            AcceptButton = ok; CancelButton = cancel;
            name.Text = lastName ?? "";
            view.Paint += (s, e) => Draw(e.Graphics, view.ClientRectangle, cur);
            view.Resize += (s, e) => view.Invalidate();
            tree.AfterSelect += (s, e) => Select(e.Node?.Tag as SymbolDef, true);
            tree.NodeMouseDoubleClick += (s, e) => { if (e.Node?.Tag is SymbolDef) { DialogResult = DialogResult.OK; Close(); } };
            find.TextChanged += (s, e) => Fill(find.Text, cur?.Id);
            Fill("", selectId);
        }

        void Fill(string filter, string selectId)
        {
            tree.BeginUpdate(); tree.Nodes.Clear();
            TreeNode sel = null;
            foreach (var d in Library.Current.Symbols)
            {
                if (filter != "" && d.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && d.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var nodes = tree.Nodes;
                foreach (var part in d.Group.Split('/').Select(x => x.Trim()).Where(x => x != ""))
                {
                    var n = nodes.Cast<TreeNode>().FirstOrDefault(x => x.Text == part && x.Tag == null) ?? nodes.Add(part);
                    nodes = n.Nodes;
                }
                var leaf = nodes.Add(d.Name); leaf.Tag = d; leaf.ToolTipText = d.Id + (d.Source != "встроенная" ? " (" + d.Source + ")" : "");
                if (d.Id == selectId) sel = leaf;
            }
            tree.ExpandAll(); tree.EndUpdate();
            if (sel != null) tree.SelectedNode = sel;
            else if (filter != "") tree.SelectedNode = Leaves(tree.Nodes).FirstOrDefault();
        }

        static IEnumerable<TreeNode> Leaves(TreeNodeCollection c) { foreach (TreeNode n in c) { if (n.Tag != null) yield return n; foreach (var x in Leaves(n.Nodes)) yield return x; } }

        void Select(SymbolDef d, bool resetPos)
        {
            if (d == null) return;
            var changed = cur != d; cur = d;
            if (resetPos && changed) pos.Text = d.Prefix == "" ? "" : NextFor(d.Prefix);
            view.Invalidate();
        }

        static string NextFor(string prefix)
        {
            var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
            if (db == null) return prefix + "1";
            int max = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                foreach (var br in SmartRef.All(tr, db))
                    if (Data.Read(br).TryGetValue("pos", out var p))
                    {
                        var m = Regex.Match(p, @"^(.*?)(\d+)$");
                        if (m.Success && m.Groups[1].Value == prefix) max = Math.Max(max, int.Parse(m.Groups[2].Value));
                    }
            return prefix + (max + 1);
        }

        /// <summary>Просмотр символа (тот же набор примитивов, что в чертеже).</summary>
        public static void Draw(Graphics g, Rectangle rc, SymbolDef d)
        {
            g.Clear(Color.White);
            if (d == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var b = d.Bounds(); double w = Math.Max(b[2] - b[0], 4), h = Math.Max(b[3] - b[1], 4);
            var sc = Math.Min((rc.Width - 40) / w, (rc.Height - 40) / h); sc = Math.Min(sc, 25);
            double cx = (b[0] + b[2]) / 2, cy = (b[1] + b[3]) / 2;
            PointF P(P2 p) => new PointF((float)(rc.Width / 2 + (p.X - cx) * sc), (float)(rc.Height / 2 - (p.Y - cy) * sc));
            var sk = new Sketch(1); d.Draw(sk, 0, new Dictionary<string, string> { { "pos", d.Prefix + "1" } });
            using (var thin = new Pen(Color.Black, (float)Math.Max(1, 0.25 * sc)))
            using (var thick = new Pen(Color.Black, (float)Math.Max(2, 0.5 * sc)))
                foreach (var s in sk.Shapes)
                {
                    var pen = s.Weight == Weight.Thick ? thick : thin;
                    switch (s.Kind)
                    {
                        case ShapeKind.Line: g.DrawLine(pen, P(s.Pts[0]), P(s.Pts[1])); break;
                        case ShapeKind.Polyline: if (s.Closed) g.DrawPolygon(pen, s.Pts.Select(P).ToArray()); else g.DrawLines(pen, s.Pts.Select(P).ToArray()); break;
                        case ShapeKind.Fill: g.FillPolygon(Brushes.Black, s.Pts.Select(P).ToArray()); break;
                        case ShapeKind.Circle: { var c = P(s.Pts[0]); var r = (float)(s.R * sc); g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r); break; }
                        case ShapeKind.Arc:
                            {
                                var c = P(s.Pts[0]); var r = (float)(s.R * sc);
                                var sweep = ((s.A1 - s.A0) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
                                g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, (float)(-s.A0 * 180 / Math.PI), (float)(-sweep * 180 / Math.PI));
                                break;
                            }
                        case ShapeKind.Text:
                            using (var f = new Font("ISOCPEUR", (float)Math.Max(6, s.H * sc), FontStyle.Italic, GraphicsUnit.Pixel))
                            {
                                var fmt = new StringFormat
                                {
                                    Alignment = s.Ha == HAlign.Left ? StringAlignment.Near : s.Ha == HAlign.Center ? StringAlignment.Center : StringAlignment.Far,
                                    LineAlignment = s.Va == VAlign.Top ? StringAlignment.Near : s.Va == VAlign.Middle ? StringAlignment.Center : StringAlignment.Far,
                                };
                                g.DrawString(s.Text, f, Brushes.Black, P(s.Pts[0]), fmt);
                            }
                            break;
                    }
                }
            using (var f = new Font("Segoe UI", 8f)) g.DrawString(d.Id, f, Brushes.Gray, 4, rc.Height - 18);
        }

        public static Result Choose(string selectId, string lastName)
        {
            using (var w = new LibraryWindow(selectId, lastName))
            {
                if (AcApp.ShowModalDialog(w) != DialogResult.OK || w.cur == null) return null;
                return new Result { Def = w.cur, Pos = w.pos.Text.Trim(), Name = w.name.Text.Trim() };
            }
        }
    }
}
