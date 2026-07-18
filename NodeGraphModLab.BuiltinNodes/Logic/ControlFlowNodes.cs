using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.BuiltinNodes.Logic;

/// <summary>
/// ForEach ループノード。リストの各要素を反復処理する。
/// IForEachController を実装し、GraphExecutor がループ展開する。
/// </summary>
[NodeType("ngol.logic.foreach", "Logic/Control", "ForEach",
    Description = "Iterate over a list. Downstream nodes run once per item.")]
[NodePort("items",        PortDirection.Input,  "any[]",   IsRequired = true, Description = "List of items to iterate")]
[NodePort("current_item", PortDirection.Output, "any",     Description = "Current item in the loop")]
[NodePort("index",        PortDirection.Output, "number",  Description = "Current 0-based loop index")]
[NodePort("count",        PortDirection.Output, "number",  Description = "Total item count")]
public sealed class ForEachNode : INode, IForEachController
{
    public string CurrentItemPortName => "current_item";
    public string IndexPortName       => "index";

    public IReadOnlyList<object?> GetItems(IReadOnlyDictionary<string, object?> inputValues)
    {
        if (!inputValues.TryGetValue("items", out var raw)) return Array.Empty<object?>();
        return raw switch
        {
            IReadOnlyList<object?> rol  => rol,
            IList<object?>         il   => il.ToList(),
            // string は IEnumerable だが文字に展開しない
            string s => new List<object?> { s },
            System.Collections.IEnumerable en => en.Cast<object?>().ToList(),
            _ => raw == null ? Array.Empty<object?>() : new List<object?> { raw }
        };
    }

    public void Execute(IExecutionContext ctx)
    {
        // count だけ出力する（current_item と index は GraphExecutor が設定する）
        var items = GetItems(
            new Dictionary<string, object?> { ["items"] = ctx.GetPortValue("items") });
        ctx.SetPortValue("count", (double)items.Count);
        ctx.Logger.LogDebug($"ForEachNode: {items.Count} items");
    }
}

/// <summary>
/// 条件分岐ノード。condition が true なら then_value、false なら else_value を出力する。
/// </summary>
[NodeType("ngol.logic.if", "Logic/Control", "If",
    Description = "Output then_value when condition is true, else_value otherwise.")]
[NodePort("condition",  PortDirection.Input, "boolean", IsRequired = true, ShowInlineEditor = true, Description = "Branch condition")]
[NodePort("then_value", PortDirection.Input, "any", Description = "Value when condition is true")]
[NodePort("else_value", PortDirection.Input, "any", Description = "Value when condition is false")]
[NodePort("result",     PortDirection.Output, "any", Description = "Selected value")]
[NodePort("branch",     PortDirection.Output, "boolean", Description = "Passes through the condition value")]
public sealed class IfNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var cond = ctx.GetPortValue("condition");
        var boolVal = cond switch
        {
            bool b => b,
            int i  => i != 0,
            double d => d != 0.0,
            string s => bool.TryParse(s, out var p) && p,
            _ => cond != null,
        };

        var result = boolVal ? ctx.GetPortValue("then_value") : ctx.GetPortValue("else_value");
        ctx.SetPortValue("result", result);
        ctx.SetPortValue("branch", boolVal);
        ctx.Logger.LogDebug($"IfNode: condition={boolVal}, result={result}");
    }
}

/// <summary>
/// 数値比較ノード。二つの数値を比較して bool を出力する。
/// </summary>
[NodeType("ngol.logic.compare", "Logic/Control", "Compare Numbers",
    Description = "Compare two numeric values.")]
[NodePort("a",        PortDirection.Input, "number", IsRequired = true, ShowInlineEditor = true, Description = "Left operand")]
[NodePort("b",        PortDirection.Input, "number", IsRequired = true, ShowInlineEditor = true, Description = "Right operand")]
[NodePort("operator", PortDirection.Input, "string", ShowInlineEditor = true, Description = "Operator: eq|ne|lt|le|gt|ge (default: eq)")]
[NodePort("result",   PortDirection.Output, "boolean", Description = "Comparison result")]
public sealed class CompareNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var a  = ToDouble(ctx.GetPortValue("a"));
        var b  = ToDouble(ctx.GetPortValue("b"));
        var op = ctx.GetPortValue("operator") as string
              ?? ctx.GetParam<string>("operator")
              ?? "eq";

        var result = op.ToLowerInvariant() switch
        {
            "eq" or "==" => a == b,
            "ne" or "!=" => a != b,
            "lt" or "<"  => a <  b,
            "le" or "<="  => a <= b,
            "gt" or ">"  => a >  b,
            "ge" or ">=" => a >= b,
            _ => false,
        };

        ctx.SetPortValue("result", result);
        ctx.Logger.LogDebug($"CompareNode: {a} {op} {b} = {result}");
    }

    private static double ToDouble(object? v) => v switch
    {
        double d => d,
        float f  => f,
        int i    => i,
        long l   => l,
        string s when double.TryParse(s, out var p) => p,
        _ => 0.0,
    };
}

/// <summary>
/// 文字列操作ノード。Format / Concat / Contains を選択して実行する。
/// </summary>
[NodeType("ngol.logic.string_op", "Logic/String", "String Operation",
    Description = "Perform a string operation (format / concat / contains).")]
[NodePort("input",     PortDirection.Input, "string", IsRequired = true, Description = "Primary string")]
[NodePort("secondary", PortDirection.Input, "string", Description = "Secondary string (concat/contains)")]
[NodePort("operation", PortDirection.Input, "string", ShowInlineEditor = true, Description = "Operation: format|concat|contains|length (default: concat)")]
[NodePort("result",    PortDirection.Output, "string", Description = "String result")]
[NodePort("bool_out",  PortDirection.Output, "boolean", Description = "Boolean result (contains)")]
[NodePort("number_out",PortDirection.Output, "number",  Description = "Numeric result (length)")]
public sealed class StringOpNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var input  = ctx.GetPortValue("input") as string
                  ?? ctx.GetPortValue("input")?.ToString() ?? "";
        var secondary = ctx.GetPortValue("secondary") as string
                     ?? ctx.GetPortValue("secondary")?.ToString() ?? "";
        var op = ctx.GetPortValue("operation") as string
              ?? ctx.GetParam<string>("operation")
              ?? "concat";

        switch (op.ToLowerInvariant())
        {
            case "format":
                try { ctx.SetPortValue("result", string.Format(input, secondary)); }
                catch { ctx.SetPortValue("result", input); }
                break;
            case "concat":
                ctx.SetPortValue("result", input + secondary);
                break;
            case "contains":
                ctx.SetPortValue("bool_out", input.IndexOf(secondary, StringComparison.Ordinal) >= 0);
                ctx.SetPortValue("result", input);
                break;
            case "length":
                ctx.SetPortValue("number_out", (double)input.Length);
                ctx.SetPortValue("result", input);
                break;
            default:
                ctx.SetPortValue("result", input);
                break;
        }
        ctx.Logger.LogDebug($"StringOpNode: op={op} input='{input}'");
    }
}
