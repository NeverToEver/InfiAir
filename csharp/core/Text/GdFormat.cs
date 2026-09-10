using System.Globalization;
using System.Text;

namespace InfiAir.Core.Text;

/// <summary>
/// GDScript `%` 运算符等价格式化。
/// 全库唯一实现（core 纯 .NET，零 Godot 依赖）。
///
/// 语义基准（全库唯一支持 %.Nf 与 %f 固定 6 位小数的实现）：
/// - %s：参数 ToString()；%d：Convert.ToInt64（GDScript int() 语义近似）；
/// - %f：固定 6 位小数 Invariant（"0.000000"）；%.Nf：N 位小数 Invariant；
/// - %%：转义字面百分号；未知 spec 原样保留；
/// - 参数越界统一输出 "?"，不得抛 IndexOutOfRange。
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
                    sb.Append(v is string s && s == "?" ? "?" : FormatInt(v));
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
                    // j 必须从 '.' 之后（i+2）扫描位数——从 '.' 处（i+1）起扫时
                    // char.IsDigit('.') 恒假，%.Nf 永不匹配，UI_DIFF_FMT
                    // 会渲染为字面 "难度 x%.2f · 中"
                    var j = i + 2;
                    var digits = "";
                    while (j < format.Length && char.IsDigit(format[j]))
                    {
                        digits += format[j];
                        j++;
                    }

                    if (j < format.Length && format[j] == 'f')
                    {
                        // 精度位必须 int.TryParse + 上限 99 守卫——超长精度串
                        // int.Parse 抛 OverflowException、超大精度 new string 巨额分配；非法精度按
                        // 未知 spec 原样保留（对齐 FormatInt 只吞 OverflowException 的加固口径）
                        var precision = 0;
                        if (digits.Length > 0 && (!int.TryParse(digits, out precision) || precision > 99))
                        {
                            sb.Append(c);
                            continue;
                        }

                        var prec = digits.Length > 0 ? precision : 0;
                        var fmt = "0." + new string('0', prec);
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

    /// <summary>%d 安全转换：仅吞 OverflowException——±Infinity/超
    /// long 域 double 的 Convert.ToInt64 抛此异常（配置/存档数据驱动的参数可达）；
    /// 类型错误（FormatException/InvalidCastException）按既定契约照抛不吞
    /// （与 GDScript % 格式化对非数值参数的报错语义一致）。</summary>
    private static string FormatInt(object? v)
    {
        try
        {
            return Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return "?";
        }
    }
}
