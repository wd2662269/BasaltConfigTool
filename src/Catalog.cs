using System;
using System.Collections.Generic;

namespace BasaltConfigTool
{
    public enum Section { ModelEntry, Engine, Server }

    public sealed class KeyMeta
    {
        public string Name;          // 字段名
        public string Chinese;       // 中文名
        public string Type;          // switch / integer / number / string / enum / string-list
        public string Default;       // 默认值（说明用）
        public string Allowed;       // 枚举可选值，逗号分隔
        public string Description;   // 详细中文说明
        public Section[] Where;

        public bool In(Section s)
        {
            for (int i = 0; i < Where.Length; i++) if (Where[i] == s) return true;
            return false;
        }
    }

    /// <summary>
    /// 每个有文档的配置键，带中文名、类型、默认值和一段详细的中文说明。
    /// 内容抄自 0.3.3 的文档：
    /// docs/configuration/index.md, models.md, adaptive.md, sampling.md,
    /// docs/running.md (memory floors), docs/precision/*, docs/platforms.md.
    /// </summary>
    public static class Catalog
    {
        public static readonly List<KeyMeta> All = new List<KeyMeta>();

        static readonly Section[] ME = { Section.ModelEntry };
        static readonly Section[] MG = { Section.ModelEntry, Section.Engine };
        static readonly Section[] SV = { Section.Server };

        static void K(string name, string chinese, string type, string dflt, string allowed, Section[] where, string desc)
        {
            All.Add(new KeyMeta
            {
                Name = name,
                Chinese = chinese,
                Type = type,
                Default = dflt,
                Allowed = allowed,
                Where = where,
                Description = desc
            });
        }

        static Catalog()
        {
            // ---------------------------------------------------------- 模型条目：身份
            K("model", "模型文件", "string", "必填", null, ME,
              "（必填）.basalt 模型文件路径。相对路径以配置文件所在目录为准，也可以用绝对路径（正斜杠 / 或双反斜杠 \\\\）。\r\n" +
              "一个 .basalt 文件自带：权重、tokenizer、对话模板、MTP 草稿器、视觉投影器、默认采样参数、专家布局表。\r\n" +
              "启动时引擎会逐个检查每个条目，文件缺失/是目录/读不了都会打印一行明确原因然后以退出码 2 结束。");

            K("alias", "模型别名", "string", "文件里的名字", null, ME,
              "API 上报的模型名，客户端用这个名字请求。\r\n" +
              "所有 API（chat / messages / responses / count_tokens）都用别名或文件名匹配；写错的名字返回 404。\r\n" +
              "两个条目用同一个别名会让启动直接失败。");

            // ---------------------------------------------------------- 模型条目：显卡与槽位
            K("gpu", "使用的显卡", "string", "GPU 0", null, ME,
              "用哪些卡，按 nvidia-smi 的编号。\"0\"、\"0,1\"，或 \"auto\"。\r\n" +
              "列出的第一张是主卡；后面每张都成为专家层（最多三张）。\r\n" +
              "\"auto\" = 所有可见的 Blackwell（sm_120）卡，其中带宽最高的当主卡。\r\n" +
              "设了 CUDA_VISIBLE_DEVICES 时，编号在它的列表内计数。");

            K("parallel", "并发槽位", "integer", "1", null, ME,
              "同时解码的请求数，1 到 8。对应引擎的 --batch/--slots。\r\n" +
              "超出的请求按到达顺序排队（不是拒绝）。每个槽有独立的会话和独立的 KV。\r\n" +
              "max-context 是共享的：每个槽拿 max-context / parallel，除非开了 slot-full-context。\r\n" +
              "实测提醒：本机 4 并发的聚合吞吐只有单流的约 1.09 倍，而每个请求慢 3.3 倍。");

            K("slot-full-context", "每槽全上下文", "switch", "false", null, ME,
              "每个槽都拿到完整的 max-context，而不是 max-context / parallel。代价是 KV 缓存变成 N 倍。\r\n" +
              "关掉时，请求超过自己槽的长度会被 400 拒绝，错误里写明长度。\r\n" +
              "注意：fit-max-tokens 只裁输出 token，不救 prompt 太长的情况。");

            K("vision", "图像输入模式", "enum", "off", "off,dynamic,cpu", ME,
              "图像（和视频）输入：\r\n" +
              "  off     — 不启用\r\n" +
              "  dynamic — 在引擎内部跑编码器，借专家缓存最后几个槽，不常驻显存（0.85 GiB 页锁定内存）\r\n" +
              "  cpu     — sidecar 跑在 CPU 上，不占显存，但约 6～70 秒一张\r\n" +
              "  <GPU编号> — 在该卡上跑 sidecar，约占它 2.2 GiB 的专家缓存\r\n" +
              "四种模式的图像嵌入是逐位相同的。视频必须先把这里打开。");

            K("video-max-tokens", "视频 token 上限", "integer", "8192", null, ME,
              "一段视频在所有帧上最多占多少个 image token，64 到 65536。\r\n" +
              "帧率默认每秒 2 帧、4 到 768 帧等距采样；请求里的 video part 可以自己给 fps（最多 60）和 max_frames。\r\n" +
              "需要 vision 打开，并且系统里有 FFmpeg 6/7/8 的共享库（本机放在 bin\\ 里）。");

            K("fit-max-tokens", "裁剪输出上限", "switch", "false", null, ME,
              "把 max_tokens 裁到剩余上下文，而不是返回 400。\r\n" +
              "仅影响输出 token，不会让过长的 prompt 变得可接受。");

            K("grammar", "语法约束", "switch", "true", null, ME,
              "在引擎内部把工具调用和 JSON 格式（json_object / json_schema）约束到它们的 schema。\r\n" +
              "关掉的话，除这两种之外的 JSON 格式会被 400 拒绝。每个验证窗口检查一次语法。");

            K("idle-unload", "空闲卸载秒数", "number", "关闭", null, ME,
              "空闲这么多秒之后卸载模型（结束引擎进程），把显存让给别的程序。下一次请求会重新加载。");

            K("idle-sleep", "空闲休眠秒数", "number", "关闭", null, ME,
              "空闲这么多秒之后原地休眠：释放显存、保留内存，下一次请求几秒内唤醒。\r\n" +
              "**仅 Linux 支持**，Windows 上不生效。");

            K("min-free-vram-mib", "最小空闲显存", "integer", "关闭", null, ME,
              "只在有这么多 MiB 显存空着时，才加载尚未加载的模型；否则返回 503。");

            // ---------------------------------------------------------- 模型条目：思考与采样
            K("reasoning-budget-tokens", "思考预算", "integer", "0", null, ME,
              "请求自己没有指定思考预算时用这个值。请求自己给的 reasoning_budget_tokens / reasoning_effort / Anthropic 的 thinking.budget_tokens 优先。\r\n" +
              "0 = 不给条目级预算，完全交给 answer-room 的自动逻辑。");

            K("thinking-loop-tokens", "思考死循环保护", "integer", "256", null, ME,
              "思考阶段里同一个 token 连续重复这么多次，就强制结束思考、让模型开始回答。0 = 关闭。\r\n" +
              "只监控思考，**正式回答永远不监控**。");

            K("answer-room", "答案预留", "string", "auto", "auto", ME,
              "为\"收尾思考 + 写答案\"保留的输出 token，防止思考把 max_tokens 吃光、答案返回空。\r\n" +
              "  auto   — 预留 = min(max(512, 上限/4), 上限/2)。8192 的上限 → 思考最多 6144、留 2048；700 的上限 → 各 350。上限小于 32 时原样放行。\r\n" +
              "  正整数 — 固定预留这么多\r\n" +
              "  0      — 不预留，思考可以用满\r\n" +
              "实测：max_tokens 给 24 时答案会是空字符串，因为 24 小于 32、auto 不预留。");

            K("temperature", "温度", "number", "模型文件里的", null, ME,
              "请求没给 temperature 时的默认值。0 = 贪心。贪心请求在任何其他设置下都保持精确。");

            K("top-p", "核采样 top-p", "number", "模型文件里的", null, ME,
              "保留达到该累积概率的最小集合。(0, 1]，1 表示关闭。");

            K("top-k", "top-k", "integer", "模型文件里的", null, ME,
              "只保留最可能的 K 个 token。0 或 -1 表示不限。");

            K("min-p", "最小概率 min-p", "number", "模型文件里的", null, ME,
              "丢掉概率低于 min-p × 最高概率 的 token。[0, 1]。");

            K("presence-penalty", "存在惩罚", "number", "模型文件里的", null, ME,
              "对出现过的 token 施加固定惩罚，0 = 关闭。");
            K("frequency-penalty", "频率惩罚", "number", "模型文件里的", null, ME,
              "按出现次数施加惩罚，0 = 关闭。");
            K("repetition-penalty", "重复惩罚", "number", "模型文件里的", null, ME,
              "对窗口内出现过的 token 做乘性惩罚，1 = 关闭。");
            K("penalty-last-n", "惩罚窗口", "integer", "64", null, ME,
              "惩罚统计的窗口长度，0 到 4096。");
            K("seed", "随机种子", "integer", "无", null, ME,
              "固定采样的随机抽取，让非贪心的回复可复现。0 也是一个有效种子。");

            // ---------------------------------------------------------- 模型条目：RoPE
            K("rope-scaling", "RoPE 外扩方式", "enum", "模型文件里的，否则 none", "none,linear,yarn", MG,
              "把上下文扩展到模型训练长度之外。\r\n" +
              "  none   — 不外扩（训练长度原生，本模型是 262144）\r\n" +
              "  linear — 位置插值：把位置编号直接除以倍数。便宜，但牺牲高频细节\r\n" +
              "  yarn   — NTK-aware 插值 + 注意力温度校正，同倍数下长上下文质量更好\r\n" +
              "**启动时固定，中途不能改**：KV 里存的是已经旋转过的键，一份缓存只能对应一种缩放。");

            K("rope-scale", "RoPE 外扩倍数", "number", "1", null, MG,
              "linear / yarn 的外扩倍数。必须等于 目标上下文 ÷ 训练长度。\r\n" +
              "本模型训练长度 262144：要跑 524288 就填 2；要跑 384K 就填 1.5。");

            K("rope-freq-base", "RoPE 频率基数", "number", "0（用模型的）", null, MG,
              "底层 ggml 参数。除非你确切知道在做什么，否则保持 0。");
            K("rope-freq-scale", "RoPE 角度缩放", "number", "0（用 1/rope-scale）", null, MG,
              "底层 ggml 参数。除非你确切知道在做什么，否则保持 0。");
            K("yarn-orig-ctx", "YaRN 原始上下文", "number", "0（即 262144）", null, MG,
              "YaRN 校正针对的训练长度。0 表示用 262144，正好是本模型的训练长度。");
            K("yarn-ext-factor", "YaRN 外推因子", "number", "auto", null, MG, "YaRN 参数，语义同 llama.cpp。");
            K("yarn-attn-factor", "YaRN 注意力温度", "number", "1", null, MG, "YaRN 参数，语义同 llama.cpp。");
            K("yarn-beta-fast", "YaRN 高频 beta", "number", "32", null, MG, "YaRN 参数，语义同 llama.cpp。");
            K("yarn-beta-slow", "YaRN 低频 beta", "number", "1", null, MG, "YaRN 参数，语义同 llama.cpp。");

            // ---------------------------------------------------------- 上下文与 KV
            K("max-context", "上下文长度", "integer", "8192", null, MG,
              "最长上下文，被 parallel 个槽共享。KV 缓存随它增长。\r\n" +
              "本模型训练长度 262144，超过就需要开 rope-scaling。\r\n" +
              "大于 32768 且没有 kv-resident / vram-elastic / layer split 时会自动启用 elastic KV：\r\n" +
              "每个会话启动时只 backing 前 32768 格，其余按需从专家缓存借，用完还回去。");

            K("kv", "KV 缓存格式", "enum", "fp16", "fp16,int8,q4_0,k8v4", MG,
              "KV 缓存的存储格式（Flash-Next 取值）：\r\n" +
              "  fp16 — 默认，最占显存\r\n" +
              "  int8 — 省一半显存，实测对质量无损（dKLD +0.0000，区间含 0）\r\n" +
              "  q4_0 — 旋转后 4-bit，约为 int8 的一半，代价 +0.0093 KLD（可测）\r\n" +
              "  k8v4 — K 用 int8、V 用旋转后 4-bit，816 字节/格（int8 是 1056），代价 +0.0056 KLD\r\n" +
              "DeepSeek 只接受 fp8 / fp16，写错会让启动失败。");

            K("kv-resident", "KV 驻留显存格数", "integer", "0（全部在显存）", null, MG,
              "Flash-Next 专用。保留 N 格 KV 在显存里（最少 20480），其余放页锁定内存。\r\n" +
              "省下来的显存给专家缓存。和 kv = k8v4 不兼容。");

            // ---------------------------------------------------------- 专家缓存与内存
            K("vram-reserve-mib", "显存预留", "integer", "700", null, MG,
              "主卡上为计算图、scratch 和输出头保留的显存。下限 256（Linux 128），低于下限启动会拒绝。\r\n" +
              "**带显示器的卡应该调高**：docs/platforms.md 明确警告显示卡在预留低时驱动会把显存换页，\r\n" +
              "而且显存会在两次启动之间有浮动，导致贪心输出逐次不同。");

            K("resident-experts", "专家常驻内存", "switch", "有 profile 时开启", null, MG,
              "Flash-Next 专用。显存放不下的专家保持在页锁定内存里；内存不够时自动退回文件映射。\r\n" +
              "开着就是\"零读盘\"的前提。");

            K("resident-budget-gib", "内存专家预算", "number", "auto", null, MG,
              "最多把多少 GiB 的专家放进内存，最热的优先。auto = 可用内存减去安全下限，\r\n" +
              "并且不超过显卡装不下的那部分专家。");

            K("mmap-experts", "专家用文件映射", "switch", "false", null, MG,
              "Flash-Next 专用。用文件映射代替内存副本。省内存，但每次取专家都要走页缓存，比常驻慢。");

            K("expert-cache", "显存专家槽数", "string", "auto", null, MG,
              "显存里保留多少个专家块。auto = 权重、会话和预留之外剩下的显存能装多少就装多少。\r\n" +
              "填固定数字可以让输出可复现（配合调高 vram-reserve-mib）。");

            K("ram-reserve-mib", "内存预留", "integer", "2048", null, MG,
              "留给系统其余部分的内存。专家层拿的是\"预留之上\"的部分；运行时的 RAM 守卫也守着这条线。\r\n" +
              "内存掉到这条线以下时，引擎会把对话挪到磁盘或丢弃，请求最多等 30 秒。\r\n" +
              "低于 1 GiB 空闲才算 critical，才真正报错。填 0 = 不留、同时关掉守卫。");

            K("resident-cpu-experts", "内存专家走普通内存", "switch", "false", null, MG,
              "配合 mmap-experts：把内存驻留的专家放普通 RAM 而不是特殊区域。");

            K("shared-expert-arena", "共享专家内存文件", "string", "关闭", null, MG,
              "Flash-Next 专用。多个进程共用一个内存文件放常驻专家（Linux 上放 /dev/shm）。Windows 不支持。");

            K("expert-profile", "专家布局文件", "string", "data/profiles/<arch>-<layers>x<experts>.prof", null, MG,
              "启动时按哪个布局文件决定哪些专家先放进显存。引擎自带一份，随版本更新。");

            K("expert-profile-save", "保存专家布局", "string", "关闭", null, MG,
              "退出和定期把学到的布局存下来，下次启动直接用它。\r\n" +
              "文档提醒：**换了工作负载反而会从一个错误的起点开始**，所以要配合同一类任务使用。");

            K("expert-profile-save-every", "布局保存间隔", "number", "10（0 = 只在退出时）", null, MG,
              "保存布局的分钟间隔。需要 expert-profile-save 且自适应层开着。");

            K("vram-elastic", "显存弹性借出", "switch", "false", null, MG,
              "Flash-Next 专用。把专家缓存切成段，让 POST /v1/vram 能在请求之间把显存借给别的程序。\r\n" +
              "开启后 elastic KV 会被关掉。");

            K("vram-segment-mib", "显存弹性段大小", "integer", "512", null, MG,
              "vram-elastic 的段大小，至少 64 MiB。");

            K("peer-reserve-mib", "副卡预留", "integer", "600", null, MG,
              "第二张卡在放完专家和提示缓冲区之后还要空出来的显存。多卡时才相关。");

            // ---------------------------------------------------------- 自适应专家层
            K("adapt-every", "自适应轮次间隔", "integer", "1（有 layer split 时 4）", null, MG,
              "Flash-Next 专用。每 N 个验证窗口做一轮自适应。0 = 关闭。\r\n" +
              "**拉长这个值是最伤性能的**：冷启动和每次话题切换都得等那一轮才追上。文档实测：\r\n" +
              "  默认(1/96/0.85)             冷 241.8  整会话 204.8\r\n" +
              "  adapt-every 64 + swaps 256  冷 160.0  整会话 143.7\r\n" +
              "  adapt-every 1  + swaps 160  冷 230.1  整会话 206.2");

            K("adapt-swaps", "每轮换入专家数", "integer", "96", null, MG,
              "Flash-Next 专用。一轮最多把多少个最热的专家换进主卡显存，0 = 关闭。\r\n" +
              "越大追得越快、搬运字节越多（每个 NVFP4 专家单程约 2.6 MiB）。\r\n" +
              "内存副本会为它留出 4×N 个空位（96 → 384 个）。");

            K("peer-adapt-swaps", "副卡每轮换入数", "integer", "同 adapt-swaps", null, MG,
              "同上，但作用在副卡的链路上。0 = 冻结副卡层。");

            K("adapt-decay", "路由计数衰减", "number", "0.85（有 layer split 时 0.7）", null, MG,
              "每轮结束后路由计数乘以这个系数。\r\n" +
              "调低 = 更快跟上话题切换；调高 = 更保留长期形成的放置。");

            // ---------------------------------------------------------- 提示处理
            K("prefill", "预填充块大小", "string", "auto", null, MG,
              "Flash-Next 专用。一次读进多少 token。可写 auto、auto:N（N 只能是 8192/16384/32768）、或直接写 token 数。\r\n" +
              "GGUF 格式的包 auto = 16384，NVFP4 才是 32768。超过上限的提示会先用一个 16384 的首块。\r\n" +
              "实测：本机把 ple-io 设为 ram 之后，49152 和 auto 在 30k 提示上没有可测差异。");

            K("prefill-cpu-share", "预填充 CPU 占比", "number", "0.6", null, MG,
              "0 到 1。流式取的提示专家里，有多大比例交给 CPU 原地算（不占 PCIe），其余搬到 GPU 算。\r\n" +
              "0 = 全部走 PCIe 到 GPU；1 = 全部 CPU 算。\r\n" +
              "固定值，所以同一份输入每次输出一样。文档实测 0 对 1 是质量中立的（+0.0025，区间含 0）。\r\n" +
              "**只影响预填充，不影响解码**。");

            K("pool-workers", "CPU 专家池线程数", "integer", "auto（P核-1 + E核/2）", null, MG,
              "解码时用多少个线程计算内存里的专家行。不写 = 引擎按 CPU 拓扑自动选：\r\n" +
              "  普通 CPU：物理核数减去一个宿主线程\r\n" +
              "  混合核 CPU（P核 + E核）：P 核减一 + E 核的一半\r\n" +
              "本机 i9-14900KS（8 P + 16 E）自动值 = 15。\r\n" +
              "实测提醒：本机专家缓存命中 97.8%~99.8%，每窗口落到 CPU 的专家极少，\r\n" +
              "把这个值从 15 降到 4 也测不出差别 —— 这个参数在本机杠杆很小。");

            K("short-read", "短提示走解码窗口", "integer", "64", null, MG,
              "Flash-Next 专用。最新的一小段（最多 N 个）token 走解码窗口读，而不是走批量提示路径。0 = 关闭。");

            K("no-prompt-skip", "不跳过提示行", "switch", "false", null, MG,
              "DeepSeek 专用。每一行都过每一层。更慢，数学结果相同。");

            K("no-peer-prefill", "副卡不参与提示", "switch", "false", null, MG,
              "Flash-Next 专用。副卡完全不参与提示计算，提示留在主卡。");

            // ---------------------------------------------------------- 草稿 / 投机解码
            K("spec", "投机解码窗口", "integer", "8（MTP）/ 6（DSpark）/ 0", null, MG,
              "验证窗口最多 N 个 token（最后被接受的那个 + 最多 N-1 个草稿）。\r\n" +
              "0 或 1 = 关闭投机解码。\r\n" +
              "**精确性**：只要不开 relaxed-drafting，输出就是纯解码的结果 —— 文档实测 275 个提示、\r\n" +
              "62,271 行 logit 逐位相同。\r\n" +
              "实测提醒：本机把 spec 从 8 降到 4，散文解码掉 10%；升到 12 没有任何收益。");

            K("draft-experts", "草稿器专家数", "integer", "32", "0,32,64,96,128", MG,
              "DeepSeek 专用。草稿器每个阶段保留多少个最常路由的专家。0 = 全部。");

            K("suffix-draft", "后缀草稿", "integer", "3", null, MG,
              "Flash-Next 专用。提示里如果出现过至少 N+ 个 token 的重复片段，就从那里填充验证窗口。0 = 只用 MTP。");

            K("mtp-max-t", "MTP 窗口上限", "integer", "0（即等于 spec）", null, MG,
              "Flash-Next 专用。限制 MTP 自己的窗口宽度。");

            K("mtp-window", "草稿器窗口格数", "integer", "32768", null, MG,
              "Flash-Next 专用。草稿器只看最后 N 格 KV。0 = 看全部。\r\n" +
              "上下文很长（比如 512K）时，把这里设为 0 可能提高草稿接受率，代价是草稿器注意力更贵。");

            K("relaxed-drafting", "宽松草稿（有损）", "switch", "false", null, MG,
              "**会改变输出。** Flash-Next 专用。采样时保留目标概率达到 P 的草稿，即使目标自己的抽取结果不同。\r\n" +
              "更快但不是精确采样分布。贪心、logit_bias、JSON、工具调用仍然精确。\r\n" +
              "文档实测 P=0.5 提速 +10.3%，P=0.3 +15.9%。启用后启动会打印一条警告。");

            K("route-tail-skip", "跳过尾部专家（有损）", "switch", "false", null, MG,
              "**会改变输出。** Flash-Next 专用。解码窗口里，一个没有任何 GPU 持有的专家如果每次都排在 R 名之后，\r\n" +
              "就既不搬也不算。更快，但比模型本身更不准。需要 device-planned MoE tail。启用后启动会打印警告。");

            K("route-prior", "路由偏置（有损）", "switch", "false", null, MG,
              "**会改变输出。** Flash-Next 专用。挑选十个专家时，给 GPU 持有的专家加 LAMBDA 偏置（权重仍用路由器的）。\r\n" +
              "更快，但更不准。需要 device-planned MoE tail。启用后启动会打印警告。");

            // ---------------------------------------------------------- 提示缓存 / 对话缓存
            K("prompt-cache", "提示检查点数量", "integer", "6", null, MG,
              "保留多少个对话检查点，每个约占 118 MB 内存。0 = 每个提示都从头读。\r\n" +
              "检查点让后续请求能从前缀中间接着读，而不是重新 prefill。日志里 `pp=47262/25722c` 的 c 就是这个。");

            K("prompt-cache-every", "检查点间隔", "integer", "16384", null, MG,
              "Flash-Next 专用。每读进 N 个新 token 也存一个检查点（仅内存）。0 = 关闭。\r\n" +
              "这个值决定重复前缀能复用多少：间隔 16384 时，共享前缀的末尾最多有 16383 个 token 需要重读。");

            K("prompt-cache-root", "系统提示检查点", "integer", "2048", null, MG,
              "系统提示长度达到 N 个 token 时，在它结尾存一个检查点。0 = 从不。");

            K("turn-token", "轮次起始 token", "integer", "248045", null, MG,
              "开启一个对话轮次的 token ID。检查点放在最后一个轮次边界。");

            K("tail-role-token", "尾部角色 token", "integer", "-1（关闭）", null, MG,
              "紧跟在最后一轮之前、以这个 token 开头的短轮次不纳入对话检查点。");

            K("conversation-cache-mib", "对话缓存内存预算", "integer", "0（关闭）", null, MG,
              "停放对话可用的内存 MiB。开了之后，离开 slot / 会话 / 退出时，\r\n" +
              "长度至少 8192 token 的对话会先停在这里；预算满时按最久未用淘汰。");

            K("conversation-cache-slots", "对话缓存槽位数", "integer", "4", null, MG,
              "最多同时停放几个对话。和 conversation-cache-mib 是先到先限的关系。");

            K("conversation-cache-min-free-mib", "停放时保留内存", "integer", "2560", null, MG,
              "停放对话时给系统保留多少物理内存。");

            K("conversation-cache-min-tokens", "最小停放长度", "integer", "0", null, MG,
              "只停放长度至少 N 个 token 的对话（内存和磁盘都适用）。0 = 全都停。");

            K("conversation-cache-disk-mib", "对话缓存磁盘预算", "integer", "0（关闭）", null, MG,
              "停放对话可用的磁盘 MiB。开启后对话会写到模型缓存目录下的 conversations 文件夹，重启后能读回。\r\n" +
              "只存系统提示和最后三轮的状态；文件与生成它的模型和设置绑定，用哈希校验，坏文件会被删掉。\r\n" +
              "写入时始终给卷保留 4 GiB 空闲。");

            K("conversation-cache-dir", "对话缓存目录", "string", "模型缓存目录", null, MG,
              "磁盘层换一个存放目录。");

            // ---------------------------------------------------------- 控制向量
            K("control-vector", "控制向量文件", "string", "关闭", null, MG,
              "Flash-Next 专用。加到残差流上的 GGUF 控制向量，缩放为 1。请求里用 experimental_speed_projection 切换。");
            K("control-vector-scaled", "带缩放的控制向量", "string", "关闭", null, MG,
              "Flash-Next 专用。格式 FILE:SCALE[,...]。");
            K("control-vector-layer-range", "控制向量层范围", "string", "1 到最后一层", null, MG,
              "Flash-Next 专用。作用在哪几层，闭区间，写成 \"A B\"。");
            K("cvec-mode", "控制向量作用方式", "enum", "add", "add,project", MG,
              "add = h += s·v；project = h -= s·(h·v)·v。");
            K("cvec-dir", "控制向量方向", "string", "per-layer", null, MG,
              "per-layer = 每层用自己的方向；single:L = 全部用第 L 层的。");

            // ---------------------------------------------------------- 诊断类（默认不改）
            K("ple-io", "n-gram 表 I/O 方式", "enum", "direct", "direct,ram", MG,
              "引擎里那张 50.66 GiB 的 PLE / n-gram 表（per_layer_token_embd）怎么读：\r\n" +
              "  direct — 从 SSD 按 4 KiB 随机读，表不进内存（官方默认）\r\n" +
              "  ram    — 启动时整块 mmap 并页锁定进内存，之后一次都不读盘\r\n" +
              "**这是本机单项收益最大的一项调优**：实测 30k 提示的预填充从约 2,400 提到约 9,600 tok/s（约 4 倍），\r\n" +
              "代价是常驻内存 +50.7 GiB、启动多约 30～45 秒。解码也小幅受益。\r\n" +
              "文档把它归在 diagnostics.md（\"默认关闭、不是设置项\"），所以默认值是 direct。");

            K("ple-row-cache", "n-gram 行缓存", "integer", "1048576", null, MG,
              "有界行缓存，每行 90 字节。0 = 关闭。ple-io = ram 时基本没用。");
            K("ple-inflight", "n-gram 并发读数", "integer", "256", null, MG,
              "同时发出的 SSD 读数量。");

            // ============================================================ SERVER 段
            K("host", "监听地址", "string", "127.0.0.1", null, SV,
              "监听地址。127.0.0.1 = 只有本机能访问；0.0.0.0 = 局域网也能访问。\r\n" +
              "**0.0.0.0 绝对不能在没有 api-key 的情况下使用。** 启动器会自动传 --api-key，所以安全。\r\n" +
              "注意 api-key 本身不能写进配置文件，只能在命令行给。");

            K("port", "端口", "integer", "8080", null, SV,
              "TCP 端口。网页界面和所有 API 都在这一个端口上。");

            K("allowed-host", "允许的 Host 名", "string-list", "无", null, SV,
              "额外应答哪些 Host 名，可重复。\"*\" = 关掉 Host 检查。\r\n" +
              "放行规则本身对 IP 形式的 Host 直接通过，所以绑 0.0.0.0 后用局域网 IP 访问不需要这个。");

            K("cors-origin", "允许的跨域来源", "string-list", "无", null, SV,
              "允许这个来源的网页调用 /v1/*。可重复。");

            K("image-url-host", "允许的图片主机", "string-list", "无", null, SV,
              "允许其他机器用 http(s) 图片 URL 指定这些主机（HOST 或 HOST:PORT）。可重复。");

            K("image-dir", "允许的图片目录", "string-list", "无", null, SV,
              "允许其他机器指定这些目录下的图片文件。可重复。");

            K("image-remote-only", "本机请求也当远程", "switch", "false", null, SV,
              "把本机发出的请求也当作远程处理，适用于本机有反向代理但没有加转发头的情况。");

            K("max-payload-mib", "请求体上限", "integer", "256", null, SV,
              "服务器读取的最大请求体。超出返回 413，错误里写明上限。");

            K("debug", "调试日志", "switch", "false", null, SV,
              "每个请求打一行日志，并输出模型的原始文本。");

            K("request-lines", "请求摘要行", "switch", "false", null, SV,
              "把每个请求的摘要回显到 stdout，给只看得到这个输出的监管程序用。");

            K("open", "自动打开网页", "switch", "false", null, SV,
              "模型就绪后打开网页界面。绑定 0.0.0.0 时它可能打开一个奇怪的 URL，所以本工具配套的启动器自己打开 127.0.0.1。");

            K("lazy", "延迟加载", "switch", "false", null, SV,
              "启动时不加载模型，第一个请求才加载。会丢弃 sidecar 图像编码器（--vision dynamic 保留）。");

            K("api-key", "API 密钥（不能写在这里）", "string", "无", null, SV,
              "**这个键不能写在配置文件里** —— basalt 设计上拒绝从文件读取 api-key，只接受命令行。\r\n" +
              "本工具不会帮你把它写进去。启动器 scripts\\start_basalt.ps1 会从 .api_key 文件读取并用 --api-key 传入。");
        }

        public static KeyMeta Find(string name, Section section)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name && All[i].In(section)) return All[i];
            // 退回到任意段去查一遍，这样位置放歪的键也还能显示出说明
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name) return All[i];
            return null;
        }

        public static List<KeyMeta> ForSection(Section s)
        {
            List<KeyMeta> list = new List<KeyMeta>();
            for (int i = 0; i < All.Count; i++) if (All[i].In(s)) list.Add(All[i]);
            list.Sort(delegate (KeyMeta a, KeyMeta b) { return string.CompareOrdinal(a.Name, b.Name); });
            return list;
        }

        /// <summary>新增一个键时，给它一个合理的起始值。</summary>
        public static JsonNode DefaultNodeFor(KeyMeta m)
        {
            if (m == null) return JsonNode.NewString("");
            switch (m.Type)
            {
                case "switch":
                    return JsonNode.NewBool(string.Equals(m.Default, "true", StringComparison.OrdinalIgnoreCase));
                case "integer":
                case "number":
                    {
                        string d = m.Default;
                        int cut = d.IndexOfAny(new char[] { '（', '(' });
                        if (cut > 0) d = d.Substring(0, cut);
                        d = d.Trim();
                        double v;
                        if (double.TryParse(d, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out v))
                            return JsonNode.NewNumber(v.ToString(
                                (v == Math.Floor(v)) ? "0" : "0.###",
                                System.Globalization.CultureInfo.InvariantCulture));
                        return JsonNode.NewNumber("0");
                    }
                case "string-list":
                    return JsonNode.NewArray();
                default:
                    return JsonNode.NewString(m.Default == null ? "" : m.Default);
            }
        }
    }
}
