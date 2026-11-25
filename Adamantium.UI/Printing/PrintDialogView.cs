using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Printing;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Printing;

/// <summary>The print dialog's view: the <see cref="PrintDialog"/> control, given what its view model was opened with,
/// and telling it how the dialog was left.</summary>
internal sealed class PrintDialogView : Border
{
    private readonly PrintDialog _dialog = new();
    private PrintDialogViewModel _model;

    public PrintDialogView()
    {
        Name = "PrintDialogView";
        Child = _dialog;
        _dialog.Print += (_, settings) => _model?.Print(settings);
        _dialog.Canceled += (_, _) => _model?.Cancel();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The control the view holds.</summary>
    internal PrintDialog Dialog => _dialog;

    private void OnDataContextChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        _model = DataContext as PrintDialogViewModel;
        if (_model == null)
        {
            return;
        }

        _dialog.CurrentPage = _model.CurrentPage;
        _dialog.Document = _model.Document;
        _dialog.Platform = _model.Platform;
    }
}
