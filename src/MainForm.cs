using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BasaltConfigTool
{
    internal sealed class NodeRef
    {
        public Section Section;
        public int ModelIndex = -1;
        public string Key;       // null = the section node itself
        public bool Unset;       // catalog key that is not in the file yet
    }

    public sealed class MainForm : Form
    {
        readonly string _dir;

        JsonNode _root;
        string _file;
        bool _dirty;
        bool _loading;

        ComboBox cmbFiles;
        Button btnRefresh;
        Button btnSave;
        Button btnReload;
        Button btnAdd;
        Button btnDelete;
        Button btnRef;
        Button btnApply;
        CheckBox chkShowUnset;
        CheckBox chkBool;
        Label lblCmd;
        Label lblLnk;
        Label lblChinese;
        Label lblKey;
        Label lblType;
        Label lblDefault;
        Label lblStatus;
        Label lblHint;
        TreeView tv;
        TextBox txtValue;
        TextBox txtDesc;
        ComboBox cmbEnum;
        GroupBox boxSel;

        public MainForm(string dir)
        {
            _dir = dir;
            BuildUi();
            RefreshFileList();
        }

        // ------------------------------------------------------------------ UI

        void BuildUi()
        {
            Text = "Basalt 配置编辑器  —  " + _dir;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(980, 620);
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.None;

            // ---------------------------------------------------------- top
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 100;

            Label lblFile = new Label();
            lblFile.Text = "配置文件：";
            lblFile.AutoSize = true;
            lblFile.Location = new Point(12, 12);
            top.Controls.Add(lblFile);

            cmbFiles = new ComboBox();
            cmbFiles.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFiles.Location = new Point(100, 8);
            cmbFiles.Width = 340;
            cmbFiles.SelectedIndexChanged += delegate { OnFileSelected(); };
            top.Controls.Add(cmbFiles);

            btnRefresh = new Button();
            btnRefresh.Text = "刷新";
            btnRefresh.Location = new Point(448, 6);
            btnRefresh.Size = new Size(70, 26);
            btnRefresh.Click += delegate { RefreshFileList(); };
            top.Controls.Add(btnRefresh);

            chkShowUnset = new CheckBox();
            chkShowUnset.Text = "显示未设置的配置项（灰色 = 文档里有、此文件没写）";
            chkShowUnset.AutoSize = true;
            chkShowUnset.Location = new Point(530, 10);
            chkShowUnset.Checked = true;
            chkShowUnset.CheckedChanged += delegate { RebuildTree(); };
            top.Controls.Add(chkShowUnset);

            Label l1 = new Label();
            l1.Text = "关联 .cmd：";
            l1.AutoSize = true;
            l1.Location = new Point(12, 44);
            top.Controls.Add(l1);

            lblCmd = new Label();
            lblCmd.Location = new Point(100, 44);
            lblCmd.AutoSize = false;
            lblCmd.Width = 1050;
            lblCmd.Height = 20;
            lblCmd.AutoEllipsis = true;
            lblCmd.ForeColor = Color.DarkBlue;
            top.Controls.Add(lblCmd);

            Label l2 = new Label();
            l2.Text = "快捷方式：";
            l2.AutoSize = true;
            l2.Location = new Point(12, 70);
            top.Controls.Add(l2);

            lblLnk = new Label();
            lblLnk.Location = new Point(100, 70);
            lblLnk.AutoSize = false;
            lblLnk.Width = 1050;
            lblLnk.Height = 20;
            lblLnk.AutoEllipsis = true;
            lblLnk.ForeColor = Color.DarkGreen;
            top.Controls.Add(lblLnk);

            Controls.Add(top);

            // ---------------------------------------------------------- 底部状态栏
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 46;

            btnSave = MkButton(bottom, "保存到文件", 12, 10, 110, delegate { Save(); });
            btnReload = MkButton(bottom, "放弃修改", 130, 10, 110, delegate { Reload(); });
            btnAdd = MkButton(bottom, "添加所选键", 248, 10, 110, delegate { AddSelected(); });
            btnDelete = MkButton(bottom, "删除所选键", 366, 10, 110, delegate { DeleteSelected(); });
            btnRef = MkButton(bottom, "全部配置项参考", 484, 10, 130, delegate { ShowReference(); });
            btnAdd.Enabled = false;
            btnDelete.Enabled = false;

            lblStatus = new Label();
            lblStatus.Location = new Point(626, 15);
            lblStatus.AutoSize = false;
            lblStatus.Width = 540;
            lblStatus.Height = 22;
            lblStatus.AutoEllipsis = true;
            bottom.Controls.Add(lblStatus);

            Controls.Add(bottom);

            // ---------------------------------------------------------- 分隔条
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            // 必须先给尺寸：SplitterDistance 是按
            // Width - Panel2MinSize 校验的，而控件刚建出来的默认宽度小得离谱
            split.Size = new Size(1180, 560);
            split.Panel1MinSize = 260;
            split.Panel2MinSize = 520;
            split.SplitterDistance = 400;

            tv = new TreeView();
            tv.Dock = DockStyle.Fill;
            tv.HideSelection = false;
            tv.ShowLines = true;
            tv.ItemHeight = 20;
            tv.AfterSelect += delegate { OnNodeSelected(); };
            split.Panel1.Controls.Add(tv);

            boxSel = new GroupBox();
            boxSel.Text = "选中项";
            boxSel.Dock = DockStyle.Top;
            boxSel.Height = 176;
            split.Panel2.Controls.Add(boxSel);

            lblChinese = new Label();
            lblChinese.Location = new Point(14, 24);
            lblChinese.AutoSize = true;
            lblChinese.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            boxSel.Controls.Add(lblChinese);

            AddLabel(boxSel, "字段名：", 14, 60);
            lblKey = new Label();
            lblKey.Location = new Point(78, 60);
            lblKey.AutoSize = true;
            lblKey.Font = new Font("Consolas", 10f);
            boxSel.Controls.Add(lblKey);

            AddLabel(boxSel, "类型：", 300, 60);
            lblType = new Label();
            lblType.Location = new Point(348, 60);
            lblType.AutoSize = true;
            boxSel.Controls.Add(lblType);

            AddLabel(boxSel, "默认：", 520, 60);
            lblDefault = new Label();
            lblDefault.Location = new Point(568, 60);
            lblDefault.AutoSize = true;
            boxSel.Controls.Add(lblDefault);

            AddLabel(boxSel, "值：", 14, 94);

            txtValue = new TextBox();
            txtValue.Location = new Point(78, 91);
            txtValue.Width = 540;
            txtValue.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyValue(); }
            };
            boxSel.Controls.Add(txtValue);

            chkBool = new CheckBox();
            chkBool.Text = "开启（true）";
            chkBool.Location = new Point(78, 92);
            chkBool.AutoSize = true;
            chkBool.Visible = false;
            boxSel.Controls.Add(chkBool);

            cmbEnum = new ComboBox();
            cmbEnum.Location = new Point(78, 91);
            cmbEnum.Width = 540;
            cmbEnum.DropDownStyle = ComboBoxStyle.DropDown;
            cmbEnum.Visible = false;
            boxSel.Controls.Add(cmbEnum);

            btnApply = new Button();
            btnApply.Text = "应用修改";
            btnApply.Location = new Point(636, 89);
            btnApply.Size = new Size(96, 28);
            btnApply.Click += delegate { ApplyValue(); };
            boxSel.Controls.Add(btnApply);

            lblHint = new Label();
            lblHint.Location = new Point(14, 130);
            lblHint.AutoSize = false;
            lblHint.Width = 740;
            lblHint.Height = 36;
            lblHint.ForeColor = Color.Gray;
            lblHint.Text = "回车或点「应用修改」只写进内存（标题会出现 *）；点底部「保存到文件」才真正落盘，保存前会自动生成 .bak 备份。";
            boxSel.Controls.Add(lblHint);

            Panel descPanel = new Panel();
            descPanel.Dock = DockStyle.Fill;
            split.Panel2.Controls.Add(descPanel);
            // 填充子控件必须排在第 0 位，否则顶部那条会被挤掉
            split.Panel2.Controls.SetChildIndex(descPanel, 0);

            Label lblDesc = new Label();
            lblDesc.Text = "详细说明";
            lblDesc.Dock = DockStyle.Top;
            lblDesc.Height = 24;
            lblDesc.TextAlign = ContentAlignment.MiddleLeft;
            lblDesc.Padding = new Padding(6, 0, 0, 0);
            descPanel.Controls.Add(lblDesc);

            txtDesc = new TextBox();
            txtDesc.Multiline = true;
            txtDesc.ReadOnly = true;
            txtDesc.ScrollBars = ScrollBars.Vertical;
            txtDesc.Dock = DockStyle.Fill;
            txtDesc.BackColor = Color.White;
            txtDesc.Font = new Font("Microsoft YaHei UI", 9.5f);
            descPanel.Controls.Add(txtDesc);
            descPanel.Controls.SetChildIndex(txtDesc, 0);

            Controls.Add(split);
            Controls.SetChildIndex(split, 0);

            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (!_dirty) return;
                DialogResult r = MessageBox.Show(this,
                    "还有未保存的修改，确定退出？", "未保存",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) e.Cancel = true;
            };
        }

        static void AddLabel(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
        }

        static Button MkButton(Control parent, string text, int x, int y, int w, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 28);
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------- 载入

        void RefreshFileList()
        {
            _loading = true;
            string keep = _file;
            cmbFiles.Items.Clear();

            string[] files;
            try { files = Directory.GetFiles(_dir, "*.json"); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "列目录失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                files = new string[0];
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
                cmbFiles.Items.Add(Path.GetFileName(files[i]));

            _loading = false;
            if (cmbFiles.Items.Count == 0)
            {
                SetStatus("这个目录里没有 *.json 配置文件。", Color.DarkRed);
                return;
            }
            int idx = 0;
            if (keep != null)
            {
                int found = cmbFiles.Items.IndexOf(keep);
                if (found >= 0) idx = found;
            }
            cmbFiles.SelectedIndex = idx;
        }

        void OnFileSelected()
        {
            if (_loading) return;
            if (cmbFiles.SelectedIndex < 0) return;
            string name = cmbFiles.SelectedItem.ToString();
            LoadFile(Path.Combine(_dir, name));
        }

        void LoadFile(string path)
        {
            if (_dirty)
            {
                DialogResult r = MessageBox.Show(this,
                    "当前配置有未保存的修改，切换文件会丢失。继续？", "未保存",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    _loading = true;
                    cmbFiles.SelectedItem = _file;
                    _loading = false;
                    return;
                }
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                _root = Json.Parse(text);
                if (_root.Kind != JsonKind.Object)
                    throw new JsonParseException("配置文件的根必须是一个 JSON 对象", 0);
            }
            catch (JsonParseException ex)
            {
                MessageBox.Show(this,
                    "这个文件不是合法 JSON，无法打开：\r\n\r\n" + ex.Message +
                    "\r\n（位置：" + ex.Offset + "）\r\n\r\n文件：" + path,
                    "解析失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _file = Path.GetFileName(path);
            _dirty = false;
            UpdateTitle();
            UpdateAssoc();
            RebuildTree();
            SetStatus("已载入 " + _file, Color.Black);
        }

        void UpdateTitle()
        {
            Text = "Basalt 配置编辑器  —  " + (_file == null ? "(未选择)" : _file)
                 + (_dirty ? "  *" : "");
        }

        void UpdateAssoc()
        {
            List<CmdRef> cmds = Assoc.CmdsFor(_dir, _file);
            if (cmds.Count == 0)
            {
                lblCmd.Text = "（本目录里没有 .cmd 引用 " + _file + "）";
                lblCmd.ForeColor = Color.DarkRed;
                lblLnk.Text = "—";
                lblLnk.ForeColor = Color.Gray;
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cmds.Count; i++)
            {
                if (i > 0) sb.Append("    |    ");
                sb.Append(Path.GetFileName(cmds[i].Path));
                if (cmds[i].ImplicitDefault) sb.Append("（不传 -ConfigFile，用启动器默认值）");
            }
            lblCmd.Text = sb.ToString();
            lblCmd.ForeColor = Color.DarkBlue;

            sb.Length = 0;
            for (int i = 0; i < cmds.Count; i++)
            {
                List<string> lnks = Assoc.ShortcutsFor(cmds[i].Path);
                if (i > 0) sb.Append("    |    ");
                sb.Append(Path.GetFileName(cmds[i].Path)).Append(" -> ");
                if (lnks.Count == 0)
                {
                    sb.Append("桌面上没有对应快捷方式");
                }
                else
                {
                    for (int k = 0; k < lnks.Count; k++)
                    {
                        if (k > 0) sb.Append("，");
                        sb.Append(Path.GetFileNameWithoutExtension(lnks[k]));
                    }
                }
            }
            lblLnk.Text = sb.ToString();
            lblLnk.ForeColor = Color.DarkGreen;
        }

        // ---------------------------------------------------------------- 树

        void RebuildTree()
        {
            if (_root == null) { tv.Nodes.Clear(); return; }

            NodeRef selRef = null;
            TreeNode selNode = tv.SelectedNode;
            if (selNode != null) selRef = selNode.Tag as NodeRef;

            tv.BeginUpdate();
            tv.Nodes.Clear();

            JsonNode models = _root.Get("models");
            if (models != null && models.Kind == JsonKind.Array)
            {
                for (int i = 0; i < models.Items.Count; i++)
                {
                    string alias = "?";
                    JsonNode a = models.Items[i].Get("alias");
                    if (a != null) alias = a.ToDisplay();
                    else
                    {
                        JsonNode m = models.Items[i].Get("model");
                        if (m != null) alias = Path.GetFileName(m.ToDisplay());
                    }
                    TreeNode n = new TreeNode("模型条目 #" + (i + 1) + "  (" + alias + ")");
                    n.Tag = new NodeRef { Section = Section.ModelEntry, ModelIndex = i };
                    FillSectionChildren(n, models.Items[i], Section.ModelEntry, i);
                    tv.Nodes.Add(n);
                    if (i == 0) n.Expand();
                }
            }
            else
            {
                TreeNode warn = new TreeNode("没有 models 数组（v2 配置必须有）");
                warn.ForeColor = Color.DarkRed;
                tv.Nodes.Add(warn);
            }

            JsonNode engine = _root.Get("engine");
            TreeNode en = new TreeNode("引擎设置  (engine)");
            en.Tag = new NodeRef { Section = Section.Engine };
            if (engine != null && engine.Kind == JsonKind.Object)
                FillSectionChildren(en, engine, Section.Engine, -1);
            tv.Nodes.Add(en);
            en.Expand();

            JsonNode server = _root.Get("server");
            TreeNode sn = new TreeNode("服务器设置  (server)");
            sn.Tag = new NodeRef { Section = Section.Server };
            if (server != null && server.Kind == JsonKind.Object)
                FillSectionChildren(sn, server, Section.Server, -1);
            tv.Nodes.Add(sn);
            sn.Expand();

            tv.EndUpdate();
            if (selRef != null)
            {
                SelectByRef(selRef);
            }
            else
            {
                // 什么都没选：默认落在第一个真实存在的键上，
                // 好让右侧说明栏有东西可显示，而不是一个空框
                foreach (TreeNode top in tv.Nodes)
                {
                    if (top.Nodes.Count > 0) { tv.SelectedNode = top.Nodes[0]; break; }
                    if (top.Tag is NodeRef) { tv.SelectedNode = top; break; }
                }
            }
            UpdateTitle();
        }

        void FillSectionChildren(TreeNode parent, JsonNode obj, Section section, int modelIndex)
        {
            for (int i = 0; i < obj.Members.Count; i++)
            {
                JsonMember m = obj.Members[i];
                KeyMeta meta = Catalog.Find(m.Key, section);
                string label = meta != null
                    ? meta.Chinese + "   [" + m.Key + "] = " + Short(m.Value)
                    : "（未收录的键）   [" + m.Key + "] = " + Short(m.Value);

                TreeNode n = new TreeNode(label);
                n.Tag = new NodeRef { Section = section, ModelIndex = modelIndex, Key = m.Key };
                if (meta == null) n.ForeColor = Color.DarkOrange;
                parent.Nodes.Add(n);
            }

            if (!chkShowUnset.Checked) return;

            List<KeyMeta> all = Catalog.ForSection(section);
            for (int i = 0; i < all.Count; i++)
            {
                if (obj.Has(all[i].Name)) continue;
                if (all[i].Name == "api-key") continue;   // never written to a file
                TreeNode n = new TreeNode("（未设置）" + all[i].Chinese + "   [" + all[i].Name + "]");
                n.Tag = new NodeRef
                {
                    Section = section,
                    ModelIndex = modelIndex,
                    Key = all[i].Name,
                    Unset = true
                };
                n.ForeColor = Color.Gray;
                parent.Nodes.Add(n);
            }
        }

        static string Short(JsonNode v)
        {
            string s = v.ToDisplay();
            s = s.Replace("\r", " ").Replace("\n", " ");
            if (s.Length > 60) s = s.Substring(0, 57) + "...";
            return s;
        }

        void SelectByRef(NodeRef want)
        {
            foreach (TreeNode top in tv.Nodes)
            {
                NodeRef r = top.Tag as NodeRef;
                if (r != null && r.Section == want.Section && r.ModelIndex == want.ModelIndex
                    && want.Key == null) { tv.SelectedNode = top; return; }
                foreach (TreeNode c in top.Nodes)
                {
                    NodeRef cr = c.Tag as NodeRef;
                    if (cr != null && cr.Section == want.Section && cr.ModelIndex == want.ModelIndex
                        && cr.Key == want.Key && cr.Unset == want.Unset)
                    { tv.SelectedNode = c; return; }
                }
            }
        }

        JsonNode SectionNode(NodeRef r)
        {
            if (_root == null) return null;
            if (r.Section == Section.Engine) return _root.Get("engine");
            if (r.Section == Section.Server) return _root.Get("server");
            JsonNode models = _root.Get("models");
            if (models == null || models.Kind != JsonKind.Array) return null;
            if (r.ModelIndex < 0 || r.ModelIndex >= models.Items.Count) return null;
            return models.Items[r.ModelIndex];
        }

        // ------------------------------------------------------------ 选中

        void OnNodeSelected()
        {
            TreeNode n = tv.SelectedNode;
            if (n == null) { ClearSelection(); return; }
            NodeRef r = n.Tag as NodeRef;
            if (r == null) { ClearSelection(); return; }

            btnAdd.Enabled = false;
            btnDelete.Enabled = false;
            btnApply.Enabled = false;
            txtValue.Text = "";
            txtValue.Enabled = true;
            txtValue.Visible = true;
            chkBool.Visible = false;
            cmbEnum.Visible = false;

            if (r.Key == null)
            {
                lblChinese.Text = r.Section == Section.ModelEntry
                    ? "模型条目 #" + (r.ModelIndex + 1)
                    : (r.Section == Section.Engine ? "引擎设置" : "服务器设置");
                lblKey.Text = r.Section == Section.ModelEntry ? "models[" + r.ModelIndex + "]"
                            : (r.Section == Section.Engine ? "engine" : "server");
                lblType.Text = "对象";
                lblDefault.Text = "-";
                txtDesc.Text = SectionHelp(r.Section);
                return;
            }

            KeyMeta meta = Catalog.Find(r.Key, r.Section);
            lblKey.Text = r.Key;

            if (r.Unset)
            {
                lblChinese.Text = meta != null ? meta.Chinese : r.Key;
                lblType.Text = meta != null ? TypeName(meta.Type) : "?";
                lblDefault.Text = meta != null ? meta.Default : "?";
                txtDesc.Text = (meta != null ? meta.Description : "（文档里没有这个键）")
                             + "\r\n\r\n=========\r\n这个键当前没有写进配置文件。点底部「添加所选键」把它加进来，再改值。";
                btnAdd.Enabled = true;
                txtValue.Enabled = false;
                return;
            }

            JsonNode sec = SectionNode(r);
            JsonNode val = sec == null ? null : sec.Get(r.Key);
            if (val == null) { ClearSelection(); return; }

            if (meta != null)
            {
                lblChinese.Text = meta.Chinese;
                lblType.Text = TypeName(meta.Type);
                lblDefault.Text = meta.Default;
                txtDesc.Text = meta.Description;
            }
            else
            {
                lblChinese.Text = "（文档里没有收录这个键）";
                lblType.Text = GuessType(val);
                lblDefault.Text = "?";
                txtDesc.Text =
                    "这个键名不在 basalt 0.3.3 的文档里。\r\n\r\n" +
                    "注意：engine 段里的键会拿引擎二进制自带的 flag 表逐个校验，\r\n" +
                    "写一个不存在的键会让启动直接失败。请确认拼写，\r\n" +
                    "或者先查 docs\\configuration\\index.md。\r\n\r\n" +
                    "（models 条目和 server 段里的未知键同样会让启动失败）";
            }

            btnApply.Enabled = true;
            btnDelete.Enabled = r.Key != "model";
            PopulateEditor(meta, val);
        }

        void PopulateEditor(KeyMeta meta, JsonNode val)
        {
            txtValue.Visible = false;
            chkBool.Visible = false;
            cmbEnum.Visible = false;

            if (val.Kind == JsonKind.Bool)
            {
                chkBool.Visible = true;
                chkBool.Checked = val.Flag;
                return;
            }
            if (val.Kind == JsonKind.Array)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < val.Items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(val.Items[i].ToDisplay());
                }
                txtValue.Visible = true;
                txtValue.Text = sb.ToString();
                return;
            }
            if (val.Kind == JsonKind.Object)
            {
                txtValue.Visible = true;
                txtValue.Text = Json.Serialize(val).Trim();
                return;
            }

            txtValue.Visible = true;
            txtValue.Text = val.ToDisplay();

            if (meta != null && meta.Type == "enum" && !string.IsNullOrEmpty(meta.Allowed))
            {
                cmbEnum.Items.Clear();
                string[] parts = meta.Allowed.Split(',');
                for (int i = 0; i < parts.Length; i++) cmbEnum.Items.Add(parts[i].Trim());
                cmbEnum.Text = val.ToDisplay();
                cmbEnum.Visible = true;
                txtValue.Visible = false;
            }
        }

        void ClearSelection()
        {
            lblChinese.Text = "";
            lblKey.Text = "";
            lblType.Text = "";
            lblDefault.Text = "";
            txtDesc.Text = "在左边选一个配置项。";
            btnApply.Enabled = false;
            btnAdd.Enabled = false;
            btnDelete.Enabled = false;
        }

        static string TypeName(string t)
        {
            switch (t)
            {
                case "switch": return "开关 true/false";
                case "integer": return "整数";
                case "number": return "数字";
                case "enum": return "枚举";
                case "string-list": return "字符串列表";
                default: return "字符串";
            }
        }

        static string GuessType(JsonNode v)
        {
            switch (v.Kind)
            {
                case JsonKind.Bool: return "开关 true/false";
                case JsonKind.Number: return "数字";
                case JsonKind.Array: return "数组";
                case JsonKind.Object: return "对象";
                default: return "字符串";
            }
        }

        static string SectionHelp(Section s)
        {
            switch (s)
            {
                case Section.ModelEntry:
                    return "这是 models 数组里的一项。\r\n\r\n" +
                           "每个条目自带模型文件、别名、显卡、上下文、并发槽位、视觉与视频、思考预算、采样默认值。\r\n" +
                           "条目里也可以写引擎 flag（会覆盖顶层 engine 段的同名项）。\r\n\r\n" +
                           "会让启动失败的情况：两个条目用同一个别名、两个条目共用一个缓存目录、\r\n" +
                           "缺少 model、写了这个模型没有的 flag。";
                case Section.Engine:
                    return "engine 段对所有接受它的模型生效；模型条目里的同名项优先。\r\n\r\n" +
                           "引擎会拿二进制自带的 flag 表逐个校验这里的键：\r\n" +
                           "  模型没有的 flag  -> 略过，并在启动日志里打一行\r\n" +
                           "  值不合法          -> 直接让启动失败\r\n" +
                           "  完全不认识的名字  -> 直接让启动失败";
                default:
                    return "server 段是全服务器共享的：监听地址与端口、Host 检查、跨域、图片源、\r\n" +
                           "请求体上限、日志、网页与延迟加载。\r\n\r\n" +
                           "api-key 不能写在这里 —— basalt 只接受命令行传入。";
            }
        }

        // ------------------------------------------------------------- 编辑

        void ApplyValue()
        {
            TreeNode n = tv.SelectedNode;
            if (n == null) return;
            NodeRef r = n.Tag as NodeRef;
            if (r == null || r.Key == null || r.Unset) return;
            JsonNode sec = SectionNode(r);
            if (sec == null) return;
            JsonNode old = sec.Get(r.Key);
            if (old == null) return;

            JsonNode made;
            try { made = MakeNode(old); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "值不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (made == null) return;

            if (!SameAs(old, made))
            {
                sec.Set(r.Key, made);
                _dirty = true;
                UpdateTitle();
                SetStatus("已修改 " + r.Key + "（尚未写盘）", Color.DarkOrange);
            }
            RebuildTree();
        }

        JsonNode MakeNode(JsonNode old)
        {
            if (old.Kind == JsonKind.Bool)
                return JsonNode.NewBool(chkBool.Checked);

            string text = cmbEnum.Visible ? cmbEnum.Text : txtValue.Text;
            if (text == null) text = "";

            if (old.Kind == JsonKind.Array)
            {
                JsonNode arr = JsonNode.NewArray();
                string[] parts = text.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim().Trim('"');
                    if (p.Length == 0) continue;
                    arr.Items.Add(JsonNode.NewString(p));
                }
                return arr;
            }
            if (old.Kind == JsonKind.Object)
            {
                try { return Json.Parse(text); }
                catch (JsonParseException ex)
                {
                    throw new Exception("这个键的值是一个对象，你给的不是合法 JSON：" + ex.Message);
                }
            }
            if (old.Kind == JsonKind.Number)
            {
                string t = text.Trim();
                double d;
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new Exception("这个键需要数字，\"" + t + "\" 不是。\r\n（小数用 . 作小数点，不要写单位）");
                return JsonNode.NewNumber(t);
            }
            if (old.Kind == JsonKind.Null)
            {
                string t = text.Trim();
                if (t == "null") return JsonNode.NewNull();
                if (t == "true") return JsonNode.NewBool(true);
                if (t == "false") return JsonNode.NewBool(false);
                double d;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    return JsonNode.NewNumber(t);
                return JsonNode.NewString(text);
            }
            return JsonNode.NewString(text);
        }

        static bool SameAs(JsonNode a, JsonNode b)
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
                        if (!SameAs(a.Items[i], b.Items[i])) return false;
                    return true;
                default: return false;
            }
        }

        void AddSelected()
        {
            TreeNode n = tv.SelectedNode;
            if (n == null) return;
            NodeRef r = n.Tag as NodeRef;
            if (r == null || r.Key == null || !r.Unset) return;
            JsonNode sec = SectionNode(r);
            if (sec == null) return;

            if (r.Key == "api-key")
            {
                MessageBox.Show(this,
                    "api-key 不能写进配置文件 —— basalt 设计上只接受命令行传入。\r\n" +
                    "启动器 scripts\\start_basalt.ps1 会从 .api_key 文件读取并加 --api-key。",
                    "不允许", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            KeyMeta meta = Catalog.Find(r.Key, r.Section);
            sec.Set(r.Key, Catalog.DefaultNodeFor(meta));
            _dirty = true;
            UpdateTitle();
            SetStatus("已添加 " + r.Key + "（尚未写盘）", Color.DarkOrange);
            RebuildTree();
        }

        void DeleteSelected()
        {
            TreeNode n = tv.SelectedNode;
            if (n == null) return;
            NodeRef r = n.Tag as NodeRef;
            if (r == null || r.Key == null || r.Unset) return;
            if (r.Key == "model")
            {
                MessageBox.Show(this,
                    "不能删除 model —— 没有它这个模型条目就无法加载。\r\n" +
                    "如果确实不要这个模型，请删掉整个条目块。",
                    "不允许", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    "删除配置项 " + r.Key + " ？\r\n（删除后该键回到默认值）",
                    "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            JsonNode sec = SectionNode(r);
            if (sec == null) return;
            sec.Remove(r.Key);
            _dirty = true;
            UpdateTitle();
            SetStatus("已删除 " + r.Key + "（尚未写盘）", Color.DarkOrange);
            RebuildTree();
        }

        // -------------------------------------------------------------- 保存

        void Save()
        {
            if (_root == null || _file == null) return;

            string problems = ValidateConfig();
            if (problems != null)
            {
                DialogResult r = MessageBox.Show(this,
                    "保存前发现这些问题：\r\n\r\n" + problems +
                    "\r\n\r\n仍然保存吗？（坏掉的配置会让引擎起不来）",
                    "校验警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            string path = Path.Combine(_dir, _file);
            string text = Json.Serialize(_root);

            try { Json.Parse(text); }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "内部错误：生成的内容无法被重新解析，未写入。\r\n" + ex.Message,
                    "中止保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backup = path + ".bak-" + stamp;
                if (File.Exists(path)) File.Copy(path, backup, true);

                File.WriteAllText(path, text, new UTF8Encoding(false));
                _dirty = false;
                UpdateTitle();
                SetStatus("已保存 " + _file + "（备份：" + Path.GetFileName(backup) + "）",
                    Color.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "写入失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void Reload()
        {
            if (_file == null) return;
            if (_dirty && MessageBox.Show(this, "放弃未保存的修改并重新读取？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _dirty = false;
            LoadFile(Path.Combine(_dir, _file));
        }

        string ValidateConfig()
        {
            StringBuilder sb = new StringBuilder();
            JsonNode models = _root.Get("models");
            if (models == null || models.Kind != JsonKind.Array || models.Items.Count == 0)
                sb.AppendLine("* 缺少 models 数组：v2 配置必须有，否则引擎拒绝启动。");

            CheckSection(sb, _root.Get("engine"), Section.Engine, "engine");
            CheckSection(sb, _root.Get("server"), Section.Server, "server");
            if (models != null && models.Kind == JsonKind.Array)
            {
                for (int i = 0; i < models.Items.Count; i++)
                    CheckSection(sb, models.Items[i], Section.ModelEntry, "models[" + i + "]");
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        void CheckSection(StringBuilder sb, JsonNode obj, Section section, string where)
        {
            if (obj == null) return;
            if (obj.Kind != JsonKind.Object)
            {
                sb.AppendLine("* " + where + " 不是一个 JSON 对象。");
                return;
            }
            for (int i = 0; i < obj.Members.Count; i++)
            {
                JsonMember m = obj.Members[i];
                KeyMeta meta = Catalog.Find(m.Key, section);
                if (meta == null)
                {
                    sb.AppendLine("* " + where + "." + m.Key +
                                  " ：这个键名不在文档里，引擎可能会拒绝启动。");
                    continue;
                }
                if (m.Key == "api-key")
                {
                    sb.AppendLine("* " + where + ".api-key ：这个键不能写在配置文件里，引擎会拒绝。");
                    continue;
                }
                switch (meta.Type)
                {
                    case "switch":
                        if (m.Value.Kind != JsonKind.Bool)
                            sb.AppendLine("* " + where + "." + m.Key + " ：应该是 true/false。");
                        break;
                    case "integer":
                        if (m.Value.Kind != JsonKind.Number || m.Value.Text.IndexOf('.') >= 0)
                            sb.AppendLine("* " + where + "." + m.Key + " ：应该是整数。");
                        break;
                    case "number":
                        if (m.Value.Kind != JsonKind.Number)
                            sb.AppendLine("* " + where + "." + m.Key + " ：应该是数字。");
                        break;
                    case "enum":
                        if (m.Value.Kind != JsonKind.String)
                        {
                            sb.AppendLine("* " + where + "." + m.Key + " ：应该是字符串。");
                        }
                        else if (!string.IsNullOrEmpty(meta.Allowed))
                        {
                            bool ok = false;
                            string[] parts = meta.Allowed.Split(',');
                            for (int k = 0; k < parts.Length; k++)
                                if (string.Equals(parts[k].Trim(), m.Value.Text,
                                        StringComparison.Ordinal)) { ok = true; break; }

                            int dummy;
                            if (!ok && m.Key == "vision" && int.TryParse(m.Value.Text, out dummy)) ok = true;

                            if (!ok)
                                sb.AppendLine("* " + where + "." + m.Key + " ：\"" + m.Value.Text +
                                              "\" 不在可选值里（" + meta.Allowed + "）。");
                        }
                        break;
                }
            }
            if (section == Section.ModelEntry && !obj.Has("model"))
                sb.AppendLine("* " + where + " 没有 model —— 引擎无法加载这个条目。");
        }

        void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        void ShowReference()
        {
            ReferenceForm f = new ReferenceForm();
            f.ShowDialog(this);
        }
    }

    // ================================================================== 关联视图

    public sealed class ReferenceForm : Form
    {
        public ReferenceForm()
        {
            Text = "全部配置项参考（来自 basalt 0.3.3 文档）";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1080, 640);
            Font = new Font("Microsoft YaHei UI", 9f);
            MinimumSize = new Size(800, 500);

            SplitContainer sp = new SplitContainer();
            sp.Dock = DockStyle.Fill;
            sp.Orientation = Orientation.Vertical;
            sp.Size = new Size(1080, 620);
            sp.Panel1MinSize = 300;
            sp.Panel2MinSize = 300;
            sp.SplitterDistance = 620;

            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = true;
            lv.HideSelection = false;
            lv.Columns.Add("段", 90);
            lv.Columns.Add("字段名", 210);
            lv.Columns.Add("中文名", 160);
            lv.Columns.Add("类型", 90);
            lv.Columns.Add("默认", 130);

            TextBox desc = new TextBox();
            desc.Multiline = true;
            desc.ReadOnly = true;
            desc.ScrollBars = ScrollBars.Vertical;
            desc.Dock = DockStyle.Fill;
            desc.BackColor = Color.White;
            desc.Font = new Font("Microsoft YaHei UI", 9.5f);

            lv.SelectedIndexChanged += delegate
            {
                if (lv.SelectedItems.Count == 0) { desc.Text = ""; return; }
                KeyMeta m = lv.SelectedItems[0].Tag as KeyMeta;
                if (m == null) { desc.Text = ""; return; }
                desc.Text = m.Chinese + "   [" + m.Name + "]\r\n"
                          + "类型：" + m.Type + "      默认：" + m.Default
                          + (string.IsNullOrEmpty(m.Allowed) ? "" : "      可选：" + m.Allowed)
                          + "\r\n------------------------------------------------\r\n"
                          + m.Description;
            };

            AddGroup(lv, "模型条目（models 里的每一项）", Section.ModelEntry);
            AddGroup(lv, "引擎（engine）", Section.Engine);
            AddGroup(lv, "服务器（server）", Section.Server);

            sp.Panel1.Controls.Add(lv);
            sp.Panel2.Controls.Add(desc);
            Controls.Add(sp);
        }

        static void AddGroup(ListView lv, string title, Section s)
        {
            ListViewGroup g = new ListViewGroup(title);
            lv.Groups.Add(g);
            List<KeyMeta> list = Catalog.ForSection(s);
            for (int i = 0; i < list.Count; i++)
            {
                KeyMeta m = list[i];
                ListViewItem it = new ListViewItem(new string[]
                {
                    s == Section.ModelEntry ? "模型条目" : (s == Section.Engine ? "engine" : "server"),
                    m.Name,
                    m.Chinese,
                    m.Type,
                    m.Default
                });
                it.Group = g;
                it.Tag = m;
                lv.Items.Add(it);
            }
        }
    }
}
