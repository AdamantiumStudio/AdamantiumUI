using System;
using System.Collections;
using System.Collections.Generic;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>Part of a list handed on as a collection, not copied: what takes it copies it once, as it copies a list.
/// Read while the list holds still.</summary>
internal sealed class PdfListRange<T>(List<T> list, int start, int count) : ICollection<T>
{
    public int Count => count;

    public bool IsReadOnly => true;

    public void CopyTo(T[] array, int arrayIndex) => list.CopyTo(start, array, arrayIndex, count);

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = start; i < start + count; i++)
        {
            yield return list[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(T item) => list.IndexOf(item, start, count) >= 0;

    public void Add(T item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Remove(T item) => throw new NotSupportedException();
}
