using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BasaltConfigTool
{
    /// <summary>一个会启动某份配置的启动脚本，以及它是否明确点名了这份配置。</summary>
    public sealed class CmdRef
    {
        public string Path;
        public bool ImplicitDefault;   // 这个 .cmd 没传 -ConfigFile，走的是启动器的默认值
    }

    /// <summary>
    /// 从磁盘上的实际文件反推：哪个启动脚本（.cmd）把某份配置交给服务端，
    /// 以及哪个快捷方式（.lnk）启动那个脚本。
    /// </summary>
    public static class Assoc
    {
        /// <summary>不传 -ConfigFile 时，scripts\start_basalt.ps1 会退回到的文件名。</summary>
        public const string LauncherDefaultConfig = "basalt_config.json";

        static readonly Regex ConfigFileFlag = new Regex(
            @"-ConfigFile\s+(""?)(?<v>[^""\s]+)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex ConfigDashFlag = new Regex(
            @"--config\s+(""?)(?<v>[^""\s]+)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex LauncherScript = new Regex(
            @"start_basalt\.ps1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary><paramref name="dir"/> 下所有会启动这份配置文件名的 *.cmd。</summary>
        public static List<CmdRef> CmdsFor(string dir, string configFileName)
        {
            List<CmdRef> found = new List<CmdRef>();
            string[] files;
            try { files = Directory.GetFiles(dir, "*.cmd"); }
            catch { return found; }

            for (int i = 0; i < files.Length; i++)
            {
                string text;
                try { text = File.ReadAllText(files[i]); }
                catch { continue; }

                bool named = NamesConfig(text, configFileName);
                if (named)
                {
                    found.Add(new CmdRef { Path = files[i], ImplicitDefault = false });
                    continue;
                }

                // 调用 start_basalt.ps1 却没传 -ConfigFile 的启动脚本，用的是默认那份配置
                if (LauncherScript.IsMatch(text)
                    && string.IsNullOrEmpty(FirstConfigArgument(text))
                    && string.Equals(configFileName, LauncherDefaultConfig, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(new CmdRef { Path = files[i], ImplicitDefault = true });
                }
            }
            found.Sort(delegate (CmdRef a, CmdRef b)
            {
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return found;
        }

        static string FirstConfigArgument(string text)
        {
            Match m = ConfigFileFlag.Match(text);
            if (m.Success) return m.Groups["v"].Value;
            m = ConfigDashFlag.Match(text);
            if (m.Success) return m.Groups["v"].Value;
            return null;
        }

        static bool NamesConfig(string text, string configFileName)
        {
            foreach (Match m in ConfigFileFlag.Matches(text))
                if (SameFile(m.Groups["v"].Value, configFileName)) return true;

            foreach (Match m in ConfigDashFlag.Matches(text))
                if (SameFile(m.Groups["v"].Value, configFileName)) return true;

            return false;
        }

        static bool SameFile(string referenced, string configFileName)
        {
            if (string.IsNullOrEmpty(referenced)) return false;
            string name = referenced.Replace('/', '\\');
            int cut = name.LastIndexOf('\\');
            if (cut >= 0) name = name.Substring(cut + 1);
            return string.Equals(name, configFileName, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- 快捷方式

        static object _wsh;
        static Type _wshType;

        static object Shell()
        {
            if (_wsh != null) return _wsh;
            try
            {
                _wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (_wshType == null) return null;
                _wsh = Activator.CreateInstance(_wshType);
            }
            catch { _wsh = null; }
            return _wsh;
        }

        public static string LnkTarget(string lnkPath)
        {
            object sh = Shell();
            if (sh == null) return null;
            try
            {
                object link = _wshType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, sh, new object[] { lnkPath });
                object target = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                    null, link, null);
                return target as string;
            }
            catch { return null; }
        }

        /// <summary>桌面（含子目录）下所有指向这个 .cmd 的 *.lnk。</summary>
        public static List<string> ShortcutsFor(string cmdFullPath)
        {
            List<string> found = new List<string>();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            List<string> links = new List<string>();
            try { links.AddRange(Directory.GetFiles(desktop, "*.lnk", SearchOption.AllDirectories)); }
            catch { }

            for (int i = 0; i < links.Count; i++)
            {
                string t = LnkTarget(links[i]);
                if (t == null) continue;
                if (string.Equals(t.Trim('"'), cmdFullPath, StringComparison.OrdinalIgnoreCase))
                    found.Add(links[i]);
                else if (string.Equals(Path.GetFileName(t.Trim('"')), Path.GetFileName(cmdFullPath),
                             StringComparison.OrdinalIgnoreCase))
                {
                    // 文件名一样但放在别的目录：也值得报出来，而不是直接忽略
                    found.Add(links[i] + "  (同名目标)");
                }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }
    }
}
