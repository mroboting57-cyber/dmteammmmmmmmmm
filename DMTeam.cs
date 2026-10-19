// DMTeam.cs — полный файл
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace DMTeam
{
    public enum LogLevel { Info, Ok, Err, Warn }

    // ---------------- Тема ----------------
    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(12, 13, 14);
        public static readonly Color Card      = Color.FromArgb(27, 28, 30);
        public static readonly Color CardHover = Color.FromArgb(38, 39, 41);
        public static readonly Color CardDown  = Color.FromArgb(32, 33, 35);
        public static Color Accent = Color.FromArgb(150, 157, 165);
        public static readonly Color Text      = Color.FromArgb(232, 233, 238);
        public static readonly Color Sub       = Color.FromArgb(150, 153, 165);
        public static readonly Color Green     = Color.FromArgb(78, 194, 128);
        public static readonly Color Red       = Color.FromArgb(240, 95, 95);
        public static readonly Color Warn      = Color.FromArgb(230, 180, 80);
        public static readonly Color Border    = Color.FromArgb(54, 55, 58);

        public static readonly Font Ui    = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font Small = new Font("Segoe UI", 10f, FontStyle.Bold);
        public static readonly Font Bold  = new Font("Segoe UI", 12f, FontStyle.Bold);

        public static void LoadAccent()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\DMTeam"))
            {
                object value = key == null ? null : key.GetValue("AccentColor");
                if (value == null)
                {
                    using (RegistryKey legacyKey = Registry.CurrentUser.OpenSubKey(@"Software\WinTuner"))
                        value = legacyKey == null ? null : legacyKey.GetValue("AccentColor");
                }
                if (value == null) return;
                int argb;
                if (Int32.TryParse(value.ToString(), out argb))
                    Accent = Color.FromArgb(argb);
            }
        }

        public static void SaveAccent(Color color)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\DMTeam"))
            {
                if (key == null) throw new InvalidOperationException("Не удалось сохранить настройки интерфейса.");
                key.SetValue("AccentColor", color.ToArgb(), RegistryValueKind.DWord);
            }
            Accent = color;
            foreach (Form form in Application.OpenForms)
                form.Invalidate(true);
        }
    }

    // ---------------- Лог ----------------
    public interface ILog { void Log(string msg, LogLevel lvl); }

    public class StatusLog : ILog
    {
        private Label lbl;
        public StatusLog(Label l) { lbl = l; }

        public void Log(string msg, LogLevel lvl)
        {
            if (lbl == null || lbl.IsDisposed) { Application.DoEvents(); return; }
            lbl.Text = msg;
            lbl.ForeColor = lvl == LogLevel.Ok ? Theme.Green
                          : lvl == LogLevel.Warn ? Theme.Warn
                          : lvl == LogLevel.Err ? Theme.Red
                          : Theme.Sub;
            Application.DoEvents();
        }
    }

    // ---------------- UI помощники ----------------
    static class Ui
    {
        public static void StyleDarkList(ListView list)
        {
            list.OwnerDraw = true;
            list.GridLines = false;
            list.UseCompatibleStateImageBehavior = false;
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(27, 28, 30)))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Bold,
                    Rectangle.Inflate(e.Bounds, -10, 0), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (Pen line = new Pen(Color.FromArgb(49, 50, 53)))
                    e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e)
            {
                if (list.View != View.Details) e.DrawDefault = true;
            };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                Color rowColor = e.Item.Selected
                    ? Color.FromArgb(49, 54, 61)
                    : (e.ItemIndex % 2 == 0 ? Color.FromArgb(17, 18, 20) : Color.FromArgb(21, 22, 24));
                using (SolidBrush fill = new SolidBrush(rowColor))
                    e.Graphics.FillRectangle(fill, e.Bounds);

                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix;
                if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                else if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                else flags |= TextFormatFlags.Left;
                Rectangle textBounds = Rectangle.Inflate(e.Bounds, e.Header.TextAlign == HorizontalAlignment.Left ? -10 : -8, 0);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Small, textBounds, Theme.Text, flags);
                using (Pen line = new Pen(Color.FromArgb(38, 39, 42)))
                    e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            list.BackColor = Color.FromArgb(17, 18, 20);
            list.ForeColor = Theme.Text;
        }

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void SetRoundedWindow(Form form, int radius)
        {
            if (form.Width <= 0 || form.Height <= 0) return;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, form.Width, form.Height), radius))
                form.Region = new Region(path);
        }

        public static T Place<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            parent.Controls.Add(c);
            return c;
        }

        public static Button Btn(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.MouseOverBackColor = Theme.CardHover;
            b.FlatAppearance.MouseDownBackColor = Theme.CardDown;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
            b.Font = Theme.Ui;
            b.Cursor = Cursors.Hand;
            b.TabStop = true;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button AccentBtn(string text)
        {
            Button b = Btn(text);
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.FromArgb(10, 14, 22);
            b.Font = Theme.Bold;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(120, 180, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 130, 215);
            return b;
        }

        public static RButton CloseBtn()
        {
            RButton b = RButton.Secondary("×");
            b.Font = new Font("Segoe UI", 16f);
            b.ForeColor = Color.FromArgb(170, 174, 182);
            b.FillColor = Color.Transparent;
            b.HoverColor = Color.FromArgb(54, 38, 40);
            b.DownColor = Color.FromArgb(72, 42, 44);
            b.LineColor = Color.Transparent;
            return b;
        }
    }

    // ---------------- RButton ----------------
    public class RButton : Button
    {
        public Color FillColor = Theme.Card;
        public Color HoverColor = Theme.CardHover;
        public Color DownColor = Theme.CardDown;
        public Color LineColor = Theme.Border;
        public bool PrimaryStyle;
        private bool hover, down;

        public RButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Opaque, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
            TabStop = true;
            UseVisualStyleBackColor = false;
        }

        public static RButton Primary(string text)
        {
            RButton b = new RButton();
            b.Text = text;
            b.PrimaryStyle = true;
            b.LineColor = Theme.Accent;
            b.Font = Theme.Bold;
            return b;
        }

        public static RButton Secondary(string text)
        {
            RButton b = new RButton();
            b.Text = text;
            b.FillColor = Theme.Card;
            b.HoverColor = Theme.CardHover;
            b.DownColor = Theme.CardDown;
            b.LineColor = Theme.Border;
            b.ForeColor = Theme.Text;
            return b;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            Color fill = !Enabled ? Theme.Card
                       : down ? DownColor
                       : hover ? HoverColor
                       : FillColor;
            Color text = Enabled ? ForeColor : Theme.Sub;
            if (PrimaryStyle)
            {
                fill = !Enabled ? Theme.Card
                      : down ? Color.FromArgb(49, 51, 55)
                      : hover ? Color.FromArgb(43, 45, 49)
                      : Color.FromArgb(34, 36, 39);
                text = Enabled ? Theme.Text : Theme.Sub;
            }
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 9))
            using (SolidBrush br = new SolidBrush(fill))
            using (Pen p = new Pen(Focused || (hover && Enabled) || PrimaryStyle ? Theme.Accent : LineColor))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(r, -5, -4);
                ControlPaint.DrawFocusRectangle(g, focus, text, fill);
            }
        }
    }

    // ---------------- Системные операции ----------------
    static class Sys
    {
        public static RegistryKey Split(string full, out string sub)
        {
            int i = full.IndexOf('\\');
            string hive = i > 0 ? full.Substring(0, i) : full;
            sub = i > 0 ? full.Substring(i + 1) : "";
            switch (hive.ToUpperInvariant())
            {
                case "HKCU": case "HKEY_CURRENT_USER":  return Registry.CurrentUser;
                case "HKLM": case "HKEY_LOCAL_MACHINE": return Registry.LocalMachine;
                case "HKCR": case "HKEY_CLASSES_ROOT":  return Registry.ClassesRoot;
                case "HKU":  case "HKEY_USERS":         return Registry.Users;
            }
            return null;
        }

        public static void RegSet(string full, string name, object value, RegistryValueKind kind)
        {
            try
            {
                string sub;
                RegistryKey root = Split(full, out sub);
                if (root == null) return;
                using (RegistryKey k = root.CreateSubKey(sub))
                {
                    if (k != null) k.SetValue(name, value, kind);
                }
            }
            catch { }
        }

        public static void RegDword(string full, string name, int value)
        {
            RegSet(full, name, value, RegistryValueKind.DWord);
        }

        public static string Run(string file, string args) { return RunEnc(file, args, null); }

        public static int RunWithExitCode(string file, string args, out string output)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(866);
                psi.StandardErrorEncoding = Encoding.GetEncoding(866);
                using (Process process = Process.Start(psi))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(120000))
                    {
                        process.Kill();
                        output = "Команда превысила время ожидания.";
                        return -1;
                    }
                    output = (stdout + "\r\n" + stderr).Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = "Ошибка: " + ex.Message;
                return -1;
            }
        }

        public static string RunEnc(string file, string args, Encoding enc)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                if (enc != null) psi.StandardOutputEncoding = enc;
                Process p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                return (o + "\r\n" + e).Trim();
            }
            catch (Exception ex) { return "Ошибка: " + ex.Message; }
        }

        public static bool SetServiceStart(string name, int start)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, true))
                {
                    if (k == null) return false;
                    k.SetValue("Start", start, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { return false; }
        }

        public static void StopService(string name)
        {
            try
            {
                using (ServiceController sc = new ServiceController(name))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                    }
                }
            }
            catch { }
        }

        public static void StartService(string name)
        {
            try
            {
                using (ServiceController sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                }
            }
            catch { }
        }
    }

    // ---------------- Действия оптимизации ----------------
    static class Ops
    {
        public static readonly string[] TelemetryTasks = {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Autochk\Proxy",
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
            @"\Microsoft\Windows\Feedback\Siuf\DmClient",
            @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
            @"\Microsoft\Windows\Windows Error Reporting\QueueReporting"
        };

        public static readonly string[] BloatServices = {
            "DiagTrack", "dmwappushservice", "RetailDemo", "MapsBroker", "WMPNetworkSvc",
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc",
            "PhoneSvc", "Fax", "RemoteRegistry"
        };

        private static readonly string[] OptionalMicrosoftAppPackages = {
            "Microsoft.BingNews",
            "Microsoft.BingWeather",
            "Microsoft.GetHelp",
            "Microsoft.Getstarted",
            "Microsoft.MicrosoftSolitaireCollection",
            "Microsoft.WindowsFeedbackHub",
            "Microsoft.YourPhone",
            "MSTeams"
        };

        private static long DirSize(string path)
        {
            long s = 0;
            try
            {
                foreach (string f in Directory.GetFiles(path)) { try { s += new FileInfo(f).Length; } catch { } }
                foreach (string d in Directory.GetDirectories(path)) s += DirSize(d);
            }
            catch { }
            return s;
        }

        public static void CleanTemp(ILog log)
        {
            log.Log("Очистка временных файлов...", LogLevel.Info);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] paths = {
                Path.GetTempPath(),
                Path.Combine(win, "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache")
            };
            long total = 0;
            foreach (string p in paths)
            {
                if (!Directory.Exists(p)) continue;
                foreach (string f in Directory.GetFiles(p)) { try { long s = new FileInfo(f).Length; File.Delete(f); total += s; } catch { } }
                foreach (string d in Directory.GetDirectories(p)) { try { total += DirSize(d); Directory.Delete(d, true); } catch { } }
            }
            log.Log(string.Format("Временные файлы очищены (~ {0:N1} МБ)", total / 1048576.0), LogLevel.Ok);
        }

        public static void CleanRecycleBin(ILog log)
        {
            log.Log("Очистка корзины...", LogLevel.Info);
            Sys.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"");
            log.Log("Корзина очищена.", LogLevel.Ok);
        }

        public static void CleanDns(ILog log)
        {
            log.Log("Сброс DNS-кэша...", LogLevel.Info);
            Sys.Run("ipconfig", "/flushdns");
            log.Log("DNS-кэш очищен.", LogLevel.Ok);
        }

        public static void CleanWindowsUpdate(ILog log)
        {
            log.Log("Очистка кэша обновлений Windows...", LogLevel.Info);
            foreach (string s in new string[] { "wuauserv", "bits", "cryptsvc" }) Sys.StopService(s);
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
            if (Directory.Exists(dl))
            {
                foreach (string d in Directory.GetDirectories(dl)) { try { Directory.Delete(d, true); } catch { } }
                foreach (string f in Directory.GetFiles(dl)) { try { File.Delete(f); } catch { } }
            }
            Sys.StartService("wuauserv");
            Sys.StartService("bits");
            log.Log("Кэш обновлений очищен.", LogLevel.Ok);
        }

        private static void CleanFilesRecursive(string path, ref int removed, ref int skipped, ref long bytes)
        {
            if (!Directory.Exists(path)) return;
            string[] files;
            try { files = Directory.GetFiles(path); }
            catch (IOException) { skipped++; return; }
            catch (UnauthorizedAccessException) { skipped++; return; }

            foreach (string file in files)
            {
                try
                {
                    FileInfo info = new FileInfo(file);
                    long size = info.Length;
                    info.Delete();
                    removed++;
                    bytes += size;
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }

            string[] directories;
            try { directories = Directory.GetDirectories(path); }
            catch (IOException) { skipped++; return; }
            catch (UnauthorizedAccessException) { skipped++; return; }
            foreach (string directory in directories)
                CleanFilesRecursive(directory, ref removed, ref skipped, ref bytes);
        }

        private static void CleanCachePaths(ILog log, string title, string[] paths)
        {
            log.Log("Очистка: " + title + "...", LogLevel.Info);
            int removed = 0, skipped = 0;
            long bytes = 0;
            foreach (string path in paths)
                CleanFilesRecursive(path, ref removed, ref skipped, ref bytes);
            string result = String.Format("{0}: файлов удалено {1}, освобождено {2:N1} МБ.",
                title, removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        private static void CleanCachePattern(ILog log, string title, string directory, string pattern)
        {
            log.Log("Очистка: " + title + "...", LogLevel.Info);
            int removed = 0, skipped = 0;
            long bytes = 0;
            if (Directory.Exists(directory))
            {
                string[] files;
                try { files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
                catch (IOException) { files = new string[0]; skipped++; }
                catch (UnauthorizedAccessException) { files = new string[0]; skipped++; }
                foreach (string file in files)
                {
                    try
                    {
                        FileInfo info = new FileInfo(file);
                        long size = info.Length;
                        info.Delete();
                        removed++;
                        bytes += size;
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
            }
            string result = String.Format("{0}: файлов удалено {1}, освобождено {2:N1} МБ.",
                title, removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void CleanThumbnailCache(ILog log)
        {
            string explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");
            CleanCachePattern(log, "кэш эскизов", explorer, "thumbcache_*.db");
        }

        public static void CleanGraphicsCache(ILog log)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] paths = {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, @"NVIDIA\DXCache"),
                Path.Combine(local, @"NVIDIA\GLCache"),
                Path.Combine(local, @"NVIDIA\ComputeCache"),
                Path.Combine(local, @"AMD\DxCache"),
                Path.Combine(local, @"AMD\GLCache"),
                Path.Combine(local, @"AMD\VkCache"),
                Path.Combine(local, @"Intel\ShaderCache"),
                Path.Combine(local, @"Intel\ComputeCache"),
                Path.Combine(local, @"Microsoft\DirectX\ShaderCache")
            };
            CleanCachePaths(log, "кэш графики", paths);
        }

        public static void CleanErrorReports(ILog log)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] paths = {
                Path.Combine(win, @"WER\ReportQueue"),
                Path.Combine(win, @"WER\ReportArchive"),
                Path.Combine(local, @"Microsoft\Windows\WER")
            };
            CleanCachePaths(log, "отчёты об ошибках Windows", paths);
        }

        public static void CleanIconCache(ILog log)
        {
            string explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");
            CleanCachePattern(log, "кэш значков", explorer, "iconcache_*.db");
        }

        public static void CleanWindowsLogs(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] paths = {
                Path.Combine(win, "Logs"),
                Path.Combine(win, "Panther")
            };
            CleanCachePaths(log, "журналы Windows", paths);
        }

        public static void CleanMemoryDumps(ILog log)
        {
            log.Log("Удаление дампов памяти...", LogLevel.Info);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            int removed = 0, skipped = 0;
            long bytes = 0;
            string[] files = {
                Path.Combine(win, "MEMORY.DMP"),
                Path.Combine(win, "memory.dmp")
            };
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        bytes += new FileInfo(file).Length;
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            CleanFilesRecursive(Path.Combine(win, "Minidump"), ref removed, ref skipped, ref bytes);
            CleanFilesRecursive(Path.Combine(local, "CrashDumps"), ref removed, ref skipped, ref bytes);
            string result = String.Format("дампы памяти: файлов удалено {0}, освобождено {1:N1} МБ.", removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void CleanRecentItems(ILog log)
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] paths = {
                Path.Combine(app, @"Microsoft\Windows\Recent")
            };
            CleanCachePaths(log, "недавние документы и переходы", paths);
        }

        public static void CleanStoreCache(ILog log)
        {
            log.Log("Очистка кэша Microsoft Store...", LogLevel.Info);
            string packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            int removed = 0, skipped = 0;
            long bytes = 0;
            try
            {
                if (Directory.Exists(packages))
                    foreach (string dir in Directory.GetDirectories(packages, "Microsoft.WindowsStore_*"))
                        CleanFilesRecursive(Path.Combine(dir, "LocalCache"), ref removed, ref skipped, ref bytes);
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
            string result = String.Format("кэш Microsoft Store: файлов удалено {0}, освобождено {1:N1} МБ.", removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void CleanDeliveryOptimization(ILog log)
        {
            log.Log("Очистка файлов оптимизации доставки...", LogLevel.Info);
            Sys.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"try { Delete-DeliveryOptimizationCache -Force } catch { }\"");
            log.Log("Файлы оптимизации доставки очищены.", LogLevel.Ok);
        }

        public static void CleanPrefetch(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            CleanCachePaths(log, "prefetch", new string[] { Path.Combine(win, "Prefetch") });
        }

        public static void CleanFontCache(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] paths = {
                Path.Combine(local, "FontCache"),
                Path.Combine(win, @"ServiceProfiles\LocalService\AppData\Local\FontCache")
            };
            CleanCachePaths(log, "кэш шрифтов", paths);
        }

        public static void CleanBrowserCaches(ILog log)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<string> paths = new List<string>();
            string[] userDataRoots = {
                @"Google\Chrome\User Data",
                @"Microsoft\Edge\User Data",
                @"BraveSoftware\Brave-Browser\User Data",
                @"Yandex\YandexBrowser\User Data",
                @"Vivaldi\User Data"
            };
            string[] profileCacheNames = {
                "Cache", "Code Cache", "GPUCache", "GrShaderCache", "ShaderCache"
            };
            foreach (string root in userDataRoots)
            {
                string userData = Path.Combine(local, root);
                if (!Directory.Exists(userData)) continue;
                try
                {
                    foreach (string profile in Directory.GetDirectories(userData))
                        foreach (string cacheName in profileCacheNames)
                            paths.Add(Path.Combine(profile, cacheName));
                }
                catch (IOException ex) { log.Log("Не удалось прочитать профили браузера: " + ex.Message, LogLevel.Warn); }
                catch (UnauthorizedAccessException ex) { log.Log("Нет доступа к профилям браузера: " + ex.Message, LogLevel.Warn); }
            }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] operaRoots = {
                Path.Combine(roaming, @"Opera Software\Opera Stable"),
                Path.Combine(roaming, @"Opera Software\Opera GX Stable")
            };
            string[] operaCacheNames = {
                "Cache", "Code Cache", "GPUCache", "System Cache"
            };
            foreach (string operaRoot in operaRoots)
                foreach (string cacheName in operaCacheNames)
                    paths.Add(Path.Combine(operaRoot, cacheName));

            string localOpera = Path.Combine(local, @"Opera Software\Opera Stable");
            foreach (string cacheName in operaCacheNames)
                paths.Add(Path.Combine(localOpera, cacheName));

            string firefox = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(firefox))
            {
                try
                {
                    foreach (string profile in Directory.GetDirectories(firefox))
                    {
                        paths.Add(Path.Combine(profile, "cache2"));
                        paths.Add(Path.Combine(profile, "startupCache"));
                    }
                }
                catch (IOException ex) { log.Log("Не удалось прочитать профили Firefox: " + ex.Message, LogLevel.Warn); }
                catch (UnauthorizedAccessException ex) { log.Log("Нет доступа к профилям Firefox: " + ex.Message, LogLevel.Warn); }
            }
            CleanCachePaths(log, "кэш браузеров", paths.ToArray());
        }

        public static void CleanDiagnosisCache(ILog log)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            CleanCachePaths(log, "кэш диагностики", new string[] { Path.Combine(programData, @"Microsoft\Diagnosis") });
        }

        public static void CleanPrintQueue(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            CleanCachePaths(log, "очередь печати", new string[] { Path.Combine(win, @"System32\spool\PRINTERS") });
        }

        public static void CleanSystemTemp(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] paths = {
                Path.Combine(win, "SystemTemp"),
                Path.Combine(win, @"SoftwareDistribution\DataStore\Logs")
            };
            CleanCachePaths(log, "системные временные файлы", paths);
        }

        public static void CleanSearchCache(ILog log)
        {
            string packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            List<string> paths = new List<string>();
            try
            {
                if (Directory.Exists(packages))
                    foreach (string dir in Directory.GetDirectories(packages, "Microsoft.Windows.Search_*"))
                        paths.Add(Path.Combine(dir, "LocalState"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            CleanCachePaths(log, "кэш поиска Windows", paths.ToArray());
        }

        public static void CleanUwpTempState(ILog log)
        {
            log.Log("Очистка TempState приложений Магазина...", LogLevel.Info);
            string packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            int removed = 0, skipped = 0;
            long bytes = 0;
            try
            {
                if (Directory.Exists(packages))
                    foreach (string dir in Directory.GetDirectories(packages))
                        CleanFilesRecursive(Path.Combine(dir, "TempState"), ref removed, ref skipped, ref bytes);
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
            string result = String.Format("TempState приложений: файлов удалено {0}, освобождено {1:N1} МБ.", removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void CleanDownloadedProgramFiles(ILog log)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            CleanCachePaths(log, "Downloaded Program Files",
                new string[] { Path.Combine(win, "Downloaded Program Files") });
        }

        public static void CleanAllUserTemps(ILog log)
        {
            log.Log("Очистка Temp во всех профилях...", LogLevel.Info);
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string users = Path.Combine(root, "Users");
            int removed = 0, skipped = 0;
            long bytes = 0;
            try
            {
                if (Directory.Exists(users))
                    foreach (string profile in Directory.GetDirectories(users))
                        CleanFilesRecursive(Path.Combine(profile, @"AppData\Local\Temp"), ref removed, ref skipped, ref bytes);
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
            string result = String.Format("Temp профилей: файлов удалено {0}, освобождено {1:N1} МБ.", removed, bytes / 1048576.0);
            log.Log(skipped == 0 ? result : result + " Пропущено занятых/недоступных: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void DisableTelemetry(ILog log)
        {
            log.Log("Отключение телеметрии...", LogLevel.Info);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1);
            Sys.RegDword(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
            log.Log("Параметры телеметрии изменены. Задания планировщика не менялись.", LogLevel.Ok);
        }

        public static void DisableTelemetryTasks(ILog log)
        {
            log.Log("Отключение выбранных заданий телеметрии...", LogLevel.Info);
            int disabled = 0, skipped = 0;
            string firstFailure = null;
            foreach (string task in TelemetryTasks)
            {
                string output;
                int exitCode = Sys.RunWithExitCode("schtasks",
                    "/Change /TN \"" + task + "\" /Disable", out output);
                if (exitCode == 0)
                {
                    disabled++;
                }
                else
                {
                    skipped++;
                    if (firstFailure == null && !String.IsNullOrWhiteSpace(output))
                        firstFailure = output;
                }
            }

            string result = "Отключено заданий телеметрии: " + disabled + " из " + TelemetryTasks.Length + ".";
            if (skipped > 0 && !String.IsNullOrWhiteSpace(firstFailure))
                result += " Первая ошибка: " + firstFailure;
            log.Log(skipped == 0 ? result : result + " Не найдено/недоступно: " + skipped + ".",
                skipped == 0 ? LogLevel.Ok : LogLevel.Warn);
        }

        public static void DisableCortana(ILog log)
        {
            log.Log("Отключение Cortana и веб-поиска...", LogLevel.Info);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0);
            log.Log("Cortana отключена.", LogLevel.Ok);
        }

        public static void DisableServices(ILog log)
        {
            log.Log("Отключение лишних служб...", LogLevel.Info);
            int n = 0;
            foreach (string s in BloatServices)
            {
                if (Sys.SetServiceStart(s, 4)) { Sys.StopService(s); n++; }
            }
            log.Log("Отключено служб: " + n, LogLevel.Ok);
        }

        public static void DisableXboxGameBar(ILog log)
        {
            log.Log("Отключение Xbox Game Bar и Game DVR...", LogLevel.Info);
            Sys.RegDword(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0);
            Sys.RegDword(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 0);
            Sys.RegDword(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
            log.Log("Game Bar/DVR отключены.", LogLevel.Ok);
        }

        public static void TweakExplorer(ILog log)
        {
            log.Log("Показ расширений и скрытых файлов...", LogLevel.Info);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 0);
            log.Log("Проводник настроен.", LogLevel.Ok);
        }

        public static void TweakTransparency(ILog log)
        {
            log.Log("Отключение прозрачности...", LogLevel.Info);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            log.Log("Прозрачность отключена.", LogLevel.Ok);
        }

        public static void TweakAnimations(ILog log)
        {
            log.Log("Визуальные эффекты на быстродействие...", LogLevel.Info);
            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
            log.Log("Готово (применится после перезахода).", LogLevel.Ok);
        }

        public static void TweakHibernate(ILog log)
        {
            log.Log("Отключение гибернации...", LogLevel.Info);
            Sys.Run("powercfg", "/hibernate off");
            log.Log("Гибернация отключена.", LogLevel.Ok);
        }

        public static void TweakPower(ILog log)
        {
            log.Log("Схема питания: высокая производительность...", LogLevel.Info);
            string guid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
            Sys.Run("powercfg", "/setactive " + guid);
            Sys.Run("powercfg", "/duplicatescheme " + guid);
            Sys.Run("powercfg", "/setactive " + guid);
            log.Log("Схема питания применена.", LogLevel.Ok);
        }

        public static void TweakAds(ILog log)
        {
            log.Log("Отключение рекламы и предложений...", LogLevel.Info);
            string cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            string[] names = {
                "ContentDeliveryAllowed", "FeatureManagementEnabled", "OemPreInstalledAppsEnabled",
                "PreInstalledAppsEnabled", "SilentInstalledAppsEnabled", "SoftLandingEnabled",
                "SystemPaneSuggestionsEnabled", "SubscribedContent-338388Enabled",
                "SubscribedContent-338389Enabled", "SubscribedContent-353698Enabled"
            };
            foreach (string name in names) Sys.RegDword(cdm, name, 0);
            log.Log("Рекламные предложения отключены.", LogLevel.Ok);
        }

        public static void RemoveOptionalMicrosoftApps(ILog log)
        {
            DialogResult answer = MessageBox.Show(
                "Будут удалены только для текущего пользователя необязательные приложения Microsoft: Новости, Погода, Получить справку, Начало работы, Solitaire, Центр отзывов, Связь с телефоном и личный Teams, если они установлены.\n" +
                "Microsoft Store, Edge, Защитник Windows, системные компоненты и приложения других пользователей не затрагиваются. Удалённые приложения можно установить снова из Microsoft Store. Продолжить?",
                "Удаление необязательных приложений", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                log.Log("Удаление приложений отменено.", LogLevel.Warn);
                return;
            }

            string names = String.Join(",", OptionalMicrosoftAppPackages.Select(
                delegate(string name) { return "'" + name + "'"; }));
            string script =
                "$ErrorActionPreference='Stop';" +
                "$names=@(" + names + ");$removed=0;$missing=0;$failed=New-Object 'System.Collections.Generic.List[string]';" +
                "foreach($name in $names){" +
                    "$packages=@(Get-AppxPackage -Name $name -ErrorAction SilentlyContinue | Where-Object {$_.Name -eq $name});" +
                    "if($packages.Count -eq 0){$missing++;continue};" +
                    "foreach($package in $packages){try{Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop;$removed++}catch{[void]$failed.Add($name)}}" +
                "};" +
                "Write-Output ('DMTEAM|removed={0}|missing={1}|failed={2}' -f $removed,$missing,[string]::Join(',', $failed.ToArray()))";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string output;
            int exitCode = Sys.RunWithExitCode("powershell",
                "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, out output);
            if (exitCode != 0)
            {
                log.Log("Не удалось выполнить удаление приложений. Код завершения: " + exitCode +
                    (String.IsNullOrWhiteSpace(output) ? "." : ". " + output), LogLevel.Err);
                return;
            }

            string result = output.Split(new string[] { "DMTEAM|" }, StringSplitOptions.None).Last().Trim();
            string[] fields = result.Split('|');
            if (fields.Length != 3 || !fields[0].StartsWith("removed=", StringComparison.Ordinal) ||
                !fields[1].StartsWith("missing=", StringComparison.Ordinal) ||
                !fields[2].StartsWith("failed=", StringComparison.Ordinal))
            {
                log.Log("Не удалось разобрать результат удаления приложений. " + output, LogLevel.Err);
                return;
            }

            string failed = fields[2].Substring("failed=".Length);
            string summary = "Необязательные приложения Microsoft: " +
                fields[0].Substring("removed=".Length) + " удалено, " +
                fields[1].Substring("missing=".Length) + " не установлено.";
            if (!String.IsNullOrEmpty(failed))
                log.Log(summary + " Не удалось удалить: " + failed + ".", LogLevel.Warn);
            else
                log.Log(summary, LogLevel.Ok);
        }

        public static void NetOptimize(ILog log)
        {
            log.Log("Оптимизация параметров TCP...", LogLevel.Info);
            Sys.Run("netsh", "int tcp set global autotuninglevel=normal");
            Sys.Run("netsh", "int tcp set global rss=enabled");
            Sys.Run("netsh", "int tcp set global ecncapability=enabled");
            log.Log("Параметры TCP обновлены.", LogLevel.Ok);
        }

        public static void CreateRestorePoint(ILog log)
        {
            log.Log("Создание точки восстановления...", LogLevel.Info);
            string cmd = "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive $env:SystemDrive + '\\'; Checkpoint-Computer -Description DMTeam -RestorePointType MODIFY_SETTINGS\"";
            Sys.Run("powershell", cmd);
            log.Log("Точка восстановления запрошена.", LogLevel.Ok);
        }

        public static bool BackupAutoruns(ILog log)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Backup", "Autoruns_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string[] keys = {
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce"
            };
            string[] startupDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            try
            {
                Directory.CreateDirectory(dir);
                foreach (string keyPath in keys)
                {
                    string subKey;
                    RegistryKey root = Sys.Split(keyPath, out subKey);
                    using (RegistryKey key = root == null ? null : root.OpenSubKey(subKey))
                    {
                        if (key == null) continue;
                    }

                    string backupFile = Path.Combine(dir,
                        keyPath.Replace("\\", "_").Replace(":", "_") + ".reg");
                    string output;
                    int result = Sys.RunWithExitCode("reg.exe",
                        "export \"" + keyPath + "\" \"" + backupFile + "\" /y", out output);
                    if (result != 0 || !File.Exists(backupFile))
                    {
                        log.Log("Не удалось сохранить " + keyPath + ". Автозагрузка не изменена. " + output,
                            LogLevel.Err);
                        return false;
                    }
                }

                for (int i = 0; i < startupDirs.Length; i++)
                {
                    string source = startupDirs[i];
                    if (String.IsNullOrEmpty(source) || !Directory.Exists(source)) continue;
                    CopyDirectory(source, Path.Combine(dir, i == 0 ? "Startup_User" : "Startup_Common"));
                }
            }
            catch (Exception ex)
            {
                log.Log("Не удалось создать резервную копию автозагрузки. Изменения не внесены: " + ex.Message,
                    LogLevel.Err);
                return false;
            }

            log.Log("Резервная копия автозагрузки сохранена: " + dir, LogLevel.Ok);
            return true;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static int RemoveRunValues(string full, ref int failures)
        {
            int removed = 0;
            string subKey;
            RegistryKey root = Sys.Split(full, out subKey);
            if (root == null) { failures++; return removed; }

            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey, true))
                {
                    if (key == null) return removed;
                    foreach (string name in key.GetValueNames())
                    {
                        try { key.DeleteValue(name, false); removed++; }
                        catch { failures++; }
                    }
                }
            }
            catch { failures++; }
            return removed;
        }

        public static void RemoveAllAutoruns(ILog log)
        {
            DialogResult answer = MessageBox.Show(
                "Будут удалены все записи Run/RunOnce текущего пользователя и компьютера, а также ярлыки из папок Startup. Перед удалением будет создана резервная копия. Продолжить?",
                "Удаление автозагрузки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                log.Log("Удаление автозагрузки отменено.", LogLevel.Warn);
                return;
            }

            if (!BackupAutoruns(log)) return;

            int removed = 0;
            int failures = 0;
            removed += RemoveRunValues(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", ref failures);
            removed += RemoveRunValues(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", ref failures);
            removed += RemoveRunValues(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", ref failures);
            removed += RemoveRunValues(@"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce", ref failures);

            string[] startupDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string directory in startupDirs)
            {
                if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        if (String.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { File.Delete(file); removed++; }
                        catch { failures++; }
                    }
                }
                catch { failures++; }
            }

            string result = "Удалено элементов автозагрузки: " + removed + ".";
            if (failures > 0)
                log.Log(result + " Не удалось удалить: " + failures + ". Проверьте права доступа.",
                    LogLevel.Warn);
            else
                log.Log(result, LogLevel.Ok);
        }

        // ============================================================
        // ПРИНУДИТЕЛЬНОЕ ОТКЛЮЧЕНИЕ ОБНОВЛЕНИЙ WINDOWS
        // ============================================================
        public static void ForceDisableWindowsUpdate(ILog log)
        {
            log.Log("Принудительное отключение Windows Update...", LogLevel.Info);

            string[] updateServices = new string[]
            {
                "wuauserv", "BITS", "UsoSvc", "WaaSMedicSvc",
                "DoSvc", "TrustedInstaller", "msiserver", "cryptsvc"
            };
            foreach (string s in updateServices)
            {
                Sys.SetServiceStart(s, 4);
                Sys.StopService(s);
            }

            string au = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
            Sys.RegDword(au, "NoAutoUpdate", 1);
            Sys.RegDword(au, "AUOptions", 1);
            Sys.RegDword(au, "NoAutoRebootWithLoggedOnUsers", 1);
            Sys.RegDword(au, "ScheduledInstallDay", 0);
            Sys.RegDword(au, "ScheduledInstallTime", 0);

            string wu = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
            Sys.RegDword(wu, "DisableWindowsUpdateAccess", 1);
            Sys.RegDword(wu, "WUServer", 0);
            Sys.RegDword(wu, "DoNotConnectToWindowsUpdateInternetLocations", 1);
            Sys.RegDword(wu, "SetDisableUXWUAccess", 1);
            Sys.RegDword(wu, "ExcludeWUDriversInQualityUpdate", 1);
            Sys.RegDword(wu, "DeferFeatureUpdates", 1);
            Sys.RegDword(wu, "DeferQualityUpdates", 1);
            Sys.RegDword(wu, "BranchReadinessLevel", 20);
            Sys.RegDword(wu, "DeferFeatureUpdatesPeriodInDays", 365);
            Sys.RegDword(wu, "DeferQualityUpdatesPeriodInDays", 30);

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
                "SearchOrderConfig", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                "ExcludeWUDriversInQualityUpdate", 1);

            string[] updateTasks = new string[]
            {
                @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
                @"\Microsoft\Windows\WindowsUpdate\sih",
                @"\Microsoft\Windows\WindowsUpdate\sihboot",
                @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
                @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
                @"\Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work",
                @"\Microsoft\Windows\UpdateOrchestrator\Schedule Work",
                @"\Microsoft\Windows\UpdateOrchestrator\Start Oobe Expedite Work",
                @"\Microsoft\Windows\UpdateOrchestrator\StartOobeAppsScan_LicenseAccepted",
                @"\Microsoft\Windows\UpdateOrchestrator\StartOobeAppsScanAfterUpdate",
                @"\Microsoft\Windows\UpdateOrchestrator\UUS Failover Task",
                @"\Microsoft\Windows\UpdateOrchestrator\Reboot",
                @"\Microsoft\Windows\UpdateOrchestrator\Reboot_AC",
                @"\Microsoft\Windows\UpdateOrchestrator\Reboot_Battery",
                @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
                @"\Microsoft\Windows\InstallService\ScanForUpdates",
                @"\Microsoft\Windows\InstallService\ScanForUpdatesAsUser",
                @"\Microsoft\Windows\InstallService\SmartRetry",
                @"\Microsoft\Windows\InstallService\RestoreAppsForRepair",
                @"\Microsoft\Windows\WaaSMedic\PerformRemediation",
                @"\Microsoft\Windows\UpdateOrchestrator\PolicyDownloadManager"
            };
            int disabledTasks = 0;
            foreach (string task in updateTasks)
            {
                string output;
                int code = Sys.RunWithExitCode("schtasks",
                    "/Change /TN \"" + task + "\" /Disable", out output);
                if (code == 0) disabledTasks++;
            }

            try
            {
                string hostsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"drivers\etc\hosts");
                if (File.Exists(hostsPath))
                {
                    string marker = "# DMTeam block Windows Update";
                    string hosts = File.ReadAllText(hostsPath);
                    if (hosts.IndexOf(marker, StringComparison.Ordinal) < 0)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine();
                        sb.AppendLine(marker);
                        sb.AppendLine("0.0.0.0 windowsupdate.microsoft.com");
                        sb.AppendLine("0.0.0.0 update.microsoft.com");
                        sb.AppendLine("0.0.0.0 download.windowsupdate.com");
                        sb.AppendLine("0.0.0.0 download.microsoft.com");
                        sb.AppendLine("0.0.0.0 wustat.windows.com");
                        sb.AppendLine("0.0.0.0 ntservicepack.microsoft.com");
                        sb.AppendLine("0.0.0.0 stats.microsoft.com");
                        sb.AppendLine("0.0.0.0 au.download.windowsupdate.com");
                        sb.AppendLine("0.0.0.0 sls.update.microsoft.com");
                        sb.AppendLine("0.0.0.0 fe2.update.microsoft.com");
                        sb.AppendLine("0.0.0.0 fe3.delivery.mp.microsoft.com");
                        sb.AppendLine("0.0.0.0 tlu.dl.delivery.mp.microsoft.com");
                        File.AppendAllText(hostsPath, sb.ToString());
                        Sys.Run("ipconfig", "/flushdns");
                    }
                }
            }
            catch { }

            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                "NoAutoRebootWithLoggedOnUsers", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                "AUPowerManagement", 0);

            log.Log("Windows Update отключён принудительно. Отключено задач: " +
                disabledTasks + " из " + updateTasks.Length + ".", LogLevel.Ok);
        }

        // ============================================================
        // ПРИНУДИТЕЛЬНОЕ ОТКЛЮЧЕНИЕ БЕЗОПАСНОСТИ WINDOWS
        // ============================================================
        public static void ForceDisableWindowsSecurity(ILog log)
        {
            log.Log("Принудительное отключение Windows Security...", LogLevel.Info);

            string[] securityServices = new string[]
            {
                "WinDefend", "WdNisSvc", "WdNisDrv", "Sense",
                "SecurityHealthService", "wscsvc", "SgrmAgent", "SgrmBroker",
                "MsSense", "WdBoot", "WdFilter", "WdDevFlt"
            };
            foreach (string s in securityServices)
            {
                Sys.SetServiceStart(s, 4);
                Sys.StopService(s);
            }

            string dp = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender";
            Sys.RegDword(dp, "DisableAntiSpyware", 1);
            Sys.RegDword(dp, "DisableAntiVirus", 1);
            Sys.RegDword(dp, "DisableRoutinelyTakingAction", 1);
            Sys.RegDword(dp, "ServiceKeepAlive", 0);
            Sys.RegDword(dp, "PUAProtection", 0);

            string rtp = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
            Sys.RegDword(rtp, "DisableRealtimeMonitoring", 1);
            Sys.RegDword(rtp, "DisableBehaviorMonitoring", 1);
            Sys.RegDword(rtp, "DisableOnAccessProtection", 1);
            Sys.RegDword(rtp, "DisableScanOnRealtimeEnable", 1);
            Sys.RegDword(rtp, "DisableIOAVProtection", 1);
            Sys.RegDword(rtp, "DisableScriptScanning", 1);
            Sys.RegDword(rtp, "DisableRawWriteNotification", 1);

            string mpEngine = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine";
            Sys.RegDword(mpEngine, "MpEnablePus", 0);

            string spynet = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet";
            Sys.RegDword(spynet, "SpynetReporting", 0);
            Sys.RegDword(spynet, "SubmitSamplesConsent", 2);
            Sys.RegDword(spynet, "DisableBlockAtFirstSeen", 1);

            string defSig = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Signature Updates";
            Sys.RegDword(defSig, "ForceUpdateFromMU", 0);
            Sys.RegDword(defSig, "SignatureDisableUpdateOnStartupWithoutEngine", 1);

            string tp = @"HKLM\SOFTWARE\Microsoft\Windows Defender\Features";
            Sys.RegDword(tp, "TamperProtection", 0);
            Sys.RegDword(tp, "TamperProtectionSource", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "IsServiceRunning", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "DisableAntiSpyware", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "DisableAntiVirus", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "PassiveMode", 1);

            string sc = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center";
            Sys.RegDword(sc + @"\Notifications", "DisableNotifications", 1);
            Sys.RegDword(sc, "DisableEnhancedNotifications", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0);
            Sys.RegDword(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 0);
            Sys.RegDword(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 0);

            string polSys = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            Sys.RegDword(polSys, "EnableLUA", 0);
            Sys.RegDword(polSys, "ConsentPromptBehaviorAdmin", 0);
            Sys.RegDword(polSys, "ConsentPromptBehaviorUser", 0);
            Sys.RegDword(polSys, "PromptOnSecureDesktop", 0);
            Sys.RegDword(polSys, "EnableInstallerDetection", 0);
            Sys.RegDword(polSys, "ValidateAdminCodeSignatures", 0);
            Sys.RegDword(polSys, "EnableSecureUIAPaths", 0);
            Sys.RegDword(polSys, "EnableUIADesktopToggle", 0);
            Sys.RegDword(polSys, "FilterAdministratorToken", 0);
            Sys.RegDword(polSys, "LocalAccountTokenFilterPolicy", 1);

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Script\Settings", "AmsiEnable", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell",
                "EnableScriptBlockLogging", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell",
                "EnableModuleLogging", 0);

            string[] securityTasks = new string[]
            {
                @"\Microsoft\Windows\Windows Defender\Windows Defender Cache Maintenance",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Cleanup",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Verification",
                @"\Microsoft\Windows\ExploitGuard\ExploitGuard MDM policy Refresh",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Remediation"
            };
            int disabledTasks = 0;
            foreach (string task in securityTasks)
            {
                string output;
                int code = Sys.RunWithExitCode("schtasks",
                    "/Change /TN \"" + task + "\" /Disable", out output);
                if (code == 0) disabledTasks++;
            }

            string[] securityProcs = new string[]
            {
                "MsMpEng", "NisSrv", "MpCmdRun", "SecurityHealthService",
                "SecurityHealthSystray", "SecurityHealthHost", "smartscreen",
                "Sense", "SgrmBroker", "SgrmAgent", "WdNisSrv"
            };
            foreach (string proc in securityProcs)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(proc))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }

            log.Log("Windows Security отключён принудительно. Отключено задач: " +
                disabledTasks + " из " + securityTasks.Length + ".", LogLevel.Ok);
        }

        public static void ForceDisableUpdatesAndSecurity(ILog log)
        {
            log.Log("Полное принудительное отключение обновлений и безопасности...", LogLevel.Info);
            ForceDisableWindowsUpdate(log);
            ForceDisableWindowsSecurity(log);
            log.Log("Готово. Windows Update и Windows Security отключены.", LogLevel.Ok);
        }

        // ============================================================
        // ВОССТАНОВЛЕНИЕ WINDOWS SECURITY
        // ============================================================
        public static void RestoreWindowsSecurity(ILog log)
        {
            log.Log("Восстановление Windows Security...", LogLevel.Info);

            string[] securityServices = new string[]
            {
                "WinDefend", "WdNisSvc", "SecurityHealthService", "wscsvc",
                "Sense", "SgrmAgent", "SgrmBroker", "WdFilter", "WdBoot", "WdDevFlt"
            };
            foreach (string s in securityServices)
            {
                Sys.SetServiceStart(s, 2);
                Sys.StartService(s);
            }

            string dp = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender";
            Sys.RegDword(dp, "DisableAntiSpyware", 0);
            Sys.RegDword(dp, "DisableAntiVirus", 0);
            Sys.RegDword(dp, "DisableRoutinelyTakingAction", 0);
            Sys.RegDword(dp, "ServiceKeepAlive", 1);
            Sys.RegDword(dp, "PUAProtection", 1);

            string rtp = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
            Sys.RegDword(rtp, "DisableRealtimeMonitoring", 0);
            Sys.RegDword(rtp, "DisableBehaviorMonitoring", 0);
            Sys.RegDword(rtp, "DisableOnAccessProtection", 0);
            Sys.RegDword(rtp, "DisableScanOnRealtimeEnable", 0);
            Sys.RegDword(rtp, "DisableIOAVProtection", 0);
            Sys.RegDword(rtp, "DisableScriptScanning", 0);
            Sys.RegDword(rtp, "DisableRawWriteNotification", 0);

            string spynet = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet";
            Sys.RegDword(spynet, "SpynetReporting", 1);
            Sys.RegDword(spynet, "SubmitSamplesConsent", 1);
            Sys.RegDword(spynet, "DisableBlockAtFirstSeen", 0);

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Features", "TamperProtection", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "DisableAntiSpyware", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "DisableAntiVirus", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "PassiveMode", 0);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Defender", "IsServiceRunning", 1);

            string sc = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center";
            Sys.RegDword(sc + @"\Notifications", "DisableNotifications", 0);
            Sys.RegDword(sc, "DisableEnhancedNotifications", 0);

            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 1);
            Sys.RegDword(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 1);
            Sys.RegDword(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 1);

            string polSys = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            Sys.RegDword(polSys, "EnableLUA", 1);
            Sys.RegDword(polSys, "ConsentPromptBehaviorAdmin", 5);
            Sys.RegDword(polSys, "ConsentPromptBehaviorUser", 3);
            Sys.RegDword(polSys, "PromptOnSecureDesktop", 1);
            Sys.RegDword(polSys, "EnableInstallerDetection", 1);
            Sys.RegDword(polSys, "ValidateAdminCodeSignatures", 0);
            Sys.RegDword(polSys, "EnableSecureUIAPaths", 1);
            Sys.RegDword(polSys, "EnableUIADesktopToggle", 0);
            Sys.RegDword(polSys, "FilterAdministratorToken", 0);
            Sys.RegDword(polSys, "LocalAccountTokenFilterPolicy", 0);

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Script\Settings", "AmsiEnable", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell", "EnableScriptBlockLogging", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell", "EnableModuleLogging", 1);

            string[] securityTasks = new string[]
            {
                @"\Microsoft\Windows\Windows Defender\Windows Defender Cache Maintenance",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Cleanup",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Verification",
                @"\Microsoft\Windows\ExploitGuard\ExploitGuard MDM policy Refresh",
                @"\Microsoft\Windows\Windows Defender\Windows Defender Remediation"
            };
            int enabledTasks = 0;
            foreach (string task in securityTasks)
            {
                string output;
                if (Sys.RunWithExitCode("schtasks", "/Change /TN \"" + task + "\" /Enable", out output) == 0)
                    enabledTasks++;
            }

            Sys.StartService("SecurityHealthService");
            Sys.StartService("wscsvc");
            Sys.Run("sc", "start WinDefend");

            log.Log("Windows Security восстановлен. Включено задач: " + enabledTasks + " из " +
                securityTasks.Length + ".", LogLevel.Ok);
        }

        // ============================================================
        // ВОССТАНОВЛЕНИЕ ПОИСКА WINDOWS
        // ============================================================
        public static void RestoreWindowsSearch(ILog log)
        {
            log.Log("Восстановление поиска Windows...", LogLevel.Info);

            Sys.SetServiceStart("WSearch", 2);
            Sys.StartService("WSearch");

            string search = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search";
            Sys.RegDword(search, "AllowCortana", 1);
            Sys.RegDword(search, "AllowSearchToUseLocation", 1);
            Sys.RegDword(search, "ConnectedSearchUseWeb", 1);
            Sys.RegDword(search, "DisableWebSearch", 0);
            Sys.RegDword(search, "AllowCloudSearch", 1);
            Sys.RegDword(search, "AllowIndexingEncryptedStoresOrItems", 1);
            Sys.RegDword(search, "EnableDynamicContentInWSB", 1);

            string searchCu = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
            Sys.RegDword(searchCu, "BingSearchEnabled", 1);
            Sys.RegDword(searchCu, "CortanaConsent", 1);
            Sys.RegDword(searchCu, "SearchboxTaskbarMode", 1);

            string explorerPolicies = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            Sys.RegDword(explorerPolicies, "NoFind", 0);

            string policyExplorer = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";
            Sys.RegDword(policyExplorer, "DisableSearchBoxSuggestions", 0);

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows Search", "SetupCompletedSuccessfully", 1);

            log.Log("Поиск Windows восстановлен.", LogLevel.Ok);
        }

        // ============================================================
        // СБРОС НАСТРОЕК WINDOWS К НОРМАЛЬНЫМ ЗНАЧЕНИЯМ
        // ============================================================
        public static void RestoreDefaultSettings(ILog log)
        {
            log.Log("Сброс настроек Windows к нормальным значениям...", LogLevel.Info);

            string advanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            Sys.RegDword(advanced, "HideFileExt", 1);
            Sys.RegDword(advanced, "ShowSuperHidden", 0);

            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 1);

            Sys.RegDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                "VisualFXSetting", 0);

            Sys.Run("powercfg", "/hibernate on");
            Sys.Run("powercfg", "/setactive SCHEME_BALANCED");

            string cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            string[] adsNames = {
                "ContentDeliveryAllowed", "FeatureManagementEnabled", "OemPreInstalledAppsEnabled",
                "PreInstalledAppsEnabled", "SilentInstalledAppsEnabled", "SoftLandingEnabled",
                "SystemPaneSuggestionsEnabled", "SubscribedContent-338388Enabled",
                "SubscribedContent-338389Enabled", "SubscribedContent-353698Enabled"
            };
            foreach (string name in adsNames) Sys.RegDword(cdm, name, 1);

            Sys.RegDword(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 1);
            Sys.RegDword(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
            Sys.RegDword(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
            Sys.RegDword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 1);

            Sys.Run("netsh", "int tcp set global autotuninglevel=normal");
            Sys.Run("netsh", "int tcp set global rss=enabled");
            Sys.Run("netsh", "int tcp set global ecncapability=disabled");

            Sys.RegDword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
                "SearchOrderConfig", 1);

            log.Log("Настройки Windows сброшены к нормальным значениям.", LogLevel.Ok);
        }

        // ============================================================
        // ПОЛНОЕ ВОССТАНОВЛЕНИЕ
        // ============================================================
        public static void RestoreEverything(ILog log)
        {
            log.Log("Полное восстановление Windows...", LogLevel.Info);
            RestoreWindowsSecurity(log);
            RestoreWindowsSearch(log);
            RestoreDefaultSettings(log);

            try
            {
                string hostsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"drivers\etc\hosts");
                if (File.Exists(hostsPath))
                {
                    string marker = "# DMTeam block Windows Update";
                    string[] lines = File.ReadAllLines(hostsPath);
                    StringBuilder sb = new StringBuilder();
                    bool skip = false;
                    foreach (string line in lines)
                    {
                        if (line.Trim() == marker) { skip = true; continue; }
                        if (skip && line.TrimStart().StartsWith("0.0.0.0")) continue;
                        skip = false;
                        sb.AppendLine(line);
                    }
                    File.WriteAllText(hostsPath, sb.ToString());
                    Sys.Run("ipconfig", "/flushdns");
                }
            }
            catch { }

            log.Log("Полное восстановление завершено.", LogLevel.Ok);
        }
    }

    // ---------------- Элемент оптимизации ----------------
    public class OptItem
    {
        public string Group;
        public string Text;
        public string Desc;
        public bool DefaultOn;
        public Action<ILog> Run;
    }

    public class DashboardButton : Button
    {
        public bool Primary;
        private bool hover, down;

        public DashboardButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color tile = down
                ? Color.FromArgb(34, 35, 37)
                : Primary
                    ? (hover ? Color.FromArgb(38, 39, 42) : Color.FromArgb(29, 30, 32))
                    : (hover ? Color.FromArgb(34, 35, 38) : Color.FromArgb(23, 24, 26));
            using (GraphicsPath path = Ui.RoundedPath(bounds, 10))
            using (SolidBrush fill = new SolidBrush(tile))
            using (Pen border = new Pen(Focused ? Theme.Accent : (Primary ? Color.FromArgb(65, 67, 71) : (hover ? Color.FromArgb(65, 67, 71) : Color.FromArgb(48, 50, 53)))))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            Rectangle titleRect = new Rectangle(18, Height / 2 - 13, Width - 36, 26);
            TextRenderer.DrawText(g, Text, Font, titleRect, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ---------------- Главное меню ----------------
    public class MainForm : Form
    {
        private Point dragStart;
        private Point formStart;
        private bool dragging;

        public MainForm()
        {
            Text = "DMTeam";
            ClientSize = new Size(720, 276);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 11, 12);
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            BuildDashboard();
        }

        private void BuildDashboard()
        {
            Panel header = new Panel();
            header.BackColor = Color.FromArgb(17, 18, 19);
            header.Location = new Point(0, 0);
            header.Size = new Size(720, 64);
            Controls.Add(header);
            EnableDrag(header);

            Label title = new Label();
            title.Text = "DMTeam";
            title.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(242, 244, 248);
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Location = new Point(22, 8);
            title.Size = new Size(300, 30);
            header.Controls.Add(title);
            EnableDrag(title);

            RButton close = Ui.CloseBtn();
            close.Size = new Size(34, 34);
            close.Location = new Point(676, 7);
            close.Click += delegate { Close(); };
            header.Controls.Add(close);

            DashboardButton optimize = CreateTile("Оптимизация Windows", true);
            optimize.Primary = true;
            optimize.Location = new Point(20, 84);
            optimize.Size = new Size(332, 126);
            optimize.Click += delegate { new OptimizationWindow().ShowDialog(this); };
            Controls.Add(optimize);

            DashboardButton more = CreateTile("Инструменты и внешний вид", false);
            more.Location = new Point(368, 84);
            more.Size = new Size(332, 126);
            more.Click += delegate { new ToolsWindow().ShowDialog(this); };
            Controls.Add(more);

            RButton about = RButton.Secondary("О программе");
            about.Location = new Point(250, 224);
            about.Size = new Size(220, 36);
            about.Click += delegate
            {
                MessageBox.Show(
                    "DMTeam — утилита для быстрой настройки Windows и обслуживания системы.\r\n\r\nСоздана MRoboting.",
                    "О программе DMTeam",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };
            Controls.Add(about);
        }

        private static DashboardButton CreateTile(string text, bool hero)
        {
            return new DashboardButton
            {
                Text = text,
                Primary = hero,
                Font = hero ? new Font("Segoe UI", 11.5f, FontStyle.Bold) : Theme.Bold
            };
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }
    }

    public class SectionButton : Button
    {
        public string LabelText;
        public string CountText;
        public bool Active;
        private bool hover;

        public SectionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(18, 19, 21);
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public event EventHandler SectionClicked;

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            EventHandler handler = SectionClicked;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color back = Active ? Color.FromArgb(36, 37, 40) : hover ? Color.FromArgb(29, 30, 32) : BackColor;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 7))
            using (SolidBrush fill = new SolidBrush(back))
                g.FillPath(fill, path);
            if (Active)
                using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 6, 3, Height - 12), 2))
                using (SolidBrush stripe = new SolidBrush(Theme.Accent))
                    g.FillPath(stripe, path);

            TextRenderer.DrawText(g, LabelText, Font, new Rectangle(16, 0, Width - 66, Height),
                Active ? Theme.Text : Theme.Sub,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!String.IsNullOrEmpty(CountText))
            {
                Rectangle badge = new Rectangle(Width - 45, 11, 30, Height - 22);
                using (GraphicsPath path = Ui.RoundedPath(badge, 7))
                using (SolidBrush fill = new SolidBrush(Active ? Color.FromArgb(51, 53, 57) : Color.FromArgb(35, 36, 39)))
                    g.FillPath(fill, path);
                TextRenderer.DrawText(g, CountText, Theme.Small, badge, Theme.Sub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class ActionRow : Control
    {
        public string ActionName;
        private bool selected;
        private bool hover;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                EventHandler handler = SelectedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }
        public event EventHandler SelectedChanged;

        public ActionRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Selected = !Selected;
            base.OnMouseDown(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Selected = !Selected;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle card = new Rectangle(1, 1, Width - 3, Height - 3);
            Color cardColor = Selected
                ? (hover ? Color.FromArgb(40, 41, 44) : Color.FromArgb(32, 33, 36))
                : (hover || Focused ? Color.FromArgb(34, 35, 38) : Color.FromArgb(25, 26, 28));
            using (GraphicsPath path = Ui.RoundedPath(card, 7))
            using (SolidBrush fill = new SolidBrush(cardColor))
            using (Pen border = new Pen(Focused ? Theme.Accent : (Selected ? Color.FromArgb(70, 72, 76) : Color.FromArgb(48, 50, 53))))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            Rectangle box = new Rectangle(14, (Height - 18) / 2, 18, 18);
            using (GraphicsPath path = Ui.RoundedPath(box, 4))
            using (SolidBrush fill = new SolidBrush(Selected ? Theme.Accent : Color.FromArgb(24, 25, 27)))
            using (Pen border = new Pen(Selected ? Theme.Accent : Color.FromArgb(76, 78, 82)))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            if (Selected)
            {
                using (Pen mark = new Pen(Color.FromArgb(18, 19, 21), 2f))
                {
                    g.DrawLine(mark, box.Left + 4, box.Top + 9, box.Left + 8, box.Top + 13);
                    g.DrawLine(mark, box.Left + 8, box.Top + 13, box.Left + 15, box.Top + 5);
                }
            }
            TextRenderer.DrawText(g, ActionName, Theme.Ui, new Rectangle(46, 0, Width - 62, Height),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public class ProcessWindow : Form
    {
        private sealed class ProcessInfo
        {
            public int Id;
            public string Name;
            public long StartTimeUtcTicks;

            public ProcessInfo(int id, string name, long startTimeUtcTicks)
            {
                Id = id;
                Name = name;
                StartTimeUtcTicks = startTimeUtcTicks;
            }
        }

        private readonly List<ListViewItem> processItems = new List<ListViewItem>();
        private ListView processList;
        private TextBox search;
        private Label processStatus;
        private WinFormsTimer refreshTimer;
        private Point dragStart;
        private Point formStart;
        private bool dragging;

        public ProcessWindow()
        {
            Text = "DMTeam — Диспетчер процессов";
            ClientSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, header, 0, 0, 860, 60);
            Label title = new Label();
            title.Text = "Диспетчер процессов";
            title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            Ui.Place(header, title, 20, 9, 400, 30);
            EnableDrag(header);
            EnableDrag(title);
            RButton close = Ui.CloseBtn();
            Ui.Place(header, close, 816, 5, 36, 34);
            close.Click += delegate { Close(); };

            search = new TextBox();
            search.BorderStyle = BorderStyle.FixedSingle;
            search.BackColor = Color.FromArgb(24, 25, 27);
            search.ForeColor = Theme.Text;
            search.Font = Theme.Ui;
            Ui.Place(this, search, 20, 78, 500, 34);
            search.TextChanged += delegate { ApplyFilter(); };

            RButton refresh = RButton.Secondary("Обновить");
            Ui.Place(this, refresh, 534, 78, 110, 34);
            refresh.Click += delegate { RefreshProcesses(); };

            RButton systemTaskManager = RButton.Secondary("Системный диспетчер");
            Ui.Place(this, systemTaskManager, 658, 78, 182, 34);
            systemTaskManager.Click += delegate
            {
                try { Process.Start("taskmgr.exe"); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "DMTeam", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };

            processList = new ListView();
            processList.View = View.Details;
            processList.FullRowSelect = true;
            processList.GridLines = false;
            processList.HideSelection = false;
            processList.MultiSelect = false;
            processList.BorderStyle = BorderStyle.FixedSingle;
            processList.BackColor = Color.FromArgb(18, 19, 21);
            processList.ForeColor = Theme.Text;
            processList.Font = Theme.Small;
            processList.Columns.Add("Процесс", 390);
            processList.Columns.Add("PID", 100, HorizontalAlignment.Right);
            processList.Columns.Add("Память", 280, HorizontalAlignment.Right);
            Ui.StyleDarkList(processList);
            Ui.Place(this, processList, 20, 126, 820, 360);
            processList.Resize += delegate { FitProcessColumns(); };
            FitProcessColumns();
            processList.MouseUp += ProcessListMouseUp;

            ContextMenuStrip processMenu = new ContextMenuStrip();
            processMenu.Items.Add("Сведения", null, delegate { ShowSelectedProcessDetails(); });
            processMenu.Items.Add("Открыть расположение файла", null, delegate { OpenSelectedProcessLocation(); });
            processMenu.Items.Add("Копировать PID", null, delegate { CopySelectedProcessId(); });
            processMenu.Items.Add(new ToolStripSeparator());
            processMenu.Items.Add("Завершить процесс…", null, delegate { EndSelectedProcess(); });
            processList.ContextMenuStrip = processMenu;

            processStatus = new Label();
            processStatus.Font = Theme.Small;
            processStatus.ForeColor = Theme.Sub;
            Ui.Place(this, processStatus, 20, 496, 820, 24);

            Label note = new Label();
            note.Text = "ПКМ — действия с процессом. Системные процессы и DMTeam защищены от завершения.";
            note.Font = Theme.Small;
            note.ForeColor = Theme.Sub;
            Ui.Place(this, note, 20, 526, 820, 20);
            RefreshProcesses();
            refreshTimer = new WinFormsTimer();
            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate { RefreshProcesses(); };
            refreshTimer.Start();
        }

        private void FitProcessColumns()
        {
            if (processList == null || processList.Columns.Count < 3) return;
            processList.Columns[0].Width = 390;
            processList.Columns[1].Width = 100;
            processList.Columns[2].Width = Math.Max(140,
                processList.ClientSize.Width - processList.Columns[0].Width -
                processList.Columns[1].Width - SystemInformation.VerticalScrollBarWidth);
        }

        private void RefreshProcesses()
        {
            processItems.Clear();
            int skipped = 0;
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch (Exception ex)
            {
                processStatus.Text = "Не удалось получить список процессов.";
                MessageBox.Show(ex.Message, "Ошибка диспетчера процессов",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        ListViewItem row = new ListViewItem(process.ProcessName);
                        row.SubItems.Add(process.Id.ToString());
                        row.SubItems.Add(String.Format("{0:N1} МБ", process.WorkingSet64 / 1048576.0));
                        long startTime = 0;
                        try { startTime = process.StartTime.ToUniversalTime().Ticks; }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        row.Tag = new ProcessInfo(process.Id, process.ProcessName, startTime);
                        processItems.Add(row);
                    }
                    catch (InvalidOperationException) { skipped++; }
                    catch (System.ComponentModel.Win32Exception) { skipped++; }
                }
            }
            processItems.Sort(delegate(ListViewItem a, ListViewItem b)
            {
                return StringComparer.CurrentCultureIgnoreCase.Compare(a.Text, b.Text);
            });
            ApplyFilter(skipped);
        }

        private void ProcessListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewHitTestInfo hit = processList.HitTest(e.Location);
            if (hit.Item == null) return;
            if (!hit.Item.Selected)
            {
                processList.SelectedItems.Clear();
                hit.Item.Selected = true;
            }
        }

        private ProcessInfo GetSelectedProcessInfo()
        {
            if (processList.SelectedItems.Count == 0) return null;
            return processList.SelectedItems[0].Tag as ProcessInfo;
        }

        private void ShowSelectedProcessDetails()
        {
            ProcessInfo info = GetSelectedProcessInfo();
            if (info == null) return;
            try
            {
                using (Process process = Process.GetProcessById(info.Id))
                {
                    string path = "";
                    try { path = process.MainModule.FileName; }
                    catch (System.ComponentModel.Win32Exception) { path = "Недоступно (нет прав или системный процесс)"; }
                    catch (InvalidOperationException) { path = "Процесс уже завершён"; }
                    string started = "";
                    try { started = process.StartTime.ToString("G"); }
                    catch (System.ComponentModel.Win32Exception) { started = "Недоступно"; }
                    MessageBox.Show(
                        "Процесс: " + info.Name + "\r\nPID: " + info.Id +
                        "\r\nПамять: " + String.Format("{0:N1} МБ", process.WorkingSet64 / 1048576.0) +
                        "\r\nЗапущен: " + started + "\r\nФайл: " + path,
                        "Сведения о процессе", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось получить сведения: " + ex.Message,
                    "Сведения о процессе", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSelectedProcessLocation()
        {
            ProcessInfo info = GetSelectedProcessInfo();
            if (info == null) return;
            try
            {
                using (Process process = Process.GetProcessById(info.Id))
                {
                    string path = process.MainModule.FileName;
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть расположение файла: " + ex.Message,
                    "DMTeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopySelectedProcessId()
        {
            ProcessInfo info = GetSelectedProcessInfo();
            if (info == null) return;
            try { Clipboard.SetText(info.Id.ToString()); }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось скопировать PID: " + ex.Message,
                    "DMTeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EndSelectedProcess()
        {
            ProcessInfo info = GetSelectedProcessInfo();
            if (info == null) return;
            if (info.Id <= 4 || info.Id == Process.GetCurrentProcess().Id)
            {
                MessageBox.Show("Завершение системных процессов и самого DMTeam заблокировано.",
                    "Нельзя завершить процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show("Завершить процесс " + info.Name + " (PID " + info.Id +
                ")? Несохранённые данные приложения могут быть потеряны.",
                "Подтвердите завершение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                using (Process process = Process.GetProcessById(info.Id))
                {
                    if (!String.Equals(process.ProcessName, info.Name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("PID уже принадлежит другому процессу. Обновите список.");
                    if (info.StartTimeUtcTicks != 0 &&
                        process.StartTime.ToUniversalTime().Ticks != info.StartTimeUtcTicks)
                        throw new InvalidOperationException("Процесс уже завершился, PID был повторно использован.");
                    process.Kill();
                    if (!process.WaitForExit(5000))
                        throw new InvalidOperationException("Процесс не завершился за отведённое время.");
                }
                processStatus.Text = "Процесс " + info.Name + " завершён.";
                RefreshProcesses();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось завершить процесс: " + ex.Message,
                    "DMTeam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        private void ApplyFilter() { ApplyFilter(0); }

        private void ApplyFilter(int skipped)
        {
            if (processList == null) return;
            processList.BeginUpdate();
            processList.Items.Clear();
            string query = search.Text.Trim();
            foreach (ListViewItem item in processItems)
            {
                if (query.Length == 0 || item.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    item.SubItems[1].Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    processList.Items.Add((ListViewItem)item.Clone());
            }
            processList.EndUpdate();
            processStatus.Text = "Процессов: " + processItems.Count + "   Показано: " +
                processList.Items.Count + (skipped > 0 ? "   Недоступно для чтения: " + skipped : "");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                refreshTimer = null;
            }
            base.OnFormClosed(e);
        }
    }

    public class RestrictionsWindow : Form
    {
        private sealed class RestrictionEntry
        {
            public string DisplayName;
            public string RegistryPath;
            public string ValueName;
            public string Value;

            public RestrictionEntry(string displayName, string registryPath, string valueName,
                string value)
            {
                DisplayName = displayName;
                RegistryPath = registryPath;
                ValueName = valueName;
                Value = value;
            }
        }

        private ListView entries;
        private Label status;
        private WinFormsTimer refreshTimer;
        private Point dragStart;
        private Point formStart;
        private bool dragging;

        public RestrictionsWindow()
        {
            Text = "DMTeam — Монитор блокировок";
            ClientSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, header, 0, 0, 900, 60);
            Label title = new Label();
            title.Text = "Монитор блокировок Windows";
            title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            Ui.Place(header, title, 20, 9, 400, 30);
            EnableDrag(header);
            EnableDrag(title);
            RButton close = Ui.CloseBtn();
            Ui.Place(header, close, 856, 5, 36, 34);
            close.Click += delegate { Close(); };

            Label explanation = new Label();
            explanation.Text = "Автообновление каждые 3 секунды. Найденные политики и IFEO можно снять; перед изменением создаётся резервная копия.";
            explanation.Font = Theme.Small;
            explanation.ForeColor = Theme.Sub;
            Ui.Place(this, explanation, 20, 72, 860, 34);

            entries = new ListView();
            entries.View = View.Details;
            entries.FullRowSelect = true;
            entries.GridLines = false;
            entries.HideSelection = false;
            entries.MultiSelect = true;
            entries.BorderStyle = BorderStyle.FixedSingle;
            entries.BackColor = Color.FromArgb(18, 19, 21);
            entries.ForeColor = Theme.Text;
            entries.Font = Theme.Small;
            entries.Columns.Add("Системный инструмент", 190);
            entries.Columns.Add("Обнаруженная блокировка", 420);
            entries.Columns.Add("Источник", 250);
            Ui.StyleDarkList(entries);
            entries.Scrollable = true;
            Ui.Place(this, entries, 20, 118, 860, 340);
            entries.Resize += delegate { FitIfeoColumns(); };
            FitIfeoColumns();
            entries.MouseUp += EntriesMouseUp;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Снять выбранные ограничения…", null, delegate { RemoveSelectedRestrictions(); });
            entries.ContextMenuStrip = menu;

            status = new Label();
            status.Font = Theme.Small;
            status.ForeColor = Theme.Sub;
            Ui.Place(this, status, 20, 468, 860, 24);

            RButton remove = RButton.Secondary("Снять выбранные");
            Ui.Place(this, remove, 340, 498, 170, 32);
            remove.Click += delegate { RemoveSelectedRestrictions(); };

            RButton removeAll = RButton.Secondary("Снять все найденные");
            Ui.Place(this, removeAll, 520, 498, 170, 32);
            removeAll.Click += delegate { RemoveAllRestrictions(); };

            RButton scan = RButton.Secondary("Обновить");
            Ui.Place(this, scan, 710, 498, 170, 32);
            scan.Click += delegate { Scan(); };
            Scan();
            refreshTimer = new WinFormsTimer();
            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate { Scan(); };
            refreshTimer.Start();
        }

        private void FitIfeoColumns()
        {
            if (entries == null || entries.Columns.Count < 3) return;
            int available = Math.Max(300,
                entries.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            entries.Columns[0].Width = Math.Max(150, (int)(available * 0.24));
            entries.Columns[1].Width = Math.Max(220, (int)(available * 0.46));
            entries.Columns[2].Width = Math.Max(120,
                available - entries.Columns[0].Width - entries.Columns[1].Width);
        }

        private void EntriesMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewHitTestInfo hit = entries.HitTest(e.Location);
            if (hit.Item == null) return;
            if (!hit.Item.Selected)
            {
                entries.SelectedItems.Clear();
                hit.Item.Selected = true;
            }
        }

        private List<RestrictionEntry> GetSelectedEntries()
        {
            List<RestrictionEntry> selected = new List<RestrictionEntry>();
            foreach (ListViewItem item in entries.SelectedItems)
            {
                RestrictionEntry entry = item.Tag as RestrictionEntry;
                if (entry != null) selected.Add(entry);
            }
            return selected;
        }

        private bool BackupRestriction(RestrictionEntry entry, out string backupPath, out string error)
        {
            backupPath = null;
            error = null;
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup", "Restrictions");
                Directory.CreateDirectory(directory);
                string safeName = entry.DisplayName + "_" + entry.ValueName;
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    safeName = safeName.Replace(invalid, '_');
                safeName = safeName.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                backupPath = Path.Combine(directory, safeName + "_" + stamp + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) + ".reg");
                string output;
                int exitCode = Sys.RunWithExitCode("reg.exe",
                    "export \"" + entry.RegistryPath + "\" \"" + backupPath + "\" /y", out output);
                if (exitCode != 0 || !File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                {
                    error = String.IsNullOrWhiteSpace(output)
                        ? "Экспорт реестра не создал резервную копию."
                        : output;
                    backupPath = null;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                backupPath = null;
                return false;
            }
        }

        private void RemoveSelectedRestrictions()
        {
            List<RestrictionEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                MessageBox.Show("Сначала выберите одно или несколько найденных ограничений.",
                    "Монитор блокировок", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RemoveRestrictions(selected, false);
        }

        private void RemoveAllRestrictions()
        {
            List<RestrictionEntry> found = Scan();
            if (found.Count == 0)
            {
                status.Text = "Найденных ограничений для снятия нет.";
                return;
            }
            RemoveRestrictions(found, true);
        }

        private void RemoveRestrictions(List<RestrictionEntry> targets, bool removeAll)
        {
            string scope = removeAll ? "всех найденных" : "выбранных";
            if (MessageBox.Show(
                "Будут удалены обнаруженные значения политики/IFEO (" + targets.Count +
                ", " + scope + "). Для каждого раздела сначала создаётся .reg-копия. " +
                "Удаление IFEO может изменить запуск соответствующей программы. " +
                "Политики организации могут примениться снова после обновления групповых политик.\r\n\r\nПродолжить?",
                "Снять ограничения", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            int removed = 0, failed = 0;
            List<string> failures = new List<string>();
            foreach (RestrictionEntry entry in targets)
            {
                string backup, error;
                if (!BackupRestriction(entry, out backup, out error))
                {
                    failed++;
                    failures.Add(entry.DisplayName + " (" + entry.ValueName +
                        "): резервная копия не создана (" + error + ")");
                    continue;
                }

                try
                {
                    RegistryKey hive;
                    string subKey;
                    GetRegistryLocation(entry.RegistryPath, out hive, out subKey);
                    using (RegistryKey key = hive.OpenSubKey(subKey, true))
                    {
                        if (key == null)
                            throw new InvalidOperationException("Раздел реестра больше не существует.");
                        string current = Convert.ToString(key.GetValue(entry.ValueName));
                        if (!String.Equals(current, entry.Value, StringComparison.Ordinal))
                            throw new InvalidOperationException("Значение изменилось после сканирования; обновите список.");
                        key.DeleteValue(entry.ValueName, false);
                    }
                    removed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(entry.DisplayName + " (" + entry.ValueName +
                        "): " + ex.Message + " (копия: " + backup + ")");
                }
            }

            Scan();
            string summary = "Снято значений ограничений: " + removed + ". Ошибок: " + failed + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\n" + String.Join("\r\n", failures.ToArray());
            if (removed > 0)
                summary += "\r\n\r\nРезервные копии находятся в папке Backup\\Restrictions рядом с DMTeam.";
            if (failed > 0)
                summary += "\r\n\r\nНекоторые значения защищены Windows или управляются политикой организации.";
            ShowRemovalResult(summary, failures);
        }

        private void GetRegistryLocation(string registryPath, out RegistryKey hive, out string subKey)
        {
            if (registryPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            {
                hive = Registry.CurrentUser;
                subKey = registryPath.Substring(5);
                return;
            }
            if (registryPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            {
                hive = Registry.LocalMachine;
                subKey = registryPath.Substring(5);
                return;
            }
            throw new InvalidOperationException("Неподдерживаемый раздел реестра: " + registryPath);
        }

        private void ShowRemovalResult(string summary, List<string> failures)
        {
            Form result = new Form();
            result.Text = "Результат снятия ограничений";
            result.ClientSize = new Size(620, 410);
            result.StartPosition = FormStartPosition.CenterParent;
            result.FormBorderStyle = FormBorderStyle.None;
            result.BackColor = Theme.Bg;
            result.ForeColor = Theme.Text;

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(result, header, 0, 0, 620, 54);
            Label heading = new Label();
            heading.Text = "Результат разблокировки";
            heading.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            heading.ForeColor = Theme.Text;
            Ui.Place(header, heading, 18, 10, 500, 30);
            RButton close = Ui.CloseBtn();
            Ui.Place(header, close, 576, 5, 36, 34);
            close.Click += delegate { result.Close(); };

            Label summaryLabel = new Label();
            summaryLabel.Text = summary.Split(new string[] { "\r\n\r\n" }, StringSplitOptions.None)[0];
            summaryLabel.Font = Theme.Ui;
            summaryLabel.ForeColor = failures.Count == 0 ? Theme.Green : Theme.Warn;
            Ui.Place(result, summaryLabel, 20, 70, 580, 34);

            TextBox details = new TextBox();
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical;
            details.WordWrap = true;
            details.BorderStyle = BorderStyle.FixedSingle;
            details.BackColor = Color.FromArgb(18, 19, 21);
            details.ForeColor = Theme.Text;
            details.Font = Theme.Small;
            int detailStart = summary.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            details.Text = detailStart >= 0
                ? summary.Substring(detailStart + 4)
                : "Все выбранные значения ограничений сняты.";
            Ui.Place(result, details, 20, 112, 580, 230);

            RButton ok = RButton.Secondary("Понятно");
            Ui.Place(result, ok, 460, 356, 140, 36);
            ok.Click += delegate { result.Close(); };
            result.AcceptButton = ok;
            result.CancelButton = ok;
            result.Shown += delegate { Ui.SetRoundedWindow(result, 14); };
            result.ShowDialog(this);
        }

        private List<RestrictionEntry> Scan()
        {
            List<string> selected = new List<string>();
            foreach (ListViewItem item in entries.SelectedItems)
            {
                RestrictionEntry entry = item.Tag as RestrictionEntry;
                if (entry != null)
                    selected.Add(entry.RegistryPath + "\0" + entry.ValueName);
            }
            entries.Items.Clear();
            List<RestrictionEntry> found = new List<RestrictionEntry>();
            int failures = 0;
            string[] ifeoRoots = {
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"
            };
            foreach (string rootPath in ifeoRoots)
            {
                try
                {
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(rootPath))
                    {
                        if (root == null) continue;
                        foreach (string imageName in root.GetSubKeyNames())
                        {
                            string subKey = rootPath + "\\" + imageName;
                            try
                            {
                                using (RegistryKey imageKey = root.OpenSubKey(imageName))
                                {
                                    if (imageKey == null) continue;
                                    string debugger = Convert.ToString(imageKey.GetValue("Debugger"));
                                    if (String.IsNullOrWhiteSpace(debugger)) continue;
                                    AddRestriction(found, imageName, @"HKLM\" + subKey,
                                        "Debugger", debugger, "IFEO Debugger: " + debugger);
                                }
                            }
                            catch (System.Security.SecurityException) { failures++; }
                            catch (UnauthorizedAccessException) { failures++; }
                            catch (IOException) { failures++; }
                        }
                    }
                }
                catch (System.Security.SecurityException) { failures++; }
                catch (UnauthorizedAccessException) { failures++; }
                catch (IOException) { failures++; }
            }

            string systemPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
            string explorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            string windowsSystemPolicies = @"Software\Policies\Microsoft\Windows\System";
            string windowsExplorerPolicies = @"Software\Policies\Microsoft\Windows\Explorer";
            string[][] policyDefinitions = {
                new string[] { "Диспетчер задач", systemPolicies, "DisableTaskMgr" },
                new string[] { "Редактор реестра", systemPolicies, "DisableRegistryTools" },
                new string[] { "Командная строка", systemPolicies, "DisableCMD" },
                new string[] { "Диспетчер задач", windowsSystemPolicies, "DisableTaskMgr" },
                new string[] { "Редактор реестра", windowsSystemPolicies, "DisableRegistryTools" },
                new string[] { "Командная строка", windowsSystemPolicies, "DisableCMD" },
                new string[] { "Смена пароля", systemPolicies, "DisableChangePassword" },
                new string[] { "Блокировка сеанса", systemPolicies, "DisableLockWorkstation" },
                new string[] { "Смена пользователя", systemPolicies, "DisableSwitchUserOption" },
                new string[] { "Панель управления", explorerPolicies, "NoControlPanel" },
                new string[] { "Панель управления", windowsExplorerPolicies, "NoControlPanel" },
                new string[] { "Параметры Windows", explorerPolicies, "SettingsPageVisibility" },
                new string[] { "Параметры Windows", windowsExplorerPolicies, "SettingsPageVisibility" },
                new string[] { "Выполнить", explorerPolicies, "NoRun" },
                new string[] { "Выполнить", windowsExplorerPolicies, "NoRun" },
                new string[] { "Поиск Windows", explorerPolicies, "NoFind" },
                new string[] { "Параметры папок", explorerPolicies, "NoFolderOptions" },
                new string[] { "Контекстное меню панели задач", explorerPolicies, "NoTrayContextMenu" },
                new string[] { "Контекстное меню проводника", explorerPolicies, "NoViewContextMenu" },
                new string[] { "Команда «Завершение работы»", explorerPolicies, "NoClose" },
                new string[] { "Выход из системы", explorerPolicies, "NoLogoff" },
                new string[] { "Сетевые подключения", explorerPolicies, "NoNetworkConnections" },
                new string[] { "Сетевое окружение", explorerPolicies, "NoNetHood" }
            };
            string[] policyHives = { "HKCU", "HKLM" };
            foreach (string hive in policyHives)
            {
                foreach (string[] definition in policyDefinitions)
                {
                    string registryPath = hive + "\\" + definition[1];
                    AddPolicyLock(found, definition[0], registryPath, definition[2], ref failures);
                }
            }

            foreach (ListViewItem item in entries.Items)
            {
                RestrictionEntry entry = item.Tag as RestrictionEntry;
                if (entry != null &&
                    selected.Contains(entry.RegistryPath + "\0" + entry.ValueName))
                    item.Selected = true;
            }
            status.Text = found.Count == 0
                ? "Активных ограничений в отслеживаемых политиках и IFEO не найдено."
                : "Найдено ограничений: " + found.Count +
                    (failures > 0 ? ". Недоступно для чтения: " + failures + "." : ".");
            if (found.Count == 0 && failures > 0)
                status.Text += " Недоступно для чтения: " + failures + ".";
            return found;
        }

        private void AddPolicyLock(List<RestrictionEntry> found, string toolName,
            string registryPath, string valueName, ref int failures)
        {
            RegistryKey hive;
            string subKey;
            GetRegistryLocation(registryPath, out hive, out subKey);
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKey))
                {
                    if (key == null) return;
                    object value = key.GetValue(valueName);
                    if (!IsRestrictionEnabled(value)) return;
                    AddRestriction(found, toolName, registryPath, valueName,
                        Convert.ToString(value), "Политика Windows: " + valueName + " = " + value);
                }
            }
            catch (System.Security.SecurityException) { failures++; }
            catch (UnauthorizedAccessException) { failures++; }
            catch (IOException) { failures++; }
        }

        private bool IsRestrictionEnabled(object value)
        {
            if (value == null) return false;
            if (value is int) return (int)value != 0;
            if (value is long) return (long)value != 0;
            string text = Convert.ToString(value);
            return !String.IsNullOrWhiteSpace(text) && text != "0";
        }

        private void AddRestriction(List<RestrictionEntry> found, string displayName,
            string registryPath, string valueName, string value, string description)
        {
            RestrictionEntry entry = new RestrictionEntry(displayName, registryPath,
                valueName, value);
            ListViewItem row = new ListViewItem(displayName);
            row.SubItems.Add(description);
            row.SubItems.Add(registryPath);
            row.Tag = entry;
            entries.Items.Add(row);
            found.Add(entry);
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                refreshTimer = null;
            }
            base.OnFormClosed(e);
        }
    }

    public class ToolsWindow : Form
    {
        private Point dragStart;
        private Point formStart;
        private bool dragging;
        private Label accentStatus;

        public ToolsWindow()
        {
            Text = "DMTeam — Инструменты";
            ClientSize = new Size(560, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;

            Panel top = new Panel();
            top.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, top, 0, 0, 560, 64);

            Label heading = new Label();
            heading.Text = "Инструменты";
            heading.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            heading.ForeColor = Theme.Text;
            Ui.Place(top, heading, 20, 8, 300, 28);
            EnableDrag(top);
            EnableDrag(heading);

            RButton close = Ui.CloseBtn();
            Ui.Place(top, close, 518, 5, 36, 34);
            close.Click += delegate { Close(); };

            RButton taskManager = RButton.Secondary("Диспетчер процессов");
            Ui.Place(this, taskManager, 20, 84, 250, 68);
            taskManager.Click += delegate { new ProcessWindow().ShowDialog(this); };

            RButton customize = RButton.Secondary("Кастомизация");
            Ui.Place(this, customize, 290, 84, 250, 68);
            customize.Click += delegate { new CustomizationWindow().ShowDialog(this); };

            RButton disableUpdate = RButton.Secondary("Отключить Windows Update");
            Ui.Place(this, disableUpdate, 20, 166, 250, 68);
            disableUpdate.Click += delegate
            {
                if (MessageBox.Show("Отключить Windows Update принудительно?", "DMTeam",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                ILog log = new StatusLog(accentStatus);
                Ops.ForceDisableWindowsUpdate(log);
            };

            RButton disableSecurity = RButton.Secondary("Отключить Windows Security");
            Ui.Place(this, disableSecurity, 290, 166, 250, 68);
            disableSecurity.Click += delegate
            {
                if (MessageBox.Show("Отключить Windows Security принудительно?", "DMTeam",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                ILog log = new StatusLog(accentStatus);
                Ops.ForceDisableWindowsSecurity(log);
            };

            RButton disableBoth = RButton.Secondary("Отключить обновления + безопасность");
            Ui.Place(this, disableBoth, 20, 248, 520, 54);
            disableBoth.Click += delegate
            {
                if (MessageBox.Show("Отключить и Windows Update, и Windows Security?", "DMTeam",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                ILog log = new StatusLog(accentStatus);
                Ops.ForceDisableUpdatesAndSecurity(log);
            };

            RButton restoreSecurity = RButton.Secondary("Вернуть Windows Security");
            Ui.Place(this, restoreSecurity, 20, 310, 250, 42);
            restoreSecurity.Click += delegate
            {
                if (MessageBox.Show("Включить обратно Windows Security, SmartScreen и UAC?",
                    "DMTeam", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Ops.RestoreWindowsSecurity(new StatusLog(accentStatus));
            };

            RButton restoreSearch = RButton.Secondary("Вернуть поиск Windows");
            Ui.Place(this, restoreSearch, 290, 310, 250, 42);
            restoreSearch.Click += delegate
            {
                if (MessageBox.Show("Включить обратно службу поиска, Cortana и веб-поиск?",
                    "DMTeam", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Ops.RestoreWindowsSearch(new StatusLog(accentStatus));
            };

            RButton restoreDefaults = RButton.Secondary("Вернуть настройки по умолчанию");
            Ui.Place(this, restoreDefaults, 20, 358, 520, 42);
            restoreDefaults.Click += delegate
            {
                if (MessageBox.Show("Сбросить прозрачность, расширения файлов, гибернацию, питание, рекламу, Game DVR и сеть к значениям Windows по умолчанию?",
                    "DMTeam", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Ops.RestoreDefaultSettings(new StatusLog(accentStatus));
            };

            RButton restoreAll = RButton.Primary("Полное восстановление");
            Ui.Place(this, restoreAll, 20, 406, 520, 46);
            restoreAll.Click += delegate
            {
                if (MessageBox.Show("Выполнить полное восстановление Windows: безопасность, поиск, настройки и очистка блокировки Windows Update в hosts?",
                    "DMTeam", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                Ops.RestoreEverything(new StatusLog(accentStatus));
            };

            accentStatus = new Label();
            accentStatus.Text = "Готово";
            accentStatus.ForeColor = Theme.Sub;
            accentStatus.Font = Theme.Small;
            accentStatus.TextAlign = ContentAlignment.MiddleCenter;
            Ui.Place(this, accentStatus, 20, 470, 520, 24);
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }
    }

    public class CustomizationWindow : Form
    {
        private Label currentColor;
        private Label colorCode;
        private Label status;
        private Panel previewSwatch;
        private readonly List<PaletteButton> paletteButtons = new List<PaletteButton>();
        private Point dragStart;
        private Point formStart;
        private bool dragging;

        private sealed class PaletteButton : Button
        {
            public Color ColorValue;
            public string ColorName;
            public bool Selected;
            private bool hover;

            public PaletteButton(Color color, string name)
            {
                ColorValue = color;
                ColorName = name;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Theme.Bg;
                ForeColor = Theme.Text;
                Cursor = Cursors.Hand;
                TabStop = true;
            }

            public bool MatchesColor(Color color)
            {
                return ColorValue.ToArgb() == color.ToArgb();
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hover = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                Color fillColor = hover ? Color.FromArgb(34, 35, 38) : Color.FromArgb(23, 24, 26);
                using (GraphicsPath path = Ui.RoundedPath(bounds, 8))
                using (SolidBrush fill = new SolidBrush(fillColor))
                using (Pen border = new Pen(Selected ? Theme.Accent : Color.FromArgb(42, 43, 46),
                    Selected ? 1.4f : 1f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                using (SolidBrush swatch = new SolidBrush(ColorValue))
                using (Pen swatchBorder = new Pen(Color.FromArgb(62, 64, 68), 1f))
                {
                    g.FillEllipse(swatch, 14, (Height - 22) / 2, 22, 22);
                    g.DrawEllipse(swatchBorder, 14, (Height - 22) / 2, 22, 22);
                }

                TextRenderer.DrawText(g, ColorName, Theme.Small,
                    new Rectangle(46, 0, Width - 72, Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                if (Selected)
                {
                    using (Pen checkPen = new Pen(Theme.Accent, 1.8f))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        g.DrawLine(checkPen, Width - 20, Height / 2,
                            Width - 16, Height / 2 + 4);
                        g.DrawLine(checkPen, Width - 16, Height / 2 + 4,
                            Width - 9, Height / 2 - 5);
                    }
                }
            }
        }

        public CustomizationWindow()
        {
            Text = "DMTeam — Кастомизация";
            ClientSize = new Size(600, 438);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, header, 0, 0, 600, 64);

            Label heading = new Label();
            heading.Text = "Кастомизация";
            heading.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            heading.ForeColor = Theme.Text;
            Ui.Place(header, heading, 20, 8, 300, 28);
            EnableDrag(header);
            EnableDrag(heading);

            RButton close = Ui.CloseBtn();
            Ui.Place(header, close, 556, 5, 36, 34);
            close.Click += delegate { Close(); };

            Label description = new Label();
            description.Text = "Настройте акцентный цвет интерфейса";
            description.Font = Theme.Small;
            description.ForeColor = Theme.Sub;
            Ui.Place(this, description, 24, 78, 552, 26);

            Panel preview = new Panel();
            preview.BackColor = Theme.Card;
            Ui.Place(this, preview, 24, 112, 552, 62);

            previewSwatch = new Panel();
            previewSwatch.BackColor = Theme.Accent;
            Ui.Place(preview, previewSwatch, 16, 15, 32, 32);

            currentColor = new Label();
            currentColor.Text = "Акцентный цвет интерфейса";
            currentColor.TextAlign = ContentAlignment.MiddleLeft;
            currentColor.Font = Theme.Small;
            currentColor.ForeColor = Theme.Text;
            Ui.Place(preview, currentColor, 62, 8, 330, 22);

            colorCode = new Label();
            colorCode.TextAlign = ContentAlignment.MiddleLeft;
            colorCode.Font = new Font("Consolas", 9.5f, FontStyle.Regular);
            colorCode.ForeColor = Theme.Sub;
            Ui.Place(preview, colorCode, 62, 32, 180, 19);

            Color[] palette = {
                Color.FromArgb(150, 157, 165),
                Color.FromArgb(74, 144, 226),
                Color.FromArgb(155, 110, 220),
                Color.FromArgb(66, 180, 125),
                Color.FromArgb(230, 155, 65),
                Color.FromArgb(220, 85, 95)
            };
            string[] names = { "Серый", "Синий", "Фиолетовый", "Зелёный", "Оранжевый", "Красный" };
            Label paletteTitle = new Label();
            paletteTitle.Text = "ЦВЕТОВАЯ ПАЛИТРА";
            paletteTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            paletteTitle.ForeColor = Theme.Sub;
            Ui.Place(this, paletteTitle, 24, 190, 552, 18);

            for (int i = 0; i < palette.Length; i++)
            {
                Color color = palette[i];
                PaletteButton swatch = new PaletteButton(color, names[i]);
                int column = i % 3;
                int row = i / 3;
                swatch.Location = new Point(24 + column * 188, 216 + row * 62);
                swatch.Size = new Size(176, 52);
                swatch.Selected = swatch.MatchesColor(Theme.Accent);
                swatch.Click += delegate { ApplyColor(color); };
                paletteButtons.Add(swatch);
                Controls.Add(swatch);
            }

            RButton custom = RButton.Primary("Свой цвет…");
            custom.Font = Theme.Small;
            Ui.Place(this, custom, 24, 352, 260, 40);
            custom.Click += delegate { ChooseCustomColor(); };

            RButton reset = RButton.Secondary("Сбросить");
            reset.Font = Theme.Small;
            Ui.Place(this, reset, 316, 352, 260, 40);
            reset.Click += delegate { ApplyColor(Color.FromArgb(150, 157, 165)); };

            status = new Label();
            status.Text = "Цвет сохраняется автоматически";
            status.Font = Theme.Small;
            status.ForeColor = Theme.Sub;
            status.TextAlign = ContentAlignment.MiddleCenter;
            Ui.Place(this, status, 24, 402, 552, 24);
            UpdatePreview(Theme.Accent);
        }

        private void ChooseCustomColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = Theme.Accent;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    ApplyColor(dialog.Color);
            }
        }

        private void ApplyColor(Color color)
        {
            try
            {
                Theme.SaveAccent(color);
                UpdatePreview(color);
                foreach (PaletteButton button in paletteButtons)
                {
                    button.Selected = button.MatchesColor(color);
                    button.Invalidate();
                }
                status.Text = "Цвет сохранён";
                status.ForeColor = Theme.Green;
                Invalidate(true);
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось сохранить цвет: " + ex.Message;
                status.ForeColor = Theme.Red;
            }
        }

        private void UpdatePreview(Color color)
        {
            previewSwatch.BackColor = color;
            colorCode.Text = "#" + color.R.ToString("X2") +
                color.G.ToString("X2") + color.B.ToString("X2");
            currentColor.Text = "Пользовательский цвет";
            foreach (PaletteButton button in paletteButtons)
            {
                if (!button.MatchesColor(color)) continue;
                currentColor.Text = button.ColorName;
                break;
            }
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X,
                    formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }
    }

    // ---------------- Окно оптимизации ----------------
    public class OptimizationWindow : Form, IMessageFilter
    {
        private Point dragStart;
        private int scrollOffset;
        private int contentHeight;
        private Point formStart;
        private bool dragging;
        private readonly List<OptItem> items = new List<OptItem>();
        private readonly Dictionary<OptItem, bool> selectedItems = new Dictionary<OptItem, bool>();
        private readonly List<SectionButton> sectionButtons = new List<SectionButton>();
        private Panel actionList;
        private Label sectionHeading;
        private Label status;
        private Label selectionCount;
        private SectionButton selectSection;
        private string activeSection;
        private Button runBtn;
        private ToolTip actionToolTip;
        ILog log;

        public OptimizationWindow()
        {
            Text = "DMTeam — Настройка системы";
            ClientSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            actionToolTip = new ToolTip();
            actionToolTip.BackColor = Theme.Card;
            actionToolTip.ForeColor = Theme.Text;
            Build();
            Application.AddMessageFilter(this);
        }

        private OptItem Add(string group, string text, string desc, bool on, Action<ILog> run)
        {
            OptItem it = new OptItem();
            it.Text = text;
            it.Desc = desc;
            it.DefaultOn = on;
            it.Run = run;
            it.Group = group;
            items.Add(it);
            return it;
        }

        private void Build()
        {
            Add("ПОДГОТОВКА", "Точка восстановления", "Создать резервную точку перед изменениями", false, Ops.CreateRestorePoint);
            Add("ОЧИСТКА", "Временные файлы", "Пользовательские и системные Temp-каталоги", true, Ops.CleanTemp);
            Add("ОЧИСТКА", "Корзина", "Очистить на всех дисках", true, Ops.CleanRecycleBin);
            Add("ОЧИСТКА", "DNS-кэш", "ipconfig /flushdns", true, Ops.CleanDns);
            Add("ОЧИСТКА", "Кэш обновлений Windows", "SoftwareDistribution\\Download", true, Ops.CleanWindowsUpdate);
            Add("ОЧИСТКА", "Кэш эскизов", "Удалить только файлы thumbcache_*.db", true, Ops.CleanThumbnailCache);
            Add("ОЧИСТКА", "Кэш графики", "DirectX, NVIDIA, AMD и Intel; после очистки может временно тормозить", true, Ops.CleanGraphicsCache);
            Add("ОЧИСТКА", "Отчёты об ошибках", "Очередь и архив WER, не системные журналы", false, Ops.CleanErrorReports);
            Add("ОЧИСТКА", "Кэш значков", "Удалить только файлы iconcache_*.db", true, Ops.CleanIconCache);
            Add("ОЧИСТКА", "Журналы Windows", "Каталоги Logs и Panther", true, Ops.CleanWindowsLogs);
            Add("ОЧИСТКА", "Дампы памяти", "MEMORY.DMP, Minidump и CrashDumps", true, Ops.CleanMemoryDumps);
            Add("ОЧИСТКА", "Файлы оптимизации доставки", "Delete-DeliveryOptimizationCache", true, Ops.CleanDeliveryOptimization);
            Add("ОЧИСТКА", "Недавние документы и переходы", "Recent и jump lists", false, Ops.CleanRecentItems);
            Add("ОЧИСТКА", "Кэш Microsoft Store", "Локальный кэш магазина", false, Ops.CleanStoreCache);
            Add("ОЧИСТКА", "Предзагрузка (Prefetch)", "Файлы ускорения запуска приложений", true, Ops.CleanPrefetch);
            Add("ОЧИСТКА", "Кэш шрифтов", "FontCache", true, Ops.CleanFontCache);
            Add("ОЧИСТКА", "Кэш браузеров", "Все профили; после очистки первая загрузка может быть медленнее", false, Ops.CleanBrowserCaches);
            Add("ОЧИСТКА", "Кэш диагностики", "Каталог Microsoft\\Diagnosis", true, Ops.CleanDiagnosisCache);
            Add("ОЧИСТКА", "Очередь печати", "spool\\PRINTERS", true, Ops.CleanPrintQueue);
            Add("ОЧИСТКА", "Системные временные файлы", "SystemTemp и логи DataStore", true, Ops.CleanSystemTemp);
            Add("ОЧИСТКА", "Кэш поиска Windows", "LocalState поиска", true, Ops.CleanSearchCache);
            Add("ОЧИСТКА", "TempState приложений Магазина", "Временные данные UWP-приложений", false, Ops.CleanUwpTempState);
            Add("ОЧИСТКА", "Downloaded Program Files", "Устаревшие загруженные элементы", true, Ops.CleanDownloadedProgramFiles);
            Add("ОЧИСТКА", "Temp во всех профилях", "AppData\\Local\\Temp всех пользователей", false, Ops.CleanAllUserTemps);
            Add("ОЧИСТКА", "Удалить необязательные приложения Microsoft", "Только текущий пользователь; системные компоненты, Store, Edge и Защитник сохраняются", false, Ops.RemoveOptionalMicrosoftApps);
            Add("СЛУЖБЫ И ТЕЛЕМЕТРИЯ", "Телеметрия", "Изменить параметры сбора данных Windows", true, Ops.DisableTelemetry);
            Add("СЛУЖБЫ И ТЕЛЕМЕТРИЯ", "Cortana и веб-поиск", "меню Пуск", true, Ops.DisableCortana);
            Add("СЛУЖБЫ И ТЕЛЕМЕТРИЯ", "Лишние службы", "Xbox, Fax, Maps, RetailDemo и др.", true, Ops.DisableServices);
            Add("СЛУЖБЫ И ТЕЛЕМЕТРИЯ", "Xbox Game Bar", "Game Bar и Game DVR", true, Ops.DisableXboxGameBar);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Показывать расширения файлов", "и скрытые файлы", true, Ops.TweakExplorer);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Отключить прозрачность", "окон", true, Ops.TweakTransparency);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Визуальные эффекты → быстродействие", "", true, Ops.TweakAnimations);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Отключить рекламу", "реклама и предложения в Windows", true, Ops.TweakAds);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Отключить гибернацию", "удалить hiberfil.sys", true, Ops.TweakHibernate);
            Add("НАСТРОЙКИ СИСТЕМЫ", "Макс. производительность", "схема питания", true, Ops.TweakPower);
            Add("СЕТЬ", "Оптимизация TCP", "autotuning, RSS, ECN", true, Ops.NetOptimize);
            Add("АВТОЗАГРУЗКА", "Удалить всю автозагрузку", "Run/RunOnce и папки Startup; перед удалением создаётся резервная копия", false, Ops.RemoveAllAutoruns);
            Add("ПЛАНИРОВЩИК", "Задания телеметрии", "Отключить только 9 известных задач сбора данных; остальные не трогаются", false, Ops.DisableTelemetryTasks);

            Panel top = new Panel();
            top.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, top, 0, 0, 820, 70);

            Label heading = new Label();
            heading.Text = "DMTeam";
            heading.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            heading.ForeColor = Theme.Text;
            Ui.Place(top, heading, 20, 8, 240, 27);

            RButton closeWindow = Ui.CloseBtn();
            closeWindow.Size = new Size(36, 34);
            closeWindow.Location = new Point(774, 5);
            closeWindow.Click += delegate { Close(); };
            top.Controls.Add(closeWindow);
            EnableDrag(top);
            EnableDrag(heading);

            Panel sidebar = new Panel();
            sidebar.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, sidebar, 0, 70, 205, 470);

            string[] sections = { "Подготовка", "Очистка", "Приватность", "Система", "Сеть", "Автозагрузка", "Планировщик" };
            int sectionY = 20;
            foreach (string section in sections)
            {
                SectionButton button = new SectionButton();
                button.LabelText = section;
                button.CountText = CountInSection(section).ToString();
                button.Location = new Point(10, sectionY);
                button.Size = new Size(185, 40);
                button.SectionClicked += delegate { ShowSection(section); };
                sidebar.Controls.Add(button);
                sectionButtons.Add(button);
                sectionY += 44;
            }

            Panel content = new Panel();
            content.BackColor = Theme.Bg;
            Ui.Place(this, content, 205, 70, 615, 470);

            sectionHeading = new Label();
            sectionHeading.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
            sectionHeading.ForeColor = Theme.Text;
            Ui.Place(content, sectionHeading, 25, 18, 335, 32);

            selectSection = new SectionButton();
            selectSection.LabelText = "Выбрать все";
            selectSection.CountText = "";
            selectSection.BackColor = Theme.Bg;
            selectSection.Location = new Point(392, 17);
            selectSection.Size = new Size(195, 34);
            selectSection.SectionClicked += delegate { ToggleSectionSelection(); };
            content.Controls.Add(selectSection);

            actionList = new Panel();
            actionList.BackColor = Theme.Bg;
            actionList.Location = new Point(25, 68);
            actionList.Size = new Size(565, 397);
            actionList.AutoScroll = false;
            content.Controls.Add(actionList);

            Panel footer = new Panel();
            footer.BackColor = Color.FromArgb(17, 18, 19);
            Ui.Place(this, footer, 0, 540, 820, 80);

            status = new Label();
            status.Text = "Готово";
            status.ForeColor = Theme.Sub;
            status.Font = Theme.Small;
            status.AutoEllipsis = true;
            Ui.Place(footer, status, 20, 8, 480, 18);
            log = new StatusLog(status);

            selectionCount = new Label();
            selectionCount.ForeColor = Theme.Sub;
            selectionCount.Font = Theme.Small;
            selectionCount.TextAlign = ContentAlignment.MiddleRight;
            Ui.Place(footer, selectionCount, 520, 8, 280, 18);

            runBtn = RButton.Primary("Применить выбранное");
            Ui.Place(footer, runBtn, 20, 35, 270, 34);
            runBtn.Click += delegate { RunSelected(); };

            RButton full = RButton.Secondary("Полная настройка");
            Ui.Place(footer, full, 298, 35, 230, 34);
            full.Click += delegate { FullOptimize(); };

            RButton close = RButton.Secondary("Назад");
            Ui.Place(footer, close, 536, 35, 264, 34);
            close.Click += delegate { Close(); };

            Controls.Add(sidebar);
            Controls.Add(content);
            Controls.Add(footer);
            ShowSection("Подготовка");
        }

        private void EnableDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point current = Cursor.Position;
                Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (Pen border = new Pen(Color.FromArgb(61, 63, 67), 1.5f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Ui.SetRoundedWindow(this, 14);
        }

        private string GroupForSection(string section)
        {
            switch (section)
            {
                case "Подготовка": return "ПОДГОТОВКА";
                case "Очистка": return "ОЧИСТКА";
                case "Приватность": return "СЛУЖБЫ И ТЕЛЕМЕТРИЯ";
                case "Система": return "НАСТРОЙКИ СИСТЕМЫ";
                case "Сеть": return "СЕТЬ";
                case "Автозагрузка": return "АВТОЗАГРУЗКА";
                case "Планировщик": return "ПЛАНИРОВЩИК";
                default: return "";
            }
        }

        private int CountInSection(string section)
        {
            string group = GroupForSection(section);
            int count = 0;
            foreach (OptItem item in items)
                if (item.Group == group) count++;
            return count;
        }

        private void ShowSection(string section)
        {
            activeSection = section;
            foreach (SectionButton button in sectionButtons)
            {
                button.Active = button.LabelText == section;
                button.Invalidate();
            }

            string groupName = GroupForSection(section);
            List<OptItem> sectionItems = new List<OptItem>();
            foreach (OptItem item in items)
                if (item.Group == groupName) sectionItems.Add(item);
            sectionHeading.Text = section;
            actionList.Controls.Clear();

            int y = 0;
            foreach (OptItem item in sectionItems)
            {
                ActionRow row = new ActionRow();
                row.ActionName = item.Text;
                row.Selected = selectedItems.ContainsKey(item) ? selectedItems[item] : item.DefaultOn;
                row.Location = new Point(0, y);
                row.Size = new Size(560, 46);
                row.Tag = y;
                actionToolTip.SetToolTip(row, item.Desc);
                row.SelectedChanged += delegate { selectedItems[item] = row.Selected; UpdateSelectionCount(); };
                actionList.Controls.Add(row);
                y += 52;
            }
            contentHeight = y;
            scrollOffset = 0;
            UpdateSelectionCount();
        }

        private void ToggleSectionSelection()
        {
            string group = GroupForSection(activeSection);
            bool select = false;
            foreach (OptItem item in items)
                if (item.Group == group && !(selectedItems.ContainsKey(item) ? selectedItems[item] : item.DefaultOn))
                    select = true;
            foreach (OptItem item in items)
                if (item.Group == group) selectedItems[item] = select;
            ShowSection(activeSection);
        }

        private void UpdateSelectionCount()
        {
            int selected = 0;
            foreach (OptItem item in items)
                if (selectedItems.ContainsKey(item) ? selectedItems[item] : item.DefaultOn) selected++;
            selectionCount.Text = selected + " / " + items.Count;
            if (selectSection == null || String.IsNullOrEmpty(activeSection)) return;
            string group = GroupForSection(activeSection);
            bool all = true;
            foreach (OptItem item in items)
                if (item.Group == group && !(selectedItems.ContainsKey(item) ? selectedItems[item] : item.DefaultOn))
                    all = false;
            selectSection.LabelText = all ? "Снять выбор" : "Выбрать все";
            selectSection.Invalidate();
        }

        private void RunSelected()
        {
            List<OptItem> sel = new List<OptItem>();
            foreach (OptItem it in items)
                if (selectedItems.ContainsKey(it) ? selectedItems[it] : it.DefaultOn) sel.Add(it);

            if (sel.Count == 0) { log.Log("Ничего не выбрано.", LogLevel.Warn); return; }

            runBtn.Enabled = false;
            int done = 0;
            foreach (OptItem it in sel)
            {
                try { it.Run(log); }
                catch (Exception ex) { log.Log(ex.Message, LogLevel.Err); }
                done++;
            }
            status.Text = "Готово. Выполнено действий: " + done;
            status.ForeColor = Theme.Green;
            runBtn.Enabled = true;
        }

        private void FullOptimize()
        {
            if (MessageBox.Show(
                "Выполнить полную настройку?\nБудут выполнены только действия, включённые по умолчанию. Автозагрузка и необязательные приложения не затрагиваются; для них используйте отдельные пункты.\nПродолжить?",
                "Полная оптимизация", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            foreach (OptItem item in items)
                selectedItems[item] = item.DefaultOn;
            ShowSection(activeSection);
            RunSelected();
        }

        private void ScrollBy(int delta)
        {
            if (actionList == null) return;
            int max = Math.Max(0, contentHeight - actionList.ClientSize.Height);
            scrollOffset += delta;
            if (scrollOffset < 0) scrollOffset = 0;
            if (scrollOffset > max) scrollOffset = max;
            foreach (Control c in actionList.Controls)
                if (c.Tag is int) c.Top = (int)c.Tag - scrollOffset;
        }

        bool IMessageFilter.PreFilterMessage(ref System.Windows.Forms.Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL || actionList == null || !actionList.IsHandleCreated) return false;
            if (!actionList.ClientRectangle.Contains(actionList.PointToClient(Cursor.Position))) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            ScrollBy(delta > 0 ? -48 : 48);
            return true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            base.OnFormClosed(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.LoadAccent();
            Application.Run(new MainForm());
        }
    }
}