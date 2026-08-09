using System.Globalization;
using System.Text;

namespace InfiAir.Core.Text;

/// <summary>
/// GDScript `%` 运算符等价格式化（Y 系列收敛，2026-08-09）。
/// 合并原 Godot 层 11 份重复实现（Hud/Main/BaseConsole/GameState/Mothership/IntroCinematic/
/// ReturnCinematic/Tutorial/SettingsUi×2/BuffSelect.GsFormat）为单一 core 纯 .NET 实现：
/// 零 Godot 依赖 → xUnit 直测（tests-csharp/GdFormatTests.cs）。
///
/// 语义基准 = Hud 标准版（全库唯一支持 %.Nf 与 %f 固定 6 位小数的实现）：
/// - %s：参数 ToString()；%d：Convert.ToInt64（GDScript int() 语义近似）；
/// - %f：固定 6 位小数 Invariant（"0.000000"）；%.Nf：N 位小数 Invariant；
/// - %%：转义字面百分号；未知 spec 原样保留；
/// - 参数越界统一输出 "?"（原 11 份实现 4 种兜底并存——Hud 抛 IndexOutOfRange 为缺陷，
///   "?" 为 6 份主流一致语义；行为收紧）。
/// </summary>
public static class GdFormat
{
    public static string Format(string format, params object[] args)
    {
        var sb = new StringBuilder(format.Length + 16);
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '%' && i + 1 < format.Length)
            {
                var spec = format[i + 1];
                if (spec == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                if (spec == 's')
                {
                    sb.Append(Arg(args, ref argIndex));
                    i++;
                    continue;
                }

                if (spec == 'd')
                {
                    var v = Arg(args, ref argIndex);
                    sb.Append(v is string s && s == "?" ? "?" : Convert.ToInt64(v));
                    i++;
                    continue;
                }

                if (spec == 'f')
                {
                    var v = Arg(args, ref argIndex);
                    sb.Append(v is string s && s == "?" ? "?" : Convert.ToDouble(v).ToString("0.000000", CultureInfo.InvariantCulture));
                    i++;
                    continue;
                }

                if (spec == '.')
                {
                    // 2026-08-09 Y 系列：j 从 '.' 之后（i+2）扫描位数——原 11 份实现均从
                    // '.' 处（i+1）起扫，char.IsDigit('.') 恒假 → %.Nf 永不匹配，UI_DIFF_FMT
                    // 实际渲染为字面 "难度 x%.2f · 中"（GDScript 迁移缺陷）；此处为行为修复
                    var j = i + 2;
                    var digits = "";
                    while (j < format.Length && char.IsDigit(format[j]))
                    {
                        digits += format[j];
                        j++;
                    }

                    if (j < format.Length && format[j] == 'f')
                    {
                        var precision = digits.Length > 0 ? int.Parse(digits) : 0;
                        var fmt = "0." + new string('0', precision);
                        var v = Arg(args, ref argIndex);
                        sb.Append(v is string s && s == "?" ? "?" : Convert.ToDouble(v).ToString(fmt, CultureInfo.InvariantCulture));
                        i = j;
                        continue;
                    }
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>参数取值 + 越界兜底（统一 "?"，与主流 6 份实现一致）。</summary>
    private static object? Arg(object[] args, ref int argIndex)
    {
        if (argIndex >= args.Length)
        {
            return "?";
        }

        return args[argIndex++];
    }
}
