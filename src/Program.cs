using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BasaltConfigTool
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // 声明本进程为 DPI 感知，避免高分屏下界面被系统拉伸变糊
            try { SetProcessDPIAware(); }
            catch { /* 老系统没有这个 API，忽略即可，界面照常工作 */ }

            string dir = ResolveDir(args);

            // 无界面自检：验证关联扫描和 JSON 往返，然后把报告写到临时目录
            if (HasFlag(args, "--selftest"))
            {
                Environment.Exit(SelfTest(dir));
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Fail(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Fail(e.ExceptionObject as Exception);
            };

            Application.Run(new MainForm(dir));
        }

        /// <summary>找配置目录：优先用命令行给的路径，否则用程序自己所在的目录。</summary>
        static string ResolveDir(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a.StartsWith("--", StringComparison.Ordinal)) continue;
                    if (Directory.Exists(a)) return Path.GetFullPath(a);
                }
            }
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>兜底：把未处理的异常写进临时文件再弹框，免得只看到一个空标题。</summary>
        static void Fail(Exception ex)
        {
            string text = (ex == null ? "未知错误" : ex.ToString());
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "BasaltConfigTool-error.txt"),
                    text, new UTF8Encoding(false));
            }
            catch { }
            MessageBox.Show(text,
                "Basalt 配置编辑器 — 未处理的错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ------------------------------------------------------------- 自检

        static int SelfTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;
            log.AppendLine("目录 = " + dir);
            log.AppendLine();

            string[] files;
            try { files = Directory.GetFiles(dir, "*.json"); }
            catch (Exception ex) { files = new string[0]; log.AppendLine("列目录失败：" + ex.Message); }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            log.AppendLine("== 找到的 *.json（共 " + files.Length + " 个）==");
            for (int i = 0; i < files.Length; i++) log.AppendLine("  " + Path.GetFileName(files[i]));
            log.AppendLine();

            log.AppendLine("== 关联扫描 ==");
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                var cmds = Assoc.CmdsFor(dir, name);
                log.AppendLine("  " + name);
                if (cmds.Count == 0)
                {
                    log.AppendLine("     .cmd：（没有）");
                }
                else
                {
                    for (int k = 0; k < cmds.Count; k++)
                    {
                        log.AppendLine("     .cmd：" + Path.GetFileName(cmds[k].Path)
                                     + (cmds[k].ImplicitDefault ? "   [不传 -ConfigFile，用启动器默认值]" : ""));
                        var lnks = Assoc.ShortcutsFor(cmds[k].Path);
                        if (lnks.Count == 0) log.AppendLine("     .lnk：（桌面上没有）");
                        for (int j = 0; j < lnks.Count; j++)
                            log.AppendLine("     .lnk：" + Path.GetFileName(lnks[j]));
                    }
                }
            }
            log.AppendLine();

            log.AppendLine("== JSON 往返（解析 -> 序列化 -> 再解析 -> 逐项比对）==");
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                try
                {
                    string text = File.ReadAllText(files[i], Encoding.UTF8);
                    JsonNode a = Json.Parse(text);
                    string again = Json.Serialize(a);
                    JsonNode b = Json.Parse(again);
                    bool same = DeepEqual(a, b);
                    int topKeys = a.Members == null ? 0 : a.Members.Count;
                    int modelKeys = 0;
                    JsonNode models = a.Get("models");
                    if (models != null && models.Kind == JsonKind.Array && models.Items.Count > 0)
                        modelKeys = models.Items[0].Members.Count;

                    string problems = CheckCatalog(a);
                    string verdict = (same && problems == null) ? "通过" : "失败";
                    if (verdict == "失败") failures++;
                    log.AppendLine("  " + name + "  -> " + verdict);
                    log.AppendLine("      顶层键 " + topKeys + " 个，models[0] 键 " + modelKeys + " 个");

                    // 排版保真度：保存之后会改动几行
                    string nl = text.Replace("\r\n", "\n").TrimEnd('\n');
                    string nl2 = again.Replace("\r\n", "\n").TrimEnd('\n');
                    if (nl == nl2)
                    {
                        log.AppendLine("        排版：与原文件逐字符相同");
                    }
                    else
                    {
                        string[] la = nl.Split('\n');
                        string[] lb = nl2.Split('\n');
                        int diff = 0;
                        int max = Math.Max(la.Length, lb.Length);
                        for (int k = 0; k < max; k++)
                        {
                            string x = k < la.Length ? la[k] : "<无>";
                            string y = k < lb.Length ? lb[k] : "<无>";
                            if (x != y) diff++;
                        }
                        log.AppendLine("        排版：原 " + la.Length + " 行 / 新 " + lb.Length
                                     + " 行，不同 " + diff + " 行");
                    }

                    if (!same) log.AppendLine("      往返后结构不一致！");
                    if (problems != null) log.AppendLine("      文档校验：" + problems.Replace("\r\n", " "));
                }
                catch (Exception ex)
                {
                    failures++;
                    log.AppendLine("  " + name + "  -> 失败  " + ex.Message);
                }
            }

            log.AppendLine();
            log.AppendLine("== 字段目录 ==");
            log.AppendLine("  收录键数：" + Catalog.All.Count);
            log.AppendLine("  model 段：" + Catalog.ForSection(Section.ModelEntry).Count
                         + "  engine 段：" + Catalog.ForSection(Section.Engine).Count
                         + "  server 段：" + Catalog.ForSection(Section.Server).Count);

            log.AppendLine();
            log.AppendLine(failures == 0 ? "结论：全部通过" : ("结论：" + failures + " 项失败"));

            string outPath = Path.Combine(Path.GetTempPath(), "BasaltConfigTool-selftest.txt");
            File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
            return failures == 0 ? 0 : 1;
        }

        static bool DeepEqual(JsonNode a, JsonNode b)
        {
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Bool: return a.Flag == b.Flag;
                case JsonKind.String: return a.Text == b.Text;
                case JsonKind.Number: return a.Text == b.Text;
                case JsonKind.Null: return true;
                case JsonKind.Array:
                    if (a.Items.Count != b.Items.Count) return false;
                    for (int i = 0; i < a.Items.Count; i++)
                        if (!DeepEqual(a.Items[i], b.Items[i])) return false;
                    return true;
                case JsonKind.Object:
                    if (a.Members.Count != b.Members.Count) return false;
                    for (int i = 0; i < a.Members.Count; i++)
                    {
                        if (a.Members[i].Key != b.Members[i].Key) return false;
                        if (!DeepEqual(a.Members[i].Value, b.Members[i].Value)) return false;
                    }
                    return true;
            }
            return false;
        }

        /// <summary>配置里的每个键都必须是文档里为那个段收录过的。</summary>
        static string CheckCatalog(JsonNode root)
        {
            StringBuilder bad = new StringBuilder();
            ScanOne(bad, root.Get("engine"), Section.Engine, "engine");
            ScanOne(bad, root.Get("server"), Section.Server, "server");
            JsonNode models = root.Get("models");
            if (models != null && models.Kind == JsonKind.Array)
                for (int i = 0; i < models.Items.Count; i++)
                    ScanOne(bad, models.Items[i], Section.ModelEntry, "models[" + i + "]");
            return bad.Length == 0 ? null : bad.ToString();
        }

        static void ScanOne(StringBuilder bad, JsonNode obj, Section s, string where)
        {
            if (obj == null || obj.Kind != JsonKind.Object) return;
            for (int i = 0; i < obj.Members.Count; i++)
            {
                string k = obj.Members[i].Key;
                if (Catalog.Find(k, s) == null)
                    bad.AppendLine(where + "." + k + " 未收录； ");
            }
        }
    }
}
