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
/// Which method the code at an address belongs to (sprint 4.3), as rundowns and method events say it. A method loaded where
/// another was takes its place from then on. Bounded: past <see cref="MaxMethods"/> or <see cref="MaxTextBytes"/> no more are kept.
/// </summary>
internal sealed class LiveMethodNames
{
    public const int MaxMethods = 400_000;

    /// <summary>Names of 45,600 methods take about 10 MB.</summary>
    public const long MaxTextBytes = 64L * 1024 * 1024;

    // By start address. Methods come mostly in runs, so an insert moves little.
    private readonly List<ulong> _starts = [];
    private readonly List<(ulong End, string Name)> _methods = [];
    private long _textBytes;

    public int Count => _starts.Count;

    /// <summary>Methods left out because the limit was reached.</summary>
    public long LeftOut { get; private set; }

    /// <summary>Bumped when a method is added: unnamed addresses may have a name now.</summary>
    public int Version { get; private set; }

    public void Add(ulong start, uint size, string name)
    {
        if (size == 0 || start == 0)
        {
            return;
        }

        var end = start + size < start ? ulong.MaxValue : start + size;
        var at = _starts.Count > 0 && start > _starts[^1] ? ~_starts.Count : _starts.BinarySearch(start);
        if (at >= 0)
        {
            if (_methods[at] == (end, name))
            {
                return;
            }

            _textBytes += Bytes(name) - Bytes(_methods[at].Name);
            _methods[at] = (end, name);
            Version++;
            return;
        }

        if (_starts.Count >= MaxMethods || _textBytes + Bytes(name) > MaxTextBytes)
        {
            LeftOut++;
            return;
        }

        _starts.Insert(~at, start);
        _methods.Insert(~at, (end, name));
        _textBytes += Bytes(name);
        Version++;
    }

    public string? Find(ulong address)
    {
        var at = _starts.BinarySearch(address);
        at = at >= 0 ? at : ~at - 1;
        return at >= 0 && address < _methods[at].End ? _methods[at].Name : null;
    }

    private static long Bytes(string name) => 48 + (2L * name.Length);
}
