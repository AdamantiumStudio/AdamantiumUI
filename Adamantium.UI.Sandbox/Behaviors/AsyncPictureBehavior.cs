using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.UI.Sandbox.Models;
using Adamantium.Imaging;
using Adamantium.UI;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Media.Imaging;
using Image = Adamantium.UI.Controls.Image;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>Fills an <see cref="Image"/> from a <see cref="DroppedPicture"/>, decoding on a background thread; a value
/// converter would decode on the UI thread and freeze it.</summary>
public class AsyncPictureBehavior : Behavior<Image>
{
    /// <summary>
    /// The picture to show, bound as <c>Picture="{Binding}"</c>. A PROPERTY rather than a peek at the host's
    /// DataContext: a behavior is attached while the template is being built, before the item's DataContext exists, so
    /// reading it in OnAttached finds nothing and the tile stays on its spinner forever. A binding arrives when it is
    /// ready, and a recycled container re-triggers it.
    /// </summary>
    public static readonly AdamantiumProperty PictureProperty = AdamantiumProperty.Register(nameof(Picture),
        typeof(DroppedPicture), typeof(AsyncPictureBehavior), new PropertyMetadata(null, OnPictureChanged));

    public DroppedPicture Picture
    {
        get => GetValue(PictureProperty) as DroppedPicture;
        set => SetValue(PictureProperty, value);
    }

    private static void OnPictureChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
        => ((AsyncPictureBehavior)component).Load(e.NewValue as DroppedPicture);

    protected override void OnAttached(Image component) => Load(Picture);

    protected override void OnDetached(Image component) => component.Source = null;

    private void Load(DroppedPicture picture)
    {
        if (picture == null || AssociatedComponent is not { } image) return;

        Task.Run(() =>
        {
            IRawBitmap bitmap = null;
            try
            {
                bitmap = BitmapLoader.Load(new MemoryStream(picture.Bytes));
            }
            catch
            {
                // A picture no decoder recognizes just never appears - it must not take the list down with it.
            }

            // Back onto the LOOP thread, which is the one that owns the visual tree - hence Post, not Invoke: Invoke
            // runs the work on the message-pump thread instead, where touching layout/render state races the loop.
            UIAppContext.Current.Dispatcher.Post(() =>
            {
                if (!ReferenceEquals(Picture, picture)) return;   // container recycled onto another item meanwhile
                if (bitmap != null) image.Source = new BitmapImage(bitmap);
                picture.IsLoading = false;
            });
        });
    }
}
