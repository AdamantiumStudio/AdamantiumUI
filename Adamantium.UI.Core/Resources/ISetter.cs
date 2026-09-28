namespace Adamantium.UI.Core.Resources;

public interface ISetter
{
   string Property { get; set; }

   Object Value { get; set; }

   public string TargetName { get; set; }

   /// <summary>Position in markup among a collection's triggers; when two write the same property, the later-declared one
   /// wins regardless of firing order.</summary>
   int DeclarationOrder { get; set; }

   /// <summary>How far down a <see cref="Style.BasedOn"/> chain the style that owns this setter stands. Compared BEFORE
   /// <see cref="DeclarationOrder"/>, so a derived style always outranks the base it is built on however the two happen
   /// to be numbered: the more local rule wins, which is the only reading a theme author can hold in their head. A
   /// CheckBox is BasedOn a ToggleButton and must not wear its accent-filled label.</summary>
   int StyleBand { get; set; }


   void Apply(IFundamentalUIComponent component, Style style, ITheme theme);
   
   void Remove(IFundamentalUIComponent component, Style style, ITheme theme);
}