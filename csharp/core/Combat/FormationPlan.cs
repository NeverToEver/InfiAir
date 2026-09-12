namespace InfiAir.Core.Combat;

/// <summary>
/// 轰炸编队的编队几何与投弹时刻表（纯逻辑，零 Godot 依赖）：
/// 楔形槽位、投弹名次、投弹时刻表这三件事实只在这里算一份，引擎侧 <c>FormationStrikeEvent</c>
/// 只做逐帧消费与节点写入。
/// 下沉的理由是消费侧的两个前提必须可测：名次定序（同名次不能靠排序实现随意决定）与
/// 时刻表单调（事件按状态机时间贪心消费，乱序表会把后续波次堆到同一帧）。
/// </summary>
public static class FormationPlan
{
    /// <summary>槽位偏移（相对编队锚点、未随航向旋转；长机恒在原点）。</summary>
    public readonly struct Slot
    {
        public Slot(float x, float y)
        {
            X = x;
            Y = y;
        }

        public readonly float X;

        public readonly float Y;

        /// <summary>到长机的距离平方（名次排序只比较这个量，不开方）。</summary>
        public float LengthSquared => (X * X) + (Y * Y);
    }

    /// <summary>投弹时刻表：<see cref="Times"/> 单调不减，<see cref="Crafts"/> 是对应槽位索引。</summary>
    public sealed class DropSchedule
    {
        public static readonly DropSchedule Empty = new([], []);

        public DropSchedule(float[] times, int[] crafts)
        {
            Times = times;
            Crafts = crafts;
        }

        public float[] Times { get; }

        public int[] Crafts { get; }

        /// <summary>末枚炸弹时刻（轰炸段的计划长度；无弹时为 0）。</summary>
        public float LastTime => Times.Length == 0 ? 0.0f : Times[^1];
    }

    /// <summary>楔形编队槽位：长机居中在原点，僚机左右交替后掠，每两架向后退一档 <paramref name="wingStep"/>。</summary>
    public static Slot[] Wedge(int count, float wingStep)
    {
        if (count <= 0)
        {
            return [];
        }

        var slots = new Slot[count];
        slots[0] = new Slot(0.0f, 0.0f);
        for (var i = 1; i < count; i++)
        {
            var side = i % 2 == 1 ? -1.0f : 1.0f;
            var tier = (i + 1) / 2; // 整数级差：0,1,1,2,2,…
            slots[i] = new Slot(side * wingStep * tier, wingStep * tier);
        }

        return slots;
    }

    /// <summary>投弹名次：按到长机距离升序，距离相同时按槽位索引定序
    /// （楔形天然存在等距的两架，定序必须确定，否则名次随排序实现漂移）。</summary>
    public static int[] DropRank(Slot[] slots)
    {
        var order = new int[slots.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        System.Array.Sort(order, (a, b) =>
        {
            var cmp = slots[a].LengthSquared.CompareTo(slots[b].LengthSquared);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        var rank = new int[slots.Length];
        for (var i = 0; i < order.Length; i++)
        {
            rank[order[i]] = i;
        }

        return rank;
    }

    /// <summary>投弹时刻表：波次外层 × 槽位内层 × 每机连投，逐项以 (时刻, 生成序) 排序后输出。
    /// 生成序作第二键，使等距/等时刻的条目顺序与配置和排序实现无关。
    /// 波次数/每机弹数 ≤0 时返回空表（调用侧的域钳已排除，这里只保证不产生空转或负时长）。</summary>
    public static DropSchedule Schedule(
        int[] rank, int batches, int bombsPerCraft, float bombInterval, float bombStagger, float volleyGap)
    {
        if (rank.Length == 0 || batches <= 0 || bombsPerCraft <= 0)
        {
            return DropSchedule.Empty;
        }

        var count = batches * rank.Length * bombsPerCraft;
        var times = new float[count];
        var crafts = new int[count];
        var seq = new int[count];
        var cursor = 0;
        for (var b = 0; b < batches; b++)
        {
            for (var craft = 0; craft < rank.Length; craft++)
            {
                for (var k = 0; k < bombsPerCraft; k++)
                {
                    times[cursor] = (b * volleyGap) + (rank[craft] * bombInterval) + (k * bombStagger);
                    crafts[cursor] = craft;
                    seq[cursor] = cursor;
                    cursor++;
                }
            }
        }

        System.Array.Sort(seq, (a, b) =>
        {
            var cmp = times[a].CompareTo(times[b]);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        var sortedTimes = new float[count];
        var sortedCrafts = new int[count];
        for (var i = 0; i < count; i++)
        {
            sortedTimes[i] = times[seq[i]];
            sortedCrafts[i] = crafts[seq[i]];
        }

        return new DropSchedule(sortedTimes, sortedCrafts);
    }
}
