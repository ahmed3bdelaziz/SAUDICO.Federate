using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.ACC.Errors;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// Recursive RVT search (current folder + subfolders, entire project),
/// pagination hardening, and cloud-model classification. Reuses the same
/// <see cref="FakeDataManagementTransport"/>/<see cref="FakeAuthenticationServiceForDataManagement"/>
/// fakes as <see cref="AccDataManagementClientTests"/> — no duplicated test infrastructure.
/// </summary>
public sealed class AccDataManagementClientSearchTests
{
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    private static (AccDataManagementClient Client, FakeDataManagementTransport Transport, FakeAuthenticationServiceForDataManagement Auth) CreateClient()
    {
        FakeDataManagementTransport transport = new FakeDataManagementTransport();
        FakeAuthenticationServiceForDataManagement auth = new FakeAuthenticationServiceForDataManagement();
        return (new AccDataManagementClient(transport, auth), transport, auth);
    }

    private const string OneRvtVersionPage = """
    {
      "links": { "self": { "href": "x" } },
      "data": [
        { "type": "versions", "id": "ver-1", "attributes": { "versionNumber": 5, "lastModifiedTime": "2026-02-01T00:00:00Z" },
          "relationships": { "item": { "data": { "type": "items", "id": "item-1" } } } }
      ],
      "included": [
        { "type": "items", "id": "item-1",
          "attributes": { "displayName": "Model.rvt", "pathInProject": "/Project Files/Sub", "lastModifiedTime": "2026-02-01T00:00:00Z", "extension": { "type": "items:autodesk.bim360:File" } },
          "relationships": { "parent": { "data": { "type": "folders", "id": "folder-1" } } } }
      ]
    }
    """;

    [Fact]
    public async Task SearchFolderRecursiveAsync_ReturnsRvtItem_WithApiSuppliedFolderPathVerbatim()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc(OneRvtVersionPage));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        AccBrowseNode result = Assert.Single(outcome.Results);
        Assert.Equal("Model.rvt", result.Name);
        Assert.Equal("item-1", result.ItemId);
        Assert.Equal("ver-1", result.VersionId);
        Assert.Equal(5, result.VersionNumber);
        Assert.Equal("folder-1", result.FolderId);
        // FolderPath must be exactly what attributes.pathInProject supplied — never constructed/guessed.
        Assert.Equal("/Project Files/Sub", result.FolderPath);
        Assert.False(outcome.HasPartialFailure);
        Assert.Contains("/data/v1/projects/proj-1/folders/folder-1/search", transport.RequestedUrls[0]);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_ExtractsCloudModelGuidsFromVersionExtensionData()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [
            { "type": "versions", "id": "ver-1",
              "attributes": { "versionNumber": 1, "extension": { "type": "versions:autodesk.bim360:C4RModel", "data": { "projectGuid": "11111111-1111-1111-1111-111111111111", "modelGuid": "22222222-2222-2222-2222-222222222222" } } },
              "relationships": { "item": { "data": { "type": "items", "id": "item-1" } } } }
          ],
          "included": [ { "type": "items", "id": "item-1", "attributes": { "displayName": "Model.rvt", "pathInProject": "/x" } } ]
        }
        """));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        AccBrowseNode result = Assert.Single(outcome.Results);
        Assert.Equal("versions:autodesk.bim360:C4RModel", result.ExtensionType);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result.CloudProjectGuid);
        Assert.Equal("22222222-2222-2222-2222-222222222222", result.CloudModelGuid);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_ExcludesNonRvtItems()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [ { "type": "versions", "id": "ver-1", "attributes": { "versionNumber": 1 }, "relationships": { "item": { "data": { "type": "items", "id": "item-1" } } } } ],
          "included": [ { "type": "items", "id": "item-1", "attributes": { "displayName": "Notes.docx", "pathInProject": "/Docs" } } ]
        }
        """));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Empty(outcome.Results);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_CaseInsensitiveRvtFilter()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [ { "type": "versions", "id": "ver-1", "attributes": { "versionNumber": 1 }, "relationships": { "item": { "data": { "type": "items", "id": "item-1" } } } } ],
          "included": [ { "type": "items", "id": "item-1", "attributes": { "displayName": "MODEL.RVT", "pathInProject": "/x" } } ]
        }
        """));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Single(outcome.Results);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_FollowsPaginationAcrossPages()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" }, "next": { "href": "https://developer.api.autodesk.com/data/v1/projects/proj-1/folders/folder-1/search?page=2" } },
          "data": [ { "type": "versions", "id": "ver-1", "attributes": { "versionNumber": 1 }, "relationships": { "item": { "data": { "type": "items", "id": "item-1" } } } } ],
          "included": [ { "type": "items", "id": "item-1", "attributes": { "displayName": "A.rvt", "pathInProject": "/x" } } ]
        }
        """));
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [ { "type": "versions", "id": "ver-2", "attributes": { "versionNumber": 1 }, "relationships": { "item": { "data": { "type": "items", "id": "item-2" } } } } ],
          "included": [ { "type": "items", "id": "item-2", "attributes": { "displayName": "B.rvt", "pathInProject": "/x" } } ]
        }
        """));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal(2, transport.RequestedUrls.Count);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_EmptyPageWithNextLink_StillFollowsToNextPage()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"},"next":{"href":"https://developer.api.autodesk.com/data/v1/projects/proj-1/folders/folder-1/search?page=2"}},"data":[]}
        """));
        transport.Responses.Enqueue(() => Doc(OneRvtVersionPage));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Single(outcome.Results);
        Assert.Equal(2, transport.RequestedUrls.Count);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_RepeatedNextLink_StopsWithoutInfiniteLoop()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        const string loopingUrl = "https://developer.api.autodesk.com/data/v1/projects/proj-1/folders/folder-1/search";
        string page = "{\"links\":{\"self\":{\"href\":\"x\"},\"next\":{\"href\":\"" + loopingUrl + "\"}},\"data\":[]}";
        // Every call returns a "next" link pointing back at the very URL just fetched.
        transport.Responses.Enqueue(() => Doc(page));
        transport.Responses.Enqueue(() => Doc(page));
        transport.Responses.Enqueue(() => Doc(page));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Empty(outcome.Results);
        // Stopped after the first fetch instead of looping forever.
        Assert.Single(transport.RequestedUrls);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_NextLinkToUnexpectedHost_IsRejected()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"},"next":{"href":"https://evil.example.com/steal"}},"data":[]}
        """));

        AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Empty(outcome.Results);
        // Only the first, legitimate request was made — the unsafe host was never fetched.
        Assert.Single(transport.RequestedUrls);
    }

    [Fact]
    public async Task SearchFolderRecursiveAsync_AlreadyCancelledToken_ThrowsWithoutCallingTransport()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchFolderRecursiveAsync("proj-1", "folder-1", cts.Token));

        Assert.Empty(transport.RequestedUrls);
    }

    [Fact]
    public async Task SearchProjectAsync_MergesResultsAcrossMultipleTopFolders()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"top-1","attributes":{"name":"Folder One"}},{"type":"folders","id":"top-2","attributes":{"name":"Folder Two"}}]}"""));
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-1","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-1"}}}}],
         "included":[{"type":"items","id":"item-1","attributes":{"displayName":"A.rvt","pathInProject":"/top1"}}]}
        """));
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-2","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-2"}}}}],
         "included":[{"type":"items","id":"item-2","attributes":{"displayName":"B.rvt","pathInProject":"/top2"}}]}
        """));

        AccSearchOutcome outcome = await client.SearchProjectAsync("hub-1", "proj-1", CancellationToken.None);

        Assert.Equal(2, outcome.Results.Count);
        Assert.False(outcome.HasPartialFailure);
    }

    [Fact]
    public async Task SearchProjectAsync_DeduplicatesByItemId_NotDisplayName()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"top-1","attributes":{"name":"Folder One"}},{"type":"folders","id":"top-2","attributes":{"name":"Folder Two"}}]}"""));
        // Same item (item-1) reachable from both top folders.
        string sameItemPage = """
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-1","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-1"}}}}],
         "included":[{"type":"items","id":"item-1","attributes":{"displayName":"Model.rvt","pathInProject":"/x"}}]}
        """;
        transport.Responses.Enqueue(() => Doc(sameItemPage));
        transport.Responses.Enqueue(() => Doc(sameItemPage));

        AccSearchOutcome outcome = await client.SearchProjectAsync("hub-1", "proj-1", CancellationToken.None);

        Assert.Single(outcome.Results);
    }

    [Fact]
    public async Task SearchProjectAsync_DuplicateDisplayNames_DifferentIds_BothRetained()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"top-1","attributes":{"name":"Folder One"}},{"type":"folders","id":"top-2","attributes":{"name":"Folder Two"}}]}"""));
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-1","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-1"}}}}],
         "included":[{"type":"items","id":"item-1","attributes":{"displayName":"Model.rvt","pathInProject":"/top1"}}]}
        """));
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-2","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-2"}}}}],
         "included":[{"type":"items","id":"item-2","attributes":{"displayName":"Model.rvt","pathInProject":"/top2"}}]}
        """));

        AccSearchOutcome outcome = await client.SearchProjectAsync("hub-1", "proj-1", CancellationToken.None);

        Assert.Equal(2, outcome.Results.Count);
        Assert.Contains(outcome.Results, r => r.ItemId == "item-1");
        Assert.Contains(outcome.Results, r => r.ItemId == "item-2");
    }

    [Fact]
    public async Task SearchProjectAsync_OneTopFolder403_KeepsOthersAndReportsPartialFailure()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"top-1","attributes":{"name":"Folder One"}},{"type":"folders","id":"top-2","attributes":{"name":"Folder Two"}}]}"""));

        // Order of concurrent per-folder requests isn't guaranteed, so make both branches valid:
        // one throws 403, the other returns a real result.
        transport.Responses.Enqueue(() => throw new ApsApiException("forbidden", 403, "forbidden"));
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-1","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-1"}}}}],
         "included":[{"type":"items","id":"item-1","attributes":{"displayName":"Model.rvt","pathInProject":"/x"}}]}
        """));

        AccSearchOutcome outcome = await client.SearchProjectAsync("hub-1", "proj-1", CancellationToken.None);

        Assert.True(outcome.HasPartialFailure);
        Assert.Equal(1, outcome.FailedFolderCount);
        Assert.Single(outcome.Results);
    }

    private const string ValidProjectGuid = "11111111-1111-1111-1111-111111111111";
    private const string ValidModelGuid = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData(null, null, null, AccResolutionStatus.Unresolved)]
    [InlineData("items:autodesk.core:File", null, null, AccResolutionStatus.Unresolved)]
    [InlineData("items:autodesk.bim360:File", null, null, AccResolutionStatus.UploadedFile)]
    // Wrong prefix ("items:" instead of "versions:") on an otherwise-plausible C4R-looking type must NOT verify.
    [InlineData("items:autodesk.bim360:C4RModel", ValidProjectGuid, ValidModelGuid, AccResolutionStatus.Unresolved)]
    // Correct type but a missing GUID must NOT verify.
    [InlineData(AccCloudModelClassifier.RevitCloudModelExtensionType, null, ValidModelGuid, AccResolutionStatus.Unresolved)]
    [InlineData(AccCloudModelClassifier.RevitCloudModelExtensionType, ValidProjectGuid, null, AccResolutionStatus.Unresolved)]
    // Correct type but a malformed (non-GUID) value must NOT verify.
    [InlineData(AccCloudModelClassifier.RevitCloudModelExtensionType, "not-a-guid", ValidModelGuid, AccResolutionStatus.Unresolved)]
    // Correct type with both GUIDs present and well-formed DOES verify.
    [InlineData(AccCloudModelClassifier.RevitCloudModelExtensionType, ValidProjectGuid, ValidModelGuid, AccResolutionStatus.CloudModelVerified)]
    public void AccCloudModelClassifier_ClassifiesConservatively(
        string? extensionType, string? projectGuid, string? modelGuid, AccResolutionStatus expected)
    {
        AccResolutionStatus status = AccCloudModelClassifier.Classify(extensionType, projectGuid, modelGuid);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void AccCloudSourceDescriptor_FromSearchResult_UnresolvedItem_NeverSetsProjectGuidOrModelGuid()
    {
        AccBrowseNode node = new AccBrowseNode
        {
            Id = "item-1", ItemId = "item-1", VersionId = "ver-1", Name = "Model.rvt",
            Kind = AccNodeKind.RvtFile, ExtensionType = "items:autodesk.bim360:C4RModel",
            CloudProjectGuid = ValidProjectGuid, CloudModelGuid = ValidModelGuid,
        };

        AccCloudSourceDescriptor descriptor = AccCloudSourceDescriptor.FromSearchResult(node);

        Assert.Equal(AccResolutionStatus.Unresolved, descriptor.ResolutionStatus);
        Assert.Null(descriptor.ProjectGuid);
        Assert.Null(descriptor.ModelGuid);
        Assert.Equal(
            "The selected ACC model has not yet been resolved to a Revit cloud model path.",
            descriptor.ResolutionMessage);
    }

    [Fact]
    public void AccCloudSourceDescriptor_FromSearchResult_VerifiedCloudModel_CarriesExactGuidValues()
    {
        AccBrowseNode node = new AccBrowseNode
        {
            Id = "item-1", ItemId = "item-1", VersionId = "ver-1", Name = "Model.rvt",
            Kind = AccNodeKind.RvtFile,
            ExtensionType = AccCloudModelClassifier.RevitCloudModelExtensionType,
            CloudProjectGuid = ValidProjectGuid, CloudModelGuid = ValidModelGuid,
        };

        AccCloudSourceDescriptor descriptor = AccCloudSourceDescriptor.FromSearchResult(node);

        Assert.Equal(AccResolutionStatus.CloudModelVerified, descriptor.ResolutionStatus);
        Assert.Equal(ValidProjectGuid, descriptor.ProjectGuid);
        Assert.Equal(ValidModelGuid, descriptor.ModelGuid);
    }

    [Fact]
    public void AccCloudSourceDescriptor_FromSearchResult_MalformedGuidOnCorrectType_StaysUnresolved_NeverFabricates()
    {
        AccBrowseNode node = new AccBrowseNode
        {
            Id = "item-1", ItemId = "item-1", Name = "Model.rvt", Kind = AccNodeKind.RvtFile,
            ExtensionType = AccCloudModelClassifier.RevitCloudModelExtensionType,
            CloudProjectGuid = "garbage-not-a-guid", CloudModelGuid = ValidModelGuid,
        };

        AccCloudSourceDescriptor descriptor = AccCloudSourceDescriptor.FromSearchResult(node);

        Assert.Equal(AccResolutionStatus.Unresolved, descriptor.ResolutionStatus);
        Assert.Null(descriptor.ProjectGuid);
        Assert.Null(descriptor.ModelGuid);
    }

    [Fact]
    public void AccCloudSourceDescriptor_UploadedFile_HasUploadedFileMessage_StillNotCloudOpenable()
    {
        AccBrowseNode node = new AccBrowseNode
        {
            Id = "item-1", ItemId = "item-1", Name = "Plain.rvt", Kind = AccNodeKind.RvtFile,
            ExtensionType = "items:autodesk.bim360:File",
        };

        AccCloudSourceDescriptor descriptor = AccCloudSourceDescriptor.FromSearchResult(node);

        Assert.Equal(AccResolutionStatus.UploadedFile, descriptor.ResolutionStatus);
        Assert.Contains("not cloud-openable", descriptor.ResolutionMessage, StringComparison.OrdinalIgnoreCase);
    }
}
