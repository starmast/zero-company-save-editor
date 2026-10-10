using Avalonia.Controls;
using Avalonia.Interactivity;
using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Controls;

public partial class StepperField : UserControl
{
    public StepperField() => InitializeComponent();

    void OnMinus(object? sender, RoutedEventArgs e) => (DataContext as EditableField)?.Step(-1);
    void OnPlus(object? sender, RoutedEventArgs e) => (DataContext as EditableField)?.Step(1);
}
