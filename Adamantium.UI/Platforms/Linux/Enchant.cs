using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The part of libenchant-2 spelling needs.</summary>
internal static unsafe class Enchant
{
    private const string Library = "libenchant-2.so.2";

    [DllImport(Library, EntryPoint = "enchant_broker_init")]
    public static extern nint BrokerInit();

    [DllImport(Library, EntryPoint = "enchant_broker_dict_exists")]
    public static extern int BrokerDictExists(nint broker, byte* tag);

    [DllImport(Library, EntryPoint = "enchant_broker_request_dict")]
    public static extern nint BrokerRequestDict(nint broker, byte* tag);

    [DllImport(Library, EntryPoint = "enchant_dict_check")]
    public static extern int DictCheck(nint dict, byte* word, nint length);

    [DllImport(Library, EntryPoint = "enchant_dict_suggest")]
    public static extern byte** DictSuggest(nint dict, byte* word, nint length, nuint* count);

    [DllImport(Library, EntryPoint = "enchant_dict_free_string_list")]
    public static extern void DictFreeStringList(nint dict, byte** list);

    [DllImport(Library, EntryPoint = "enchant_dict_add")]
    public static extern void DictAdd(nint dict, byte* word, nint length);

    [DllImport(Library, EntryPoint = "enchant_dict_add_to_session")]
    public static extern void DictAddToSession(nint dict, byte* word, nint length);
}
