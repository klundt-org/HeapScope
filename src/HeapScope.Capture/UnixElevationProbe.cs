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


namespace HeapScope.Capture;

/// <summary>
/// Elevation off Windows (sprint 2.3.5): whether this process runs as root, from .NET's own cross-platform check. On Linux
/// (sprint 4.1) another process's state follows the user it acts as: a .NET process creates its diagnostics socket as its
/// effective user, so HeapScope reaches it as that user or as root. Another user's process is
/// <see cref="ElevationState.Elevated"/> (it needs root, or HeapScope run as that user), the same user's is not. On macOS
/// it is not looked up yet (sprint 4.2), so it is <see cref="ElevationState.Unknown"/>.
/// </summary>
public sealed class UnixElevationProbe : IElevationProbe
{
    private readonly IProcessFacts _facts;
    private readonly Lazy<int?> _ownUser;

    internal UnixElevationProbe(IProcessFacts facts)
    {
        _facts = facts;
        _ownUser = new(() => facts.EffectiveUserId(Environment.ProcessId));
    }

    public static UnixElevationProbe Instance { get; } = new(CapturePlatform.ProcessFacts());

    public bool IsCurrentProcessElevated => Environment.IsPrivilegedProcess;

    public ElevationState GetState(int processId) => StateOf(_facts.EffectiveUserId(processId), _ownUser.Value);

    /// <summary>
    /// The rule on two effective user ids: the same user's process is not elevated, another user's is (root's included);
    /// unknown when either id could not be read (the process exited).
    /// </summary>
    internal static ElevationState StateOf(int? target, int? own) =>
        target is not { } targetUser || own is not { } ownUser ? ElevationState.Unknown
        : targetUser == ownUser ? ElevationState.NotElevated
        : ElevationState.Elevated;
}
