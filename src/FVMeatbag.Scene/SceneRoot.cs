using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene;

/// <summary>
/// The root scene. Its graph is a child of the application's root graph; every component's graph
/// hangs below it in the same shape as the component tree.
/// </summary>
public sealed class SceneRoot
{
    public SceneRoot(Graph.Graph parentGraph, string name = "Scene")
    {
        Graph = parentGraph.CreateChild(name);
        Root = new SceneComponent("Root");
        Root.Attach(this);
    }

    public Graph.Graph Graph { get; }
    public SceneComponent Root { get; }
    public SceneComponent? Selected { get; private set; }
    public GizmoComponent? Gizmo { get; private set; }

    public event EventHandler? SelectionChanged;

    /// <summary>Selects a component and attaches a transform gizmo that follows it. Pass null to clear.</summary>
    public void Select(SceneComponent? component)
    {
        if (component == Selected)
        {
            return;
        }
        if (component is not null && component.Scene != this)
        {
            throw new InvalidOperationException($"{component.Name} is not part of this scene.");
        }

        using (Graph.Evaluator.BeginBatch())
        {
            if (Gizmo is not null)
            {
                Root.RemoveChild(Gizmo);
                Gizmo = null;
            }
            Selected = component;
            if (component is not null)
            {
                Gizmo = Root.AddChild(new GizmoComponent(component));
            }
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
