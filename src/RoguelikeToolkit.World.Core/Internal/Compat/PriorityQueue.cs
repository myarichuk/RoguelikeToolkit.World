#if !NET6_0_OR_GREATER
namespace System.Collections.Generic;

/// <summary>
/// Minimal netstandard2.1 stand-in for the .NET 6 <c>PriorityQueue&lt;TElement, TPriority&gt;</c> (binary min-heap),
/// covering the members Core uses. Dequeue order for equal priorities is unspecified, exactly as in the BCL type;
/// Core's callers use totally ordered priorities (value, tile index), so results do not depend on the heap shape.
/// </summary>
internal sealed class PriorityQueue<TElement, TPriority>
{
    private (TElement Element, TPriority Priority)[] _nodes = new (TElement, TPriority)[16];
    private readonly IComparer<TPriority> _comparer = Comparer<TPriority>.Default;

    public int Count { get; private set; }

    public void Enqueue(TElement element, TPriority priority)
    {
        if (Count == _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);

        int i = Count++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_comparer.Compare(priority, _nodes[parent].Priority) >= 0) break;
            _nodes[i] = _nodes[parent];
            i = parent;
        }

        _nodes[i] = (element, priority);
    }

    public TElement Dequeue()
    {
        if (!TryDequeue(out var element, out _)) throw new InvalidOperationException("Queue empty.");
        return element;
    }

    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        if (Count == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }

        (element, priority) = _nodes[0];
        var last = _nodes[--Count];
        _nodes[Count] = default;

        if (Count > 0)
        {
            int i = 0;
            while (true)
            {
                int child = (i << 1) + 1;
                if (child >= Count) break;
                if (child + 1 < Count && _comparer.Compare(_nodes[child + 1].Priority, _nodes[child].Priority) < 0) child++;
                if (_comparer.Compare(last.Priority, _nodes[child].Priority) <= 0) break;
                _nodes[i] = _nodes[child];
                i = child;
            }

            _nodes[i] = last;
        }

        return true;
    }

    public void Clear()
    {
        Array.Clear(_nodes, 0, Count);
        Count = 0;
    }
}
#endif
