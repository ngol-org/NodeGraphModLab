using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.BuiltinNodes.Logic;

/// <summary>
/// 二つの数値を加算する。
/// </summary>
[NodeType("ngol.logic.add", "Logic/Math", "Add Numbers",
    Description = "Add two numeric values and output the result.")]
[NodePort("a", PortDirection.Input, "number", IsRequired = true, ShowInlineEditor = true, Description = "First operand")]
[NodePort("b", PortDirection.Input, "number", IsRequired = true, ShowInlineEditor = true, Description = "Second operand")]
[NodePort("result", PortDirection.Output, "number", Description = "a + b")]
public sealed class AddNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var a = ToDouble(ctx.GetPortValue("a") ?? ctx.GetParam<object>("a"));
        var b = ToDouble(ctx.GetPortValue("b") ?? ctx.GetParam<object>("b"));
        ctx.SetPortValue("result", a + b);
        ctx.Logger.LogDebug($"AddNode: {a} + {b} = {a + b}");
    }

    internal static double ToDouble(object? v)
    {
        if (v == null) return 0.0;
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is int i) return i;
        if (v is long l) return l;
        if (double.TryParse(v.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r)) return r;
        return 0.0;
    }
}

/// <summary>
/// 値をログ出力するノード。
/// </summary>
[NodeType("ngol.logic.log", "Logic/Debug", "Log Value",
    Description = "Log a value to the host console.")]
[NodePort("value", PortDirection.Input, "any", Description = "Value to log")]
[NodePort("label", PortDirection.Input, "string", ShowInlineEditor = true, Description = "Optional label prefix")]
public sealed class LogValueNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var label = ctx.GetPortValue("label") as string ?? ctx.GetParam<string>("label") ?? "Log";
        var value = ctx.GetPortValue("value");
        ctx.Logger.LogInfo($"[{label}] {value}");
    }
}

/// <summary>
/// 定数値を出力するノード。
/// </summary>
[NodeType("ngol.logic.const_number", "Logic/Values", "Constant Number",
    Description = "Output a constant numeric value.")]
[NodePort("value", PortDirection.Output, "number", ShowInlineEditor = true, Description = "The constant value")]
public sealed class ConstantNumberNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var v = ctx.GetParam<double>("value");
        ctx.SetPortValue("value", v);
    }
}

/// <summary>
/// 定数文字列を出力するノード。
/// </summary>
[NodeType("ngol.logic.const_string", "Logic/Values", "Constant String",
    Description = "Output a constant string value.")]
[NodePort("value", PortDirection.Output, "string", ShowInlineEditor = true, Description = "The constant string")]
public sealed class ConstantStringNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var v = ctx.GetParam<string>("value") ?? "";
        ctx.SetPortValue("value", v);
    }
}

