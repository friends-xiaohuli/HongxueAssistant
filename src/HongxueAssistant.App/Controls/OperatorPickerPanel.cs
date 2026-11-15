using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HongxueAssistant.App.Controls;

/// <summary>The picker's fixed two-column, 60-DIP rows, realized only around the visible viewport.</summary>
public sealed class OperatorPickerPanel : VirtualizingPanel, IScrollInfo
{
    private const double RowHeight = 60;
    private Size _extent;
    private Size _viewport;
    private double _offset;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = ItemsControl.GetItemsOwner(this).Items.Count;
        var extentHeight = (count + 1) / 2 * RowHeight;
        var viewport = new Size(availableSize.Width, double.IsInfinity(availableSize.Height) ? extentHeight : availableSize.Height);
        var extent = new Size(viewport.Width, extentHeight);
        var offset = Math.Clamp(_offset, 0, Math.Max(0, extent.Height - viewport.Height));
        if (_extent != extent || _viewport != viewport || _offset != offset)
        {
            _extent = extent;
            _viewport = viewport;
            _offset = offset;
            ScrollOwner?.InvalidateScrollInfo();
        }

        var first = Math.Min(count, (int)(_offset / RowHeight) * 2);
        var last = Math.Min(count - 1, (int)Math.Ceiling((_offset + viewport.Height) / RowHeight) * 2 + 1);
        // WPF initializes a newly attached items host's generator when InternalChildren is accessed.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last) continue;
            ((IRecyclingItemContainerGenerator)generator).Recycle(position, 1);
            RemoveInternalChildRange(i, 1);
        }

        if (first <= last)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
            {
                for (var index = first; index <= last; index++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out _);
                    // Recycled containers may already be realized in the generator but detached from the panel.
                    if (VisualTreeHelper.GetParent(child) != this)
                    {
                        if (childIndex == InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(viewport.Width / 2, RowHeight));
                }
            }
        }
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(index % 2 * finalSize.Width / 2,
                index / 2 * RowHeight - _offset, finalSize.Width / 2, RowHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        else if (args.ItemUICount > 0)
        {
            if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
            else if (args.Action == NotifyCollectionChangedAction.Move)
                RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
        }
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        var top = index / 2 * RowHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + RowHeight > _offset + _viewport.Height)
            SetVerticalOffset(top + RowHeight - _viewport.Height);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
        if (bounds.Top < 0) SetVerticalOffset(_offset + bounds.Top);
        else if (bounds.Bottom > _viewport.Height) SetVerticalOffset(_offset + bounds.Bottom - _viewport.Height);
        return bounds;
    }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (_offset == offset) return;
        _offset = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void LineUp() => SetVerticalOffset(_offset - RowHeight);
    public void LineDown() => SetVerticalOffset(_offset + RowHeight);
    public void PageUp() => SetVerticalOffset(_offset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(_offset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(_offset - RowHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset + RowHeight * 3);
    public void SetHorizontalOffset(double offset) { }
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
