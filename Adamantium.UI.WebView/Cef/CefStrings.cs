using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>CEF's strings, UTF-16 like .NET's: set from a string (CEF copies it and frees the copy with the string's
/// own destructor), read into one, and the ones CEF hands over to be freed.</summary>
internal static unsafe class CefStrings
{
    /// <summary>Sets <paramref name="target"/> to a copy of <paramref name="text"/>; null clears it.</summary>
    public static void Set(CefStringUtf16* target, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            CefInterop.CefStringUtf16Clear(target);
            return;
        }

        fixed (char* chars = text)
        {
            CefInterop.CefStringUtf16Set((Char16*)chars, (ulong)text.Length, target, 1);
        }
    }

    /// <summary>The text of <paramref name="source"/>; empty for none.</summary>
    public static string Read(CefStringUtf16* source)
    {
        return source == null || source->str == null || source->length == 0
            ? string.Empty
            : new string((char*)source->str, 0, (int)source->length);
    }

    /// <summary>The text of a string CEF handed over to be freed, which it frees.</summary>
    public static string ReadAndFree(CefStringUserfree userfree)
    {
        var source = (CefStringUtf16*)userfree.pointer;
        var text = Read(source);
        if (source != null)
        {
            CefInterop.CefStringUserfreeUtf16Free(new CefStringUserfreeUtf16 { pointer = source });
        }

        return text;
    }

    /// <summary>The texts of a list CEF owns; none for a null list.</summary>
    public static string[] ReadList(CefStringList list)
    {
        if (list.pointer == null)
        {
            return [];
        }

        var count = (int)CefInterop.CefStringListSize(list);
        var texts = new string[count];
        for (var i = 0; i < count; i++)
        {
            var value = new CefStringUtf16();
            CefInterop.CefStringListValue(list, (ulong)i, &value);
            texts[i] = Read(&value);
            Clear(&value);
        }

        return texts;
    }

    /// <summary>Frees what <see cref="Set"/> put into <paramref name="target"/>.</summary>
    public static void Clear(CefStringUtf16* target) => CefInterop.CefStringUtf16Clear(target);
}
