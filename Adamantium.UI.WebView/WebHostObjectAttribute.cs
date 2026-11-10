namespace Adamantium.UI.WebView;

/// <summary>
/// Marks an interface whose methods a page may call, once an object of it is added to a <see cref="WebViewBridge"/>
/// with <see cref="WebViewBridge.AddHostObject"/>: the build makes its <see cref="WebHostObject"/> and an
/// <c>AddHostObject</c> for it. A page calls a method by its name in camel case, <c>await
/// adamantium.hostObjects.name.method(...)</c>; arguments and answers come as JSON, an awaited method is awaited, and
/// an interface so marked that a method answers with comes to the page as an object of its own. Methods alone: a
/// property or an event is not the page's, and a name is one method. An object of two such interfaces is added
/// through the one it is for: <c>bridge.AddHostObject(name, new CalculatorHostObject(target), origins)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class WebHostObjectAttribute : Attribute
{
}
