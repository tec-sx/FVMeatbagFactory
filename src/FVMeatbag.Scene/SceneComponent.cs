using System.Numerics;
using FVMeatbag.Graph;
using FVMeatbag.Graph.Ports;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene;

/// <summary>
/// A visual element of the scene. Each attached component owns a child graph that mirrors the
/// scene hierarchy (the component's graph is a child of its parent component's graph), so removing
/// a component tears down all of its behaviour in one step.
/// </summary>
/// <remarks>
/// Components never own document data. They read it from the project hierarchy through nodes in
/// their graph and only describe how it is displayed and interacted with.
/// </remarks>
public class SceneComponent
{
    private readonly List<SceneComponent> _children = [];
    private Transform _localTransform = Transform.Identity;

    public SceneComponent(string name)
    {
        Name = name;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public SceneComponent? Parent { get; private set; }
    public IReadOnlyList<SceneComponent> Children => _children;

    /// <summary>The scene this component is attached to, or null.</summary>
    public SceneRoot? Scene { get; private set; }

    /// <summary>This component's behaviour graph; exists while the component is attached.</summary>
    public Graph.Graph? Graph { get; private set; }

    /// <summary>Computes <see cref="WorldMatrix"/>; exists while the component is attached.</summary>
    public Node<WorldTransformInput, WorldTransformOutput>? TransformNode { get; private set; }

    public Transform LocalTransform
    {
        get => _localTransform;
        set
        {
            _localTransform = value;
            TransformNode?.UpdateInputs((ref WorldTransformInput i) => i.Local = value);
        }
    }

    public Matrix4x4 WorldMatrix => TransformNode?.OutputValues.World ?? _localTransform.ToMatrix();

    public T AddChild<T>(T child) where T : SceneComponent
    {
        if (child.Parent is not null)
        {
            throw new InvalidOperationException($"{child.Name} already has a parent.");
        }
        _children.Add(child);
        child.Parent = this;
        if (Scene is not null)
        {
            child.Attach(Scene);
        }
        return child;
    }

    public bool RemoveChild(SceneComponent child)
    {
        if (!_children.Contains(child))
        {
            return false;
        }
        child.Detach();
        _children.Remove(child);
        child.Parent = null;
        return true;
    }

    public IEnumerable<SceneComponent> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var component in child.DescendantsAndSelf())
            {
                yield return component;
            }
        }
    }

    public override string ToString() => Name;

    /// <summary>Builds the component's behaviour nodes. <see cref="TransformNode"/> already exists.</summary>
    protected virtual void OnAttached(Graph.Graph graph)
    {
    }

    /// <summary>Called before the component's graph is torn down.</summary>
    protected virtual void OnDetaching()
    {
    }

    /// <summary>The output this component's world transform is relative to. Defaults to the parent's world matrix.</summary>
    protected virtual OutputPort? ResolveParentWorld() => Parent?.TransformNode?.Output(nameof(WorldTransformOutput.World));

    internal void Attach(SceneRoot scene)
    {
        var parentGraph = Parent?.Graph ?? scene.Graph;
        Scene = scene;
        Graph = parentGraph.CreateChild(Name);
        using (Graph.Evaluator.BeginBatch())
        {
            TransformNode = Graph.AddNode(
                new WorldTransformOperation(),
                $"{Name}.Transform",
                new WorldTransformInput { Local = _localTransform, ParentWorld = Matrix4x4.Identity });
            if (ResolveParentWorld() is { } parentWorld)
            {
                Graph.Connect(parentWorld, TransformNode.Input(nameof(WorldTransformInput.ParentWorld)));
            }
            OnAttached(Graph);
            foreach (var child in _children)
            {
                child.Attach(scene);
            }
        }
    }

    internal void Detach()
    {
        if (Scene is { } scene && scene.Selected == this)
        {
            scene.Select(null);
        }
        foreach (var child in _children)
        {
            child.Detach();
        }
        OnDetaching();
        if (Graph?.Parent is { } parentGraph)
        {
            parentGraph.RemoveChild(Graph);
        }
        Graph = null;
        TransformNode = null;
        Scene = null;
    }
}
