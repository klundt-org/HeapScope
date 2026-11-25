// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/klundt-org/HeapScope
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, version 3 only, with additional terms
// under Section 7 of that license.
//
// Additional terms, including attribution requirements, apply.
// See the ADDITIONAL-TERMS.md file distributed with this program.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program (see the LICENSE file). If not, see
// <https://www.gnu.org/licenses/>.
//
// Commercial licenses are available. See COMMERCIAL.md.

namespace HeapScope.Loaders.Trace;

/// <summary>
/// Which method the code at an address belongs to (sprint 4.3), as rundowns and method events say it. A method loaded over the code
/// of others takes their place from then on. Bounded: past <see cref="MaxMethods"/> or <see cref="MaxTextBytes"/> no more are kept.
/// </summary>
internal sealed class LiveMethodNames
{
    public const int MaxMethods = 300_000;

    /// <summary>Names of 45,600 methods take about 10 MB.</summary>
    public const long MaxTextBytes = 32L * 1024 * 1024;

    // By start address, none over another.
    private List<ulong> _starts = [];
    private List<(ulong End, string Name)> _methods = [];
    private long _textBytes;

    public int Count => _starts.Count;

    /// <summary>Methods left out because the limit was reached.</summary>
    public long LeftOut { get; private set; }

    /// <summary>Bumped when a method is added or gone: addresses may be another method's now.</summary>
    public int Version { get; private set; }

    /// <summary>Many at once, in any order (a rundown): sorted once, where adding them one by one would move the list for each.</summary>
    public void AddAll(IEnumerable<(ulong Start, uint Size, string Name)> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var all = methods.Select((m, i) => (m.Start, m.Size, m.Name, Order: i)).ToList();
        all.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Order.CompareTo(b.Order));
        if (_starts.Count == 0)
        {
            foreach (var (start, size, name, _) in all)
            {
                Add(start, size, name);
            }

            return;
        }

        foreach (var (start, size, name, _) in all.OrderBy(static m => m.Order))
        {
            Add(start, size, name);
        }
    }

    public void Add(ulong start, uint size, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (size == 0 || start == 0)
        {
            return;
        }

        var end = start + size < start ? ulong.MaxValue : start + size;

        // The first method that ends after this one's start: it and those that start before its end are under it.
        var first = _starts.Count > 0 && start >= _methods[^1].End ? _starts.Count : First(start);
        var count = 0;
        while (first + count < _starts.Count && _starts[first + count] < end)
        {
            count++;
        }

        if (count == 1 && _starts[first] == start && _methods[first] == (end, name))
        {
            return;
        }

        Remove(first, count);
        if (_starts.Count >= MaxMethods || _textBytes + Bytes(name) > MaxTextBytes)
        {
            LeftOut++;
            return;
        }

        _starts.Insert(first, start);
        _methods.Insert(first, (end, name));
        _textBytes += Bytes(name);
        Version++;
    }

    /// <summary>The method that starts at <paramref name="start"/> is unloaded: its code may become another's.</summary>
    public void Remove(ulong start)
    {
        var at = _starts.BinarySearch(start);
        if (at >= 0)
        {
            Remove(at, 1);
        }
    }

    public string? Find(ulong address)
    {
        var at = _starts.BinarySearch(address);
        at = at >= 0 ? at : ~at - 1;
        return at >= 0 && address < _methods[at].End ? _methods[at].Name : null;
    }

    /// <summary>Lets go of everything (the recording is over and its stacks are named).</summary>
    public void Clear() => (_starts, _methods, _textBytes) = ([], [], 0);

    private int First(ulong start)
    {
        var at = _starts.BinarySearch(start);
        if (at >= 0)
        {
            return at;
        }

        at = ~at;
        return at > 0 && _methods[at - 1].End > start ? at - 1 : at;
    }

    private void Remove(int at, int count)
    {
        if (count == 0)
        {
            return;
        }

        for (var i = at; i < at + count; i++)
        {
            _textBytes -= Bytes(_methods[i].Name);
        }

        _starts.RemoveRange(at, count);
        _methods.RemoveRange(at, count);
        Version++;
    }

    private static long Bytes(string name) => 48 + (2L * name.Length);
}
