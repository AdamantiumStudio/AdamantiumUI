using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>A ribbon command that is one of a set - a tool among tools. Drawn as a <see cref="RibbonToggleButton"/>; a press
/// only ever checks it, and the others of its <see cref="GroupName"/> clear. The name does not reach past the
/// <see cref="RibbonGroup"/> the command is in, so two groups may use the same one.</summary>
public class RibbonRadioButton : RibbonToggleButton
{
   public static readonly AdamantiumProperty GroupNameProperty = AdamantiumProperty.Register(nameof(GroupName),
      typeof(string), typeof(RibbonRadioButton), new PropertyMetadata(string.Empty));

   /// <summary>Commands of one ribbon group sharing this name are mutually exclusive; empty is a name too.</summary>
   public string GroupName
   {
      get => GetValue<string>(GroupNameProperty);
      set => SetValue(GroupNameProperty, value);
   }

   protected override void OnToggle()
   {
      if (IsChecked != true)
      {
         SetCurrentValue(IsCheckedProperty, true);
      }
   }

   protected override void OnToggleStateChanged(bool? value)
   {
      if (value != true)
      {
         return;
      }

      foreach (var other in GroupSiblings())
      {
         if (other.IsChecked == true)
         {
            other.SetCurrentValue(IsCheckedProperty, false);
         }
      }
   }

   private IEnumerable<RibbonRadioButton> GroupSiblings()
   {
      var group = OwningGroup();
      if (group == null)
      {
         yield break;
      }

      var pending = new Stack<object>();
      foreach (var item in group.Items)
      {
         pending.Push(item);
      }

      while (pending.Count > 0)
      {
         var item = pending.Pop();
         if (item is RibbonRadioButton radio)
         {
            if (!ReferenceEquals(radio, this) && string.Equals(radio.GroupName ?? string.Empty, GroupName ?? string.Empty))
            {
               yield return radio;
            }

            continue;
         }

         if (item is IUIComponent component)
         {
            foreach (var child in component.VisualChildren)
            {
               pending.Push(child);
            }
         }
      }
   }

   private RibbonGroup OwningGroup()
   {
      IFundamentalUIComponent node = this;
      while (node != null)
      {
         if (node is RibbonGroup group)
         {
            return group;
         }

         if (node.TemplatedParent is RibbonGroup templated)
         {
            return templated;
         }

         if (ItemsControl.AuthoredOwner(node as IUIComponent) is RibbonGroup owner)
         {
            return owner;
         }

         node = (node as IUIComponent)?.VisualParent ?? node.GetLogicalParentOrBridge();
      }

      return null;
   }
}
