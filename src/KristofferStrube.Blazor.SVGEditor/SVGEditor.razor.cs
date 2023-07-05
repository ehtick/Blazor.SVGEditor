﻿using AngleSharp;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using KristofferStrube.Blazor.SVGEditor.MenuItems.CompleteNewShape;
using KristofferStrube.Blazor.SVGEditor.MenuItems.AddNewSVGElement;
using KristofferStrube.Blazor.SVGEditor.MenuItems.Action;
using KristofferStrube.Blazor.SVGEditor.ShapeEditors;

namespace KristofferStrube.Blazor.SVGEditor;

public partial class SVGEditor : ComponentBase
{
    private string? _input;
    private ElementReference SVGElementReference;
    private List<Shape>? ColorPickerShapes;
    private string? ColorPickerAttributeName;
    private Action<string>? ColorPickerSetter;
    private (double x, double y)? TranslatePanner;
    private readonly Subject<ISVGElement> ElementSubject = new();
    private List<Shape>? BoxSelectionShapes;
    private string ColorPickerTitle => $"Pick {ColorPickerAttributeName} Color";
    private bool IsColorPickerOpen => ColorPickerShapes is not null;

    [Parameter, EditorRequired]
    public string Input { get; set; } = string.Empty;

    [Parameter]
    public Action<string>? InputUpdated { get; set; }

    [Parameter]
    public bool SnapToInteger { get; set; } = false;

    [Parameter]
    public SelectionMode SelectionMode { get; set; } = SelectionMode.WindowSelection;

    [Parameter]
    public bool DisableContextMenu { get; set; }

    [Parameter]
    public bool DisableZoom { get; set; }

    [Parameter]
    public bool DisablePanning { get; set; }

    [Parameter]
    public bool DisableDeselecting { get; set; }

    [Parameter]
    public bool DisableSelecting { get; set; }

    [Parameter]
    public bool DisableBoxSelection { get; set; }

    [Parameter]
    public bool DisableMoveEditMode { get; set; }

    [Parameter]
    public bool DisableMoveAnchorEditMode { get; set; }

    [Parameter]
    public bool DisableRemoveElement { get; set; }

    [Parameter]
    public bool DisableCopyElement { get; set; }

    [Parameter]
    public bool DisablePasteElement { get; set; }

    [Parameter]
    public bool DisableScaleLabel { get; set; }

    [Parameter]
    public List<CompleteNewShapeMenuItem> CompleteNewShapeMenuItems { get; set; } = new() { 
        new(typeof(CompleteWithoutCloseMenuItem), (svgEditor) => svgEditor.SelectedShapes[0] is Path),
        new(typeof(RemoveLastInstruction), (svgEditor) => svgEditor.SelectedShapes[0] is Path),
    };

    [Parameter]
    public List<AddNewSVGElementMenuItem> AddNewSVGElementMenuItems { get; set; } = new() {
        new(typeof(AddNewStopFromLinearGradientMenuItem), (svgEditor, data) => data is LinearGradient),
        new(typeof(AddNewStopFromStopMenuItem), (svgEditor, data) => data is Stop),
        new(typeof(AddNewPathMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewPolygonMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewPolylineMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewLineMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewCircleMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewEllipseMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewRectMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewTextMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewGradientMenuItem), (_, data) => data is not (LinearGradient or Stop)),
        new(typeof(AddNewAnimationMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement && !shape.AnimationElements.Any(a => a.AttributeName is "fill" or "stroke" or "d")),
    };

    [Parameter]
    public List<ActionMenuItem> ActionMenuItems { get; set; } = new() {
        new(typeof(FillMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement),
        new(typeof(StrokeMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement),
        new(typeof(TextMenuItem), (_, data) => data is Text text && !text.IsChildElement),
        new(typeof(AnimationsMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement && shape.HasAnimation),
        new(typeof(MoveMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement),
        new(typeof(ScaleMenuItem), (_, data) => data is Path path && !path.IsChildElement),
        new(typeof(GroupMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement),
        new(typeof(UngroupMenuItem), (_, data) => data is G g && !g.IsChildElement),
        new(typeof(RemoveMenuItem), (svgEditor, data) => data is Shape && !svgEditor.DisableRemoveElement),
        new(typeof(CopyMenuItem), (svgEditor, data) => data is Shape && !svgEditor.DisableCopyElement),
        new(typeof(PasteMenuItem), (svgEditor, _) => !svgEditor.DisablePasteElement),
        new(typeof(OptimizeMenuItem), (_, data) => data is Shape shape && !shape.IsChildElement),
    };

    public bool ShouldShowContextMenu(object? data) => !DisableContextMenu
        && (data is null || data is Shape { IsChildElement: false })
        && ((SelectedShapes.Count == 1 && EditMode == EditMode.Add)
            || AddNewSVGElementMenuItems.Any(item => item.ShouldBePresented(this, data))
            || data is Stop
            || ActionMenuItems.Any(item => item.ShouldBePresented(this, data))
        );

    internal IDocument Document { get; set; } = default!;

    public double Scale { get; set; } = 1;

    public (double x, double y) Translate = (0, 0);

    public (double x, double y) LastRightClick { get; set; }

    public Box? SelectionBox { get; set; }

    public List<ISVGElement> Elements { get; internal set; } = default!;

    public List<Shape> SelectedShapes { get; set; } = new();

    public Dictionary<string, ISVGElement> Definitions { get; set; } = new();

    public ISVGElement? EditGradient { get; set; }

    public Shape? FocusedShape { get; set; }

    public List<Shape> MoveOverShapes { get; set; } = new();

    public (double x, double y) MovePanner { get; set; }

    public int? CurrentAnchor { get; set; }

    public Shape? CurrentEditShape { get; set; }

    private EditMode editMode = EditMode.None;
    public EditMode EditMode
    {
        get => editMode;
        set
        {
            if (value is EditMode.Move && DisableMoveEditMode) return;
            if (value is EditMode.MoveAnchor && DisableMoveAnchorEditMode) return;
            editMode = value;
        }
    }

    public List<Shape> MarkedShapes =>
        FocusedShape != null && !SelectedShapes.Contains(FocusedShape) ?
        SelectedShapes.Append(FocusedShape).ToList() :
        SelectedShapes;

    public List<Shape> VisibleSelectionShapes =>
        BoxSelectionShapes is not null ?
        BoxSelectionShapes.ToList() :
        MarkedShapes;

    public string? PreviousColor { get; set; }

    public Dictionary<string, Type> SupportedTypes { get; set; } = new Dictionary<string, Type> {
            { "RECT", typeof(Rect) },
            { "CIRCLE", typeof(Circle) },
            { "ELLIPSE", typeof(Ellipse) },
            { "POLYGON", typeof(Polygon) },
            { "POLYLINE", typeof(Polyline) },
            { "LINE", typeof(Line) },
            { "PATH", typeof(Path) },
            { "TEXT", typeof(Text) },
            { "G", typeof(G) },
            { "DEFS", typeof(Defs) },
        };

    protected override async Task OnParametersSetAsync()
    {
        if (Input == _input)
        {
            return;
        }
        _input = Input;

        Definitions.Clear();
        ClearSelectedShapes();

        IBrowsingContext context = BrowsingContext.New();
        Document = await context.OpenAsync(req => req.Content(Input));

        Elements = Document.GetElementsByTagName("BODY")[0].Children.Select(child =>
        {
            ISVGElement? sVGElement = SupportedTypes.TryGetValue(child.TagName, out Type? type)
                ? Activator.CreateInstance(type, child, this) as ISVGElement
                : throw new NotImplementedException($"Tag not supported:\n {child.OuterHtml}");
            if (sVGElement is not null)
            {
                sVGElement.Changed = UpdateInput;
            }
            return sVGElement!;
        }
        ).ToList();

        Elements.ForEach(e => e.UpdateHtml());
    }

    protected override void OnInitialized()
    {
        _ = ElementSubject
            .Buffer(TimeSpan.FromMilliseconds(33))
            .Where(updates => updates.Count > 0)
            .Subscribe(updates =>
            {
                updates
                    .Distinct()
                    .ToList()
                    .ForEach(element => element.UpdateHtml());
                UpdateInput();
            });

        moduleTask = new(async () => await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/KristofferStrube.Blazor.SVGEditor/KristofferStrube.Blazor.SVGEditor.js"));
    }

    public void UpdateInput(ISVGElement SVGElement)
    {
        ElementSubject.OnNext(SVGElement);
    }

    public void SelectShape(Shape shape)
    {
        if (DisableSelecting) return;
        SelectedShapes.Add(shape);
    }

    public void ClearSelectedShapes()
    {
        if (DisableDeselecting) return;
        SelectedShapes.Clear();
    }

    public void FocusShape(Shape shape)
    {
        if (DisableSelecting) return;
        FocusedShape = shape;
    }

    public void UnfocusShape()
    {
        if (DisableDeselecting) return;
        FocusedShape = null;
    }

    public void AddElement(ISVGElement SVGElement, ISVGElement? parent = null)
    {
        if (parent is null)
        {
            Elements.Add(SVGElement);
            SVGElement.UpdateHtml();
            _ = Document.GetElementsByTagName("BODY")[0].AppendElement(SVGElement.Element);
        }
        else
        {
            _ = parent.Element.AppendChild(SVGElement.Element);
            parent.Changed?.Invoke(parent);
        }
        UpdateInput();
    }

    public void AddDefinition(ISVGElement SVGElement)
    {
        ISVGElement? firstDefs = Elements.Where(e => e is Defs).FirstOrDefault();
        if (firstDefs is Defs defs)
        {
            defs.Children.Add(SVGElement);
            SVGElement.Changed = defs.UpdateInput;
            AddElement(SVGElement, defs);
        }
        else
        {
            IElement element = Document.CreateElement("DEFS");
            var newDefs = new Defs(element, this)
            {
                Changed = UpdateInput
            };
            newDefs.Children.Add(SVGElement);
            SVGElement.Changed = newDefs.UpdateInput;
            AddElement(newDefs);
            AddElement(SVGElement, newDefs);
        }
    }

    public void RemoveElement(ISVGElement SVGElement, ISVGElement? parent = null)
    {
        if (parent is null)
        {
            _ = Elements.Remove(SVGElement);
        }
        else
        {
            _ = parent.Element.RemoveChild(SVGElement.Element);
            parent.Changed?.Invoke(parent);
        }
    }

    private void UpdateInput()
    {
        _input = string.Join(" \n", Elements.Select(e => e.StoredHtml));
        InputUpdated?.Invoke(_input);
    }

    private void RerenderAll()
    {
        Elements.ForEach(e => e.Rerender());
    }

    public (double x, double y) LocalTransform((double x, double y) pos)
    {
        return ((pos.x * Scale) + Translate.x, (pos.y * Scale) + Translate.y);
    }

    public (double x, double y) LocalDetransform((double x, double y) pos)
    {
        (double x, double y) res = (x: (pos.x - Translate.x) / Scale, y: (pos.y - Translate.y) / Scale);
        return SnapToInteger ? ((double x, double y))((int)res.x, (int)res.y) : res;
    }

    private void ZoomIn(double x, double y, double ZoomFactor = 1.1)
    {
        if (DisableZoom) return;

        double prevScale = Scale;
        Scale *= ZoomFactor;
        if ((Scale > 0.91) && (Scale < 1.09))
        {
            Scale = 1;
        }
        Translate = (Translate.x + ((x - Translate.x) * (1 - (Scale / prevScale))), Translate.y + ((y - Translate.y) * (1 - (Scale / prevScale))));
    }

    private void ZoomOut(double x, double y, double ZoomFactor = 1.1)
    {
        if (DisableZoom) return;

        double prevScale = Scale;
        Scale /= ZoomFactor;
        if ((Scale > 0.91) && (Scale < 1.09))
        {
            Scale = 1;
        }
        Translate = (Translate.x + ((x - Translate.x) * (1 - (Scale / prevScale))), Translate.y + ((y - Translate.y) * (1 - (Scale / prevScale))));
    }
}