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

using HeapScope.Core.Graph;

namespace HeapScope.Core.Tests.Graph;

public class TypeNameFormsTests
{
    [Theory]
    [InlineData("System.String", "System.String")]
    [InlineData("System.Collections.Generic.Dictionary<System.Int32, Fixtures.X>", "System.Collections.Generic.Dictionary<System.Int32, Fixtures.X>")]
    [InlineData("System.Collections.Generic.Dictionary<System.Int32, Fixtures.X>+Entry[]", "Entry<System.Int32, Fixtures.X>[]")]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary<System.String, Fixtures.R>+Node", "Node<System.String, Fixtures.R>")]
    [InlineData("Fixtures.Leaks.ClosureLeak+<>c__DisplayClass6_0", "<>c__DisplayClass6_0")]
    [InlineData("Program+<<Main>$>d__0", "<<Main>$>d__0")]
    [InlineData("System.Collections.Generic.List<Outer+Inner>", "System.Collections.Generic.List<Outer+Inner>")]
    [InlineData("A<B>+C<D>[,]", "C<B, D>[,]")]
    public void Nested_types_keep_only_their_own_name_with_the_declaring_arguments(string fullName, string gcdumpName) =>
        TypeNameForms.AsGcDumpName(fullName).ShouldBe(gcdumpName);

    /// <summary>Sprint 4.4: names as allocation ticks have them, of real traces.</summary>
    [Theory]
    [InlineData("System.String", "System.String")]
    [InlineData("System.Byte[]", "System.Byte[]")]
    [InlineData("Dependency[,]", "Dependency[,]")]
    [InlineData("System.Int32[][]", "System.Int32[][]")]
    [InlineData("System.Collections.Generic.Dictionary`2[System.String,System.Int32]", "System.Collections.Generic.Dictionary<System.String, System.Int32>")]
    [InlineData("Entry[System.Int32,Fixtures.Leaks.StaticCacheEntry][]", "Entry<System.Int32, Fixtures.Leaks.StaticCacheEntry>[]")]
    [InlineData("System.Nullable`1[System.ValueTuple`2[System.String,Microsoft.CodeAnalysis.Location]][]", "System.Nullable<System.ValueTuple<System.String, Microsoft.CodeAnalysis.Location>>[]")]
    [InlineData("A`1[B[,]]", "A<B[,]>")]
    [InlineData("A`1[C+D`1[B]]", "A<C+D<B>>")]
    [InlineData("<>c__DisplayClass19_0`2[System.Object,System.Int32]", "<>c__DisplayClass19_0<System.Object, System.Int32>")]
    [InlineData("<<Main>$>d__0", "<<Main>$>d__0")]
    [InlineData("A`1[B", "A`1[B")]
    [InlineData("A]B", "A]B")]
    [InlineData("A`", "A`")]
    public void A_name_of_the_runtime_s_events_is_spelled_as_the_snapshots_spell_it(string recorded, string expected) =>
        TypeNameForms.FromRuntimeEvent(recorded).ShouldBe(expected);

    /// <summary>A trace may hold any name: depth costs no stack.</summary>
    [Fact]
    public void A_deeply_nested_name_is_converted()
    {
        var name = string.Concat(Enumerable.Repeat("A`1[", 20_000)) + "B" + new string(']', 20_000);

        TypeNameForms.AsGcDumpName(TypeNameForms.FromRuntimeEvent(name)).ShouldStartWith("A<A<A<");
    }
}
