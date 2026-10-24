using System;
using System.Collections;
using Adamantium.MVVM;
using Adamantium.UI.Sandbox.Localization;
using DataErrors = System.ComponentModel.INotifyDataErrorInfo;
using DataErrorsChangedEventArgs = System.ComponentModel.DataErrorsChangedEventArgs;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A name the view-model checks as it is typed: none, or one starting with a digit, is wrong - said through
/// <see cref="DataErrors"/>, so the bound box marks itself and the text under it says why.</summary>
[ViewModel]
public partial class ProjectNameCheck : DataErrors
{
    [Bindable, Affects(nameof(Error), nameof(HasErrors))]
    private string _name = "1Game";

    /// <summary>Why the name is wrong; empty when it is not.</summary>
    public string Error => string.IsNullOrWhiteSpace(Name)
        ? TextStrings.NameEmpty
        : char.IsDigit(Name[0]) ? TextStrings.NameStartsWithDigit : string.Empty;

    public bool HasErrors => Error.Length > 0;

    public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

    public IEnumerable GetErrors(string propertyName) =>
        propertyName == nameof(Name) && HasErrors ? new[] { Error } : Array.Empty<string>();

    partial void OnNameChanged(string value) => ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Name)));
}
