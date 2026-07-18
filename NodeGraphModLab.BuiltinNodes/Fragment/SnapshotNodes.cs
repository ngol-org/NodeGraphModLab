using System.Collections;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.BuiltinNodes.Fragment;

// ============================================================
// 断片グラフ連携用スナップショットノード群
// カテゴリ: Fragment/Snapshot
//
// 共通動作:
//   1. 入力ポートから値を受け取る（ポート名 = 型名）
//   2. ISnapshotStore に (nodeInstanceId, ポート名) キーで保存
//   3. 出力ポートに値をそのまま流す（同一断片内での連結を許可）
//
// list 型のみ IEnumerable を実行時に List<object?> に materialize して保存する。
// ============================================================

[NodeType("ngol.snapshot.any", "Fragment/Snapshot", "Snapshot (Any)",
    Description = "任意型の値をスナップショットとして保存する")]
[NodePort("value", PortDirection.Input, "any")]
[NodePort("value", PortDirection.Output, "any")]
public sealed class SnapshotAnyNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("value");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "value", value);
        ctx.SetPortValue("value", value);
    }
}

[NodeType("ngol.snapshot.number", "Fragment/Snapshot", "Snapshot (Number)",
    Description = "数値型の値をスナップショットとして保存する")]
[NodePort("number", PortDirection.Input, "number")]
[NodePort("number", PortDirection.Output, "number")]
public sealed class SnapshotNumberNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("number");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "number", value);
        ctx.SetPortValue("number", value);
    }
}

[NodeType("ngol.snapshot.string", "Fragment/Snapshot", "Snapshot (String)",
    Description = "文字列型の値をスナップショットとして保存する")]
[NodePort("string", PortDirection.Input, "string")]
[NodePort("string", PortDirection.Output, "string")]
public sealed class SnapshotStringNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("string");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "string", value);
        ctx.SetPortValue("string", value);
    }
}

[NodeType("ngol.snapshot.bool", "Fragment/Snapshot", "Snapshot (Bool)",
    Description = "真偽値型の値をスナップショットとして保存する")]
[NodePort("bool", PortDirection.Input, "bool")]
[NodePort("bool", PortDirection.Output, "bool")]
public sealed class SnapshotBoolNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("bool");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "bool", value);
        ctx.SetPortValue("bool", value);
    }
}

[NodeType("ngol.snapshot.gameobject", "Fragment/Snapshot", "Snapshot (GameObject)",
    Description = "GameObject の参照をスナップショットとして保存する")]
[NodePort("gameobject", PortDirection.Input, "gameobject")]
[NodePort("gameobject", PortDirection.Output, "gameobject")]
public sealed class SnapshotGameObjectNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("gameobject");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "gameobject", value);
        ctx.SetPortValue("gameobject", value);
    }
}

[NodeType("ngol.snapshot.material", "Fragment/Snapshot", "Snapshot (Material)",
    Description = "Material の参照をスナップショットとして保存する")]
[NodePort("material", PortDirection.Input, "material")]
[NodePort("material", PortDirection.Output, "material")]
public sealed class SnapshotMaterialNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("material");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "material", value);
        ctx.SetPortValue("material", value);
    }
}

[NodeType("ngol.snapshot.transform", "Fragment/Snapshot", "Snapshot (Transform)",
    Description = "Transform コンポーネントをスナップショットとして保存する")]
[NodePort("transform", PortDirection.Input, "transform")]
[NodePort("transform", PortDirection.Output, "transform")]
public sealed class SnapshotTransformNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("transform");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "transform", value);
        ctx.SetPortValue("transform", value);
    }
}

[NodeType("ngol.snapshot.component", "Fragment/Snapshot", "Snapshot (Component)",
    Description = "汎用 Component をスナップショットとして保存する (Rigidbody, Collider 等)")]
[NodePort("component", PortDirection.Input, "component")]
[NodePort("component", PortDirection.Output, "component")]
public sealed class SnapshotComponentNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("component");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "component", value);
        ctx.SetPortValue("component", value);
    }
}

[NodeType("ngol.snapshot.rigidbody", "Fragment/Snapshot", "Snapshot (Rigidbody)",
    Description = "Rigidbody コンポーネントをスナップショットとして保存する")]
[NodePort("rigidbody", PortDirection.Input, "rigidbody")]
[NodePort("rigidbody", PortDirection.Output, "rigidbody")]
public sealed class SnapshotRigidbodyNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("rigidbody");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "rigidbody", value);
        ctx.SetPortValue("rigidbody", value);
    }
}

[NodeType("ngol.snapshot.collider", "Fragment/Snapshot", "Snapshot (Collider)",
    Description = "Collider コンポーネントをスナップショットとして保存する")]
[NodePort("collider", PortDirection.Input, "collider")]
[NodePort("collider", PortDirection.Output, "collider")]
public sealed class SnapshotColliderNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("collider");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "collider", value);
        ctx.SetPortValue("collider", value);
    }
}

[NodeType("ngol.snapshot.vector2", "Fragment/Snapshot", "Snapshot (Vector2)",
    Description = "Vector2 をスナップショットとして保存する")]
[NodePort("vector2", PortDirection.Input, "vector2")]
[NodePort("vector2", PortDirection.Output, "vector2")]
public sealed class SnapshotVector2Node : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("vector2");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "vector2", value);
        ctx.SetPortValue("vector2", value);
    }
}

[NodeType("ngol.snapshot.vector3", "Fragment/Snapshot", "Snapshot (Vector3)",
    Description = "Vector3 をスナップショットとして保存する")]
[NodePort("vector3", PortDirection.Input, "vector3")]
[NodePort("vector3", PortDirection.Output, "vector3")]
public sealed class SnapshotVector3Node : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("vector3");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "vector3", value);
        ctx.SetPortValue("vector3", value);
    }
}

[NodeType("ngol.snapshot.vector4", "Fragment/Snapshot", "Snapshot (Vector4)",
    Description = "Vector4 をスナップショットとして保存する")]
[NodePort("vector4", PortDirection.Input, "vector4")]
[NodePort("vector4", PortDirection.Output, "vector4")]
public sealed class SnapshotVector4Node : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("vector4");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "vector4", value);
        ctx.SetPortValue("vector4", value);
    }
}

[NodeType("ngol.snapshot.color", "Fragment/Snapshot", "Snapshot (Color)",
    Description = "Color をスナップショットとして保存する")]
[NodePort("color", PortDirection.Input, "color")]
[NodePort("color", PortDirection.Output, "color")]
public sealed class SnapshotColorNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("color");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "color", value);
        ctx.SetPortValue("color", value);
    }
}

[NodeType("ngol.snapshot.quaternion", "Fragment/Snapshot", "Snapshot (Quaternion)",
    Description = "Quaternion をスナップショットとして保存する")]
[NodePort("quaternion", PortDirection.Input, "quaternion")]
[NodePort("quaternion", PortDirection.Output, "quaternion")]
public sealed class SnapshotQuaternionNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("quaternion");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "quaternion", value);
        ctx.SetPortValue("quaternion", value);
    }
}

[NodeType("ngol.snapshot.rect", "Fragment/Snapshot", "Snapshot (Rect)",
    Description = "Rect をスナップショットとして保存する")]
[NodePort("rect", PortDirection.Input, "rect")]
[NodePort("rect", PortDirection.Output, "rect")]
public sealed class SnapshotRectNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("rect");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "rect", value);
        ctx.SetPortValue("rect", value);
    }
}

[NodeType("ngol.snapshot.bounds", "Fragment/Snapshot", "Snapshot (Bounds)",
    Description = "Bounds をスナップショットとして保存する")]
[NodePort("bounds", PortDirection.Input, "bounds")]
[NodePort("bounds", PortDirection.Output, "bounds")]
public sealed class SnapshotBoundsNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("bounds");
        var allowNull = ctx.GetParam<bool>("allowNull");
        if (value is not null || allowNull) ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "bounds", value);
        ctx.SetPortValue("bounds", value);
    }
}

[NodeType("ngol.snapshot.list", "Fragment/Snapshot", "Snapshot (List)",
    Description = "IEnumerable をスナップショット保存時に List<object?> に materialize して固定する")]
[NodePort("list", PortDirection.Input, "list")]
[NodePort("list", PortDirection.Output, "list")]
public sealed class SnapshotListNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var raw = ctx.GetPortValue("list");

        // IEnumerable を保存前に materialize して遅延列挙を防ぐ
        object? materialized = raw switch
        {
            null => null,
            IEnumerable enumerable => Materialize(enumerable),
            _ => raw
        };

        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "list", materialized);
        ctx.SetPortValue("list", materialized);
    }

    private static List<object?> Materialize(IEnumerable source)
    {
        var list = new List<object?>();
        foreach (var item in source)
            list.Add(item);
        return list;
    }
}
