using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What is dragged, as CEF holds it and as our drag carries it: text, HTML, a link and files.</summary>
internal static unsafe class CefDragDatas
{
    /// <summary>What a page drags, as our drag's payload; the link's address stands for the text where there is none.
    /// </summary>
    public static DataPackage ToPackage(CefDragData* data)
    {
        var package = new DataPackage();
        var text = CefStrings.ReadAndFree(data->get_fragment_text(data));
        var link = data->is_link(data) != 0 ? CefStrings.ReadAndFree(data->get_link_url(data)) : string.Empty;
        if (text.Length > 0 || link.Length > 0)
        {
            package.Set(DataFormats.Text, text.Length > 0 ? text : link);
        }

        var html = CefStrings.ReadAndFree(data->get_fragment_html(data));
        if (html.Length > 0)
        {
            package.Set(DataFormats.Html, html);
        }

        if (data->is_file(data) != 0)
        {
            var list = CefInterop.CefStringListAlloc();
            try
            {
                if (data->get_file_paths(data, list) != 0)
                {
                    var files = new string[CefInterop.CefStringListSize(list)];
                    for (var i = 0; i < files.Length; i++)
                    {
                        var value = new CefStringUtf16();
                        CefInterop.CefStringListValue(list, (ulong)i, &value);
                        files[i] = CefStrings.Read(&value);
                        CefStrings.Clear(&value);
                    }

                    package.Set(DataFormats.Files, files);
                }
            }
            finally
            {
                CefInterop.CefStringListFree(list);
            }
        }

        return package;
    }

    /// <summary>What our drag carries, as CEF takes it - a new object with its reference for CEF. Text stays text, not a
    /// link the page would go to where it is dropped.</summary>
    public static CefDragData* FromPackage(IDataPackage package)
    {
        var data = CefInterop.CefDragDataCreate();
        if (package.Get(DataFormats.Text) is string text && text.Length > 0)
        {
            Set(data, data->set_fragment_text, text);
        }

        if (package.Get(DataFormats.Html) is string html && html.Length > 0)
        {
            Set(data, data->set_fragment_html, html);
        }

        if (package.Get(DataFormats.Files) is string[] files)
        {
            foreach (var file in files)
            {
                var path = new CefStringUtf16();
                var name = new CefStringUtf16();
                try
                {
                    CefStrings.Set(&path, file);
                    CefStrings.Set(&name, Path.GetFileName(file));
                    data->add_file(data, &path, &name);
                }
                finally
                {
                    CefStrings.Clear(&path);
                    CefStrings.Clear(&name);
                }
            }
        }

        return data;
    }

    private static void Set(CefDragData* data, delegate* unmanaged<CefDragData*, CefStringUtf16*, void> setter, string text)
    {
        var value = new CefStringUtf16();
        try
        {
            CefStrings.Set(&value, text);
            setter(data, &value);
        }
        finally
        {
            CefStrings.Clear(&value);
        }
    }
}
