using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Views;

public partial class PersonnelView : UserControl
{
    const double DragThreshold = 6;

    OperatorChipViewModel? _dragChip;
    Point _start;
    bool _dragging;
    int _target;

    public PersonnelView()
    {
        InitializeComponent();
        // Tunnel + handledEventsToo: the chip is a Button that marks press events handled.
        LiveStrip.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        LiveStrip.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        LiveStrip.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Taking capture from the chip's Button raises a capture-lost event for the Button; only the strip losing it ends the drag.
        LiveStrip.AddHandler(PointerCaptureLostEvent, (_, e) => { if (ReferenceEquals(e.Source, LiveStrip)) Cancel(); },
            RoutingStrategies.Tunnel, handledEventsToo: true);
        LiveStrip.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) Cancel(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    PersonnelViewModel? Vm => DataContext as PersonnelViewModel;

    static OperatorChipViewModel? ChipAt(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<OperatorChipViewModel>().FirstOrDefault();

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(LiveStrip).Properties.IsLeftButtonPressed) return;
        var chip = ChipAt(e.Source);
        if (chip == null || chip.IsDead) return;
        _dragChip = chip;
        _start = e.GetPosition(LiveStrip);
        _dragging = false;
    }

    List<Control> Containers()
    {
        var list = new List<Control>();
        for (int i = 0; i < LiveStrip.ItemCount; i++)
            if (LiveStrip.ContainerFromIndex(i) is Control c) list.Add(c);
        return list;
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragChip == null || Vm == null) return;
        var pos = e.GetPosition(LiveStrip);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _start.X) + Math.Abs(pos.Y - _start.Y) < DragThreshold) return;
            _dragging = true;
            _dragChip.IsDragging = true;
            e.Pointer.Capture(LiveStrip);              // from here on this is a drag, not a click
        }
        var others = Containers().Where(c => c.DataContext != _dragChip).ToList();
        var centers = others.Select(c => c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), LiveStrip)?.X ?? double.MaxValue).ToList();
        _target = DropIndex(centers, pos.X);
        foreach (var c in Vm.Live) { c.DropBefore = false; c.DropAfter = false; }
        var chips = others.Select(c => (OperatorChipViewModel)c.DataContext!).ToList();
        if (chips.Count == 0) return;
        if (_target < chips.Count) chips[_target].DropBefore = true; else chips[^1].DropAfter = true;
    }

    /// <summary>How many of the other chips lie left of the pointer: the dragged chip is dropped after that many.</summary>
    public static int DropIndex(IReadOnlyList<double> otherCentersX, double pointerX) => otherCentersX.Count(x => x < pointerX);

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var chip = _dragChip;
        bool dragged = _dragging;
        int target = _target;
        Cancel();
        if (chip != null && dragged) Vm?.MoveTo(chip.Guid, target);
    }

    void Cancel()
    {
        Vm?.ClearDropMarks();
        _dragChip = null;
        _dragging = false;
    }
}
