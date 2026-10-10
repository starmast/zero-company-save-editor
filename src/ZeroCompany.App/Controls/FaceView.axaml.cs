using Avalonia;
using Avalonia.Controls;

namespace ZeroCompany.App.Controls;

public partial class FaceView : UserControl
{
    public FaceView() => InitializeComponent();

    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = base.ArrangeOverride(finalSize);
        Ring.CornerRadius = new CornerRadius(Math.Min(finalSize.Width, finalSize.Height) / 2);
        return r;
    }
}
