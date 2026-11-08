using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SecureLab.Api.Tests;

public class TrackedPathsGuardTests
{
    private static string[] FindForbiddenPaths(string[] paths)
    {
        return paths.Where(path =>
        {
            var normalized = path.Replace('\\', '/');
            var fileName = normalized.Split('/').Last();

            return fileName == ".env" || normalized.EndsWith(".local.json", StringComparison.Ordinal);
        }).ToArray();
    }

    private static readonly string[] P =
    {
        "infra/.env.example"
    };
    private static readonly string[] A =
    {
        "infra/.env.example",
        "config/reports.local.json"
    };
    private static readonly string[] B =
    {
        "infra/.env.example"
    };

    [Fact]
    public void P_isAccepted()
    {
        Assert.Empty(FindForbiddenPaths(P));
    }

    [Fact]
    public void A_isRejected()
    {
        var forbidden = FindForbiddenPaths(A);

        Assert.Equal(new[]
        {
            "config/reports.local.json",
        },forbidden);
    }

    [Fact]
    public void B_isAccepted()
    {
        Assert.Empty(FindForbiddenPaths(B));
    }

    [Fact]
    public void OwnLists_MatchPrediction()
    {
        var control = new[]
        {
            "infra/.env.example",
            "docs/readme.md"
        };

        var boundary = new[]
        {
            "infra/.env.example",
            "config/custom.local.json"
        };

        Assert.Empty(FindForbiddenPaths(control));

        Assert.Equal(new[]
        {
            "config/custom.local.json",
        },
        FindForbiddenPaths(boundary));
    }

    private static string RunGit(string arguments)
    {
        var startinfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startinfo) ?? throw new InvalidOperationException("Could not start Git");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode !=0)
        {
            throw new InvalidOperationException($"Git failed: {error}");
        }

        return output;
    }

    [Fact]
    public void CurrentTrackedPaths_AreAccepted()
    {
        var output = RunGit("ls-files -z");

        var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        var forbidden = FindForbiddenPaths(paths);

        Assert.True(forbidden.Length == 0, "Forbidden tracked paths:" + Environment.NewLine + string.Join(Environment.NewLine, forbidden));
    }
}