using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Automation;

internal static unsafe class UiaSafeArray
{
    public static nint Of(params int[] values)
    {
        var array = UiaInterop.SafeArrayCreateVector(UiaInterop.VariantInt, 0, (uint)values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var index = i;
            UiaInterop.SafeArrayPutElement(array, &index, &value);
        }

        return array;
    }

    public static nint Of(double[] values)
    {
        var array = UiaInterop.SafeArrayCreateVector(UiaInterop.VariantDouble, 0, (uint)values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var index = i;
            UiaInterop.SafeArrayPutElement(array, &index, &value);
        }

        return array;
    }

    public static nint NoObjects() => UiaInterop.SafeArrayCreateVector(UiaInterop.VariantUnknown, 0, 0);

    public static nint Of<T>(IReadOnlyList<T> objects) where T : class
    {
        if (objects.Count == 0)
        {
            return 0;
        }

        var array = UiaInterop.SafeArrayCreateVector(UiaInterop.VariantUnknown, 0, (uint)objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            var pointer = UiaBridge.ComPointer(objects[i]);
            var index = i;
            UiaInterop.SafeArrayPutElement(array, &index, (void*)pointer);
            Marshal.Release(pointer);
        }

        return array;
    }
}
