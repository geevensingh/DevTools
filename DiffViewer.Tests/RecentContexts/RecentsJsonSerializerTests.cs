using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

public class RecentsJsonSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields_ForMixedSides()
    {
        var items = new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree()),
                new DiffSide.CommitIsh("main"),
                new DiffSide.WorkingTree(),
                new DateTimeOffset(2026, 5, 14, 18, 0, 0, TimeSpan.Zero)),
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\bar", new DiffSide.CommitIsh("HEAD~3"), new DiffSide.CommitIsh("feature/foo")),
                new DiffSide.CommitIsh("HEAD~3"),
                new DiffSide.CommitIsh("feature/foo"),
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
        };
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, items);

        var json = RecentsJsonSerializer.Serialize(doc);
        var roundTripped = RecentsJsonSerializer.Deserialize(json);

        roundTripped.Version.Should().Be(RecentsDoc.CurrentVersion);
        roundTripped.Items.Should().HaveCount(2);
        roundTripped.Items[0].Should().Be(items[0]);
        roundTripped.Items[1].Should().Be(items[1]);
    }

    [Fact]
    public void Serialize_UsesTypeDiscriminator_ForWorkingTreeAndCommit()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree()),
                new DiffSide.CommitIsh("main"),
                new DiffSide.WorkingTree(),
                DateTimeOffset.UtcNow),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        json.Should().Contain("\"type\": \"commit\"");
        json.Should().Contain("\"type\": \"workingTree\"");
        json.Should().Contain("\"reference\": \"main\"");
    }

    [Fact]
    public void Serialize_OmitsReferenceField_ForWorkingTree()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.WorkingTree(), new DiffSide.WorkingTree()),
                new DiffSide.WorkingTree(),
                new DiffSide.WorkingTree(),
                DateTimeOffset.UtcNow),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        json.Should().NotContain("\"reference\"");
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize(string.Empty).Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_Whitespace_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize("   \r\n  ").Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize("{ not valid").Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_UnknownFutureVersion_ReturnsEmpty()
    {
        var json = "{\"version\":99,\"items\":[]}";
        RecentsJsonSerializer.Deserialize(json).Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_MissingItems_ReturnsEmpty()
    {
        var json = "{\"version\":1}";
        RecentsJsonSerializer.Deserialize(json).Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_UnknownSideType_SkipsItem()
    {
        var json = """
        {
          "version": 1,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "mystery" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" },
            { "repoPath": "C:/repos/bar", "left": { "type": "commit", "reference": "main" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;
        var doc = RecentsJsonSerializer.Deserialize(json);
        doc.Items.Should().HaveCount(1);
        doc.Items[0].Identity.CanonicalRepoPath.Should().EndWith("bar");
    }

    [Fact]
    public void Deserialize_MissingReferenceForCommitSide_SkipsItem()
    {
        var json = """
        {
          "version": 1,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;
        var doc = RecentsJsonSerializer.Deserialize(json);
        doc.Items.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_PreservesEmptyItems()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, Array.Empty<RecentLaunchContext>());
        var json = RecentsJsonSerializer.Serialize(doc);
        var rt = RecentsJsonSerializer.Deserialize(json);
        rt.Items.Should().BeEmpty();
        rt.Version.Should().Be(RecentsDoc.CurrentVersion);
    }

    [Fact]
    public void RoundTrip_NormalizesLastUsedUtcToUtc_RegardlessOfInputOffset()
    {
        var local = new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.FromHours(-5));
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.WorkingTree(), new DiffSide.WorkingTree()),
                new DiffSide.WorkingTree(),
                new DiffSide.WorkingTree(),
                local),
        });

        var rt = RecentsJsonSerializer.Deserialize(RecentsJsonSerializer.Serialize(doc));

        rt.Items[0].LastUsedUtc.UtcDateTime.Should().Be(local.UtcDateTime);
        rt.Items[0].LastUsedUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Serialize_NullDoc_Throws()
    {
        Action act = () => RecentsJsonSerializer.Serialize(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
