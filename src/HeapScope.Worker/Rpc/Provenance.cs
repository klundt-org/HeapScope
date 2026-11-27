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


using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeapScope.Contracts;
using HeapScope.Core;

namespace HeapScope.Worker.Rpc;

/// <summary>
/// A request or a result as one log line: what was asked and what was answered, so a disputed
/// result can be traced to its inputs. Numbers, flags, enums, ids, times and the names below are written as they are;
/// lists by their count; any other text by its length only, so field values and string contents never reach the log
/// (ARCHITECTURE.md 11 and 12). The user's filter text is written, quoted, unless the user turned that off on the Settings
/// page; its shape always is. Only when the user turned on Include dump values in logs, and confirmed it
/// (owner, 2026-11-19), is any other text written too, quoted (its first <see cref="MaxValueChars"/>
/// characters, and its length), and a list's first <see cref="MaxListItems"/> items, each described.
/// </summary>
internal static class Provenance
{
    /// <summary>The characters of a value written when dump values are on; its length is written too.</summary>
    internal const int MaxValueChars = 200;

    /// <summary>The items of a list described when dump values are on; the rest are counted.</summary>
    internal const int MaxListItems = 20;

    /// <summary>The longest line written when dump values are on; the rest is cut and said so.</summary>
    internal const int MaxLineChars = 16 * 1024;

    // Texts HeapScope or the runtime's metadata produce, never values read from the dump or typed by the user.
    private static readonly HashSet<string> s_namedTexts = new(StringComparer.Ordinal)
    {
        "TypeName", "Name", "Module", "Path", "CachePath", "CacheDetail", "RuntimeVersion", "ProcessName", "Note",
        "FieldsNote", "UnreachableNote", "Unavailable", "FieldsUnavailable", "DominatorType", "TargetType", "Title",
        "Phase", "Field", "ProductVersion", "Inspection", "Status", "SortColumn", "Column",
    };

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_properties = new();

    // Quoted on one line, readable: escapes only what would break the line or the quotes.
    private static readonly JsonSerializerOptions s_quoted = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Where a retention path starts, for the log (the "held by a static field" case): the root's
    /// kind and HeapScope's description of it, built from metadata names (a static field's name, a thread), never values.
    /// </summary>
    public static string Describe(PathRootDto root) =>
        string.Create(CultureInfo.InvariantCulture, $"{(root.Frozen ? "frozen object" : root.Kind.ToString())}: {root.Description}");

    /// <summary>A key path for the log: its root, its type steps, the objects it explains and their bytes.</summary>
    public static string Describe(KeyPathDto path) => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Describe(path.Root)}] {string.Join(" > ", path.Steps.Select(s => s.TypeName))}{(path.Steps.Count > 0 ? " > " : string.Empty)}{path.TargetType} ({path.Objects} objects, {path.Bytes} bytes)");

    /// <summary>
    /// A Similar Retention group for the log: its number as shown, pattern key, objects, bytes, types and paths
    /// (roots and type names; the paths overlap, each holding the whole group).
    /// </summary>
    public static string Describe(RetentionGroupDto group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"group {group.Number} #{group.Key}: {group.Objects} objects, {group.Bytes} bytes, {group.Types} types from {group.FirstType}, {group.PathCount} paths: {string.Join(" | ", group.Paths.Select(p => $"[{Describe(p.Root)}] {string.Join(" > ", p.Steps.Select(s => s.TypeName))}"))}");
    }

    /// <summary>The value for the log by the user's settings: two levels of nested records deep, four with dump values on.</summary>
    public static string Describe(object? value)
    {
        var privacy = LogPrivacy.Current.Read();
        return Describe(value, privacy.IncludeFilterText, privacy.IncludeDumpValues);
    }

    /// <summary>
    /// The value for the log, with or without the filter's text, and with or without values read from a dump. With values,
    /// records go two levels deeper (an object's struct fields and array elements) and the line stops at
    /// <see cref="MaxLineChars"/>. A value that cannot be described is named by its type: the log line never fails the answer.
    /// </summary>
    internal static string Describe(object? value, bool includeFilterText, bool includeDumpValues = false)
    {
        var text = new StringBuilder();
        try
        {
            Append(text, value, depth: 0, new Privacy(includeFilterText, includeDumpValues));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return string.Create(CultureInfo.InvariantCulture, $"({value?.GetType().Name} not described: {cause.GetType().Name})");
        }

        if (includeDumpValues && text.Length > MaxLineChars)
        {
            var cut = char.IsHighSurrogate(text[MaxLineChars - 1]) ? MaxLineChars - 1 : MaxLineChars;
            text.Length = cut;
            text.Append(CultureInfo.InvariantCulture, $"... (line cut at {cut} chars)");
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, object? value, int depth, Privacy privacy)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                return;
            case string s when privacy.IncludeDumpValues:
                text.Append(JsonSerializer.Serialize(s.Length > MaxValueChars ? s[..(char.IsHighSurrogate(s[MaxValueChars - 1]) ? MaxValueChars - 1 : MaxValueChars)] : s, s_quoted))
                    .Append(s.Length > MaxValueChars ? "... (" : " (")
                    .Append(s.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(" chars)");
                return;
            case string s:
                text.Append('(').Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(" chars)");
                return;
            case bool or Enum or Guid:
                text.Append(value);
                return;
            case IFormattable formattable:
                text.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            case ObjectSetSpec set:
                // A set by its shape: kind, names and counts, never addresses or its cache key.
                text.Append("ObjectSetSpec {").Append(set.Shape).Append('}');
                return;
            case ICollection { Count: > 0 } collection when privacy.IncludeDumpValues:
                // Each item at the list's own depth, so a list of fields shows each field's value.
                text.Append('[').Append(collection.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
                var shown = 0;
                foreach (var item in collection)
                {
                    if (shown == MaxListItems || text.Length > MaxLineChars)
                    {
                        break;
                    }

                    text.Append(shown == 0 ? " " : ", ");
                    Append(text, item, depth, privacy);
                    shown++;
                }

                if (collection.Count > shown)
                {
                    text.Append(CultureInfo.InvariantCulture, $", and {collection.Count - shown} more");
                }

                text.Append(']');
                return;
            case ICollection collection:
                text.Append('[').Append(collection.Count.ToString(CultureInfo.InvariantCulture)).Append(']');
                return;
            case IEnumerable:
                text.Append("[...]");
                return;
        }

        var type = value.GetType();
        text.Append(type.Name);
        if (depth >= (privacy.IncludeDumpValues ? 4 : 2))
        {
            return;
        }

        text.Append(" {");
        var first = true;
        foreach (var property in s_properties.GetOrAdd(type, static t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract")]))
        {
            text.Append(first ? " " : ", ").Append(property.Name).Append('=');
            first = false;
            var propertyValue = property.GetValue(value);
            if (propertyValue is string named && s_namedTexts.Contains(property.Name))
            {
                text.Append(named);
            }
            else if (property.Name == "Filter" && propertyValue is string { Length: > 0 } filter)
            {
                // The filter's shape: "why is my type missing?" needs to know that an exclusion or a module term
                // was there; its text too, unless the user turned that off (owner 2026-11-12).
                if (privacy.IncludeFilterText)
                {
                    text.Append(JsonSerializer.Serialize(filter, s_quoted)).Append(' ');
                }

                var terms = NameFilter.Terms(filter);
                text.Append(CultureInfo.InvariantCulture, $"({filter.Length} chars; {terms.Count(t => !t.Negated && !t.OnModule)} terms, {terms.Count(t => t.Negated)} excluding, {terms.Count(t => t.OnModule)} on the module)");
            }
            else
            {
                Append(text, propertyValue, depth + 1, privacy);
            }
        }

        text.Append(" }");
    }

    private readonly record struct Privacy(bool IncludeFilterText, bool IncludeDumpValues);
}
