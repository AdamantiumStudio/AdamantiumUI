using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Adamantium.UI.Core.RoutedEvents;

public class RoutedEventArgs:EventArgs
{
   public bool Handled { get; set; }
   public object OriginalSource { get; set; }
   public object Source { get; set; }
   public RoutedEvent RoutedEvent { get; set; }

   public RoutedEventArgs()
   { }

   public RoutedEventArgs(RoutedEvent routedEvent)
   {
      RoutedEvent = routedEvent;
   }

   public RoutedEventArgs(RoutedEvent routedEvent, IUIComponent originalSource)
   {
      RoutedEvent = routedEvent;
      OriginalSource = originalSource;
   }

   internal void Invoke(Delegate handler, object target)
   {
      InvokeEventHandler(handler, target);
   }

   /// <summary>Calls <paramref name="handler"/> for <paramref name="target"/> with these args. A class of args that has
   /// its own handler type overrides this: it calls a handler of that type directly and hands any other to the base, so
   /// a handler is called without reflection and without generating code. A handler of a type no override knows is
   /// called through <see cref="Delegate.DynamicInvoke"/>.</summary>
   protected virtual void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is RoutedEventHandler routed)
      {
         routed(target, this);
         return;
      }

      try
      {
         handler.DynamicInvoke(target, this);
      }
      catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
      {
         ExceptionDispatchInfo.Throw(wrapped.InnerException);
      }
   }
}
