using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Resources.Triggers;

/// <summary>One test of a multi-condition trigger (the WPF Condition): a <see cref="MultiTrigger"/>'s is a property of the
/// host, <see cref="Property"/>; a <see cref="MultiDataTrigger"/>'s is a <see cref="Binding"/> on its data. <see cref="Value"/>
/// is read as the type of what it is compared with, as a <see cref="PropertyTrigger"/>'s or a <see cref="DataTrigger"/>'s.</summary>
public class Condition
{
    /// <summary>The host's property a <see cref="MultiTrigger"/> tests.</summary>
    public string Property { get; set; }

    /// <summary>What a <see cref="MultiDataTrigger"/> tests: a binding on the host's data.</summary>
    public Binding Binding { get; set; }

    public object Value { get; set; }
}
