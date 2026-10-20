using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Adamantium.UI.Core.RoutedEvents;

internal sealed class HandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> ByType = new();

    private readonly Type handlerType;
    private Action<Delegate, object, RoutedEventArgs> call;

    private HandlerInvoker(Type handlerType)
    {
        this.handlerType = handlerType;
    }

    public Type HandlerType => handlerType;

    public static HandlerInvoker For(Type handlerType)
    {
        return ByType.GetOrAdd(handlerType, static type => new HandlerInvoker(type));
    }

    public void Invoke(Delegate handler, object sender, RoutedEventArgs e)
    {
        (call ??= Compile(handlerType))(handler, sender, e);
    }

    private static Action<Delegate, object, RoutedEventArgs> Compile(Type type)
    {
        if (type == typeof(RoutedEventHandler))
        {
            return static (handler, sender, e) => ((RoutedEventHandler)handler)(sender, e);
        }

        var parameters = type.GetMethod("Invoke")?.GetParameters();
        if (parameters is not { Length: 2 } ||
            !IsReference(parameters[0].ParameterType) ||
            !IsReference(parameters[1].ParameterType) ||
            !Related(parameters[1].ParameterType, typeof(RoutedEventArgs)))
        {
            return Dynamic;
        }

        try
        {
            var handlerParameter = Expression.Parameter(typeof(Delegate));
            var senderParameter = Expression.Parameter(typeof(object));
            var argsParameter = Expression.Parameter(typeof(RoutedEventArgs));
            var body = Expression.Invoke(
                Expression.Convert(handlerParameter, type),
                Expression.Convert(senderParameter, parameters[0].ParameterType),
                Expression.Convert(argsParameter, parameters[1].ParameterType));

            return Expression.Lambda<Action<Delegate, object, RoutedEventArgs>>(
                body, handlerParameter, senderParameter, argsParameter).Compile();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Dynamic;
        }
    }

    private static void Dynamic(Delegate handler, object sender, RoutedEventArgs e)
    {
        try
        {
            handler.DynamicInvoke(sender, e);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
        {
            ExceptionDispatchInfo.Throw(wrapped.InnerException);
        }
    }

    private static bool IsReference(Type type)
    {
        return !type.IsValueType && !type.IsByRef && !type.IsPointer;
    }

    private static bool Related(Type a, Type b)
    {
        return a.IsAssignableFrom(b) || b.IsAssignableFrom(a);
    }
}
