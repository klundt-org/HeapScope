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

namespace HeapScope.Architecture.Tests;

/// <summary>
/// The release workflow builds and runs a release before it is public (sprint 4.8, peer review 4.5 M1). Its rules, read from
/// <c>.github/workflows/release.yml</c> by job, since a workflow has no other test before a release: started by hand only; the
/// draft first; every build before anything is attested; the archives that can run here run; the provenance names each file;
/// nothing replaces an attached file; and the draft is published last, by a job that runs no build.
/// </summary>
public sealed class ReleaseWorkflowTests
{
    private static readonly string s_workflow = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "release.yml")).ReplaceLineEndings("\n");

    /// <summary>The text of each job, by its key under <c>jobs:</c>.</summary>
    private static Dictionary<string, string> Jobs()
    {
        var jobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = s_workflow[(s_workflow.IndexOf("\njobs:\n", StringComparison.Ordinal) + 7)..];
        string? name = null;
        var text = new System.Text.StringBuilder();
        foreach (var line in body.Split('\n'))
        {
            if (line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal) && line[2] != ' ' && line.EndsWith(':'))
            {
                if (name is not null)
                {
                    jobs[name] = text.ToString();
                }

                name = line.Trim().TrimEnd(':');
                text.Clear();
                continue;
            }

            text.Append(line).Append('\n');
        }

        jobs[name!] = text.ToString();
        return jobs;
    }

    [Fact]
    public void A_release_is_started_by_hand_and_never_by_publishing_one()
    {
        var on = s_workflow[s_workflow.IndexOf("\non:\n", StringComparison.Ordinal)..s_workflow.IndexOf("\npermissions:", StringComparison.Ordinal)];

        on.ShouldContain("workflow_dispatch:");
        on.ShouldNotContain("\n  release:", customMessage: "a release published by hand would be public before it is built");
        on.ShouldContain("default: false", customMessage: "a run that does not say publish is a dry run");
    }

    [Fact]
    public void The_draft_comes_first_and_the_release_is_published_last_after_every_build_and_check()
    {
        var jobs = Jobs();

        jobs["prepare"].ShouldContain("--draft");
        foreach (var build in new[] { "build", "tool", "sbom" })
        {
            jobs[build].ShouldContain("needs: prepare");
        }

        jobs["attest"].ShouldContain("needs: [prepare, build, tool, tool-check, sbom]");
        jobs["publish"].ShouldContain("needs: attest");
        jobs["publish"].ShouldContain("--draft=false");
        jobs.Where(j => j.Key != "publish").ShouldAllBe(j => !j.Value.Contains("--draft=false"));
    }

    [Fact]
    public void Each_archive_that_can_run_here_runs_before_it_is_kept()
    {
        var build = Jobs()["build"];

        build.ShouldContain("bash build/fixture-dump.sh");
        build.ShouldContain("bash build/check-install.sh");
        build.ShouldContain("check-install-steps.sh /INSTALL.md", customMessage: "INSTALL.md's Linux steps, from the release's own archive");
        build.IndexOf("check-install.sh", StringComparison.Ordinal).ShouldBeLessThan(build.IndexOf("upload-artifact", StringComparison.Ordinal));
        build.Split("runs: true").Length.ShouldBe(3, "win-x64 and linux-x64 run; the macOS builds cannot here");
    }

    [Fact]
    public void Nothing_replaces_a_file_and_the_provenance_names_each_one()
    {
        var jobs = Jobs();

        s_workflow.ShouldNotContain("--clobber");
        jobs["prepare"].ShouldContain("never replaces a release's files");
        jobs["attest"].ShouldContain("subject-path: ${{ steps.files.outputs.subjects }}");
        jobs["attest"].ShouldContain("Files the release does not name");
        jobs["attest"].ShouldNotContain("subject-path: |", customMessage: "a glob would attest whatever lay in the folder");
    }

    // Sprint 4.8 review H6: the provenance names the run's ref and commit, so a release runs on its tag, checks the tag is still that
    // commit, and every job builds the run's own commit: no checkout names a ref (an input or an output would be untrusted code to
    // CodeQL's cache poisoning rule).
    [Fact]
    public void A_release_runs_on_its_tag_and_every_job_builds_the_runs_own_commit()
    {
        var jobs = Jobs();

        jobs["prepare"].ShouldContain("[ \"$GITHUB_REF\" = \"refs/tags/$TAG\" ]");
        jobs["prepare"].ShouldContain("[ \"$tagged\" = \"$GITHUB_SHA\" ]");
        foreach (var (name, job) in jobs.Where(static j => j.Value.Contains("actions/checkout", StringComparison.Ordinal)))
        {
            job.Split('\n').ShouldNotContain(static l => l.TrimStart().StartsWith("ref:", StringComparison.Ordinal), customMessage: name);
        }
    }

    [Fact]
    public void Only_the_jobs_that_build_nothing_may_write()
    {
        foreach (var (name, job) in Jobs())
        {
            var writes = job.Contains("contents: write", StringComparison.Ordinal);
            var attests = job.Contains("attestations: write", StringComparison.Ordinal) || job.Contains("id-token: write", StringComparison.Ordinal);
            var builds = job.Contains("setup-dotnet", StringComparison.Ordinal);
            if (builds)
            {
                (writes || attests).ShouldBeFalse($"{name} builds, so it holds read access only");
            }

            if (writes)
            {
                attests.ShouldBeFalse($"{name} writes releases, so it does not attest too");
            }
        }

        s_workflow[..s_workflow.IndexOf("\njobs:", StringComparison.Ordinal)].ShouldContain("permissions:\n  contents: read");
    }
}
